// ---------------------------------------------------------------------------
// AudioSpectrum.cs
//
/// Real frequency bands for the "音乐律动条" module (phase B).
///
/// Phase A moved every bar with the same number, which reads as a breathing arch rather than a
/// spectrum. This file adds the missing half: capture the default output device in loopback
/// (WASAPI), keep the newest 1024 mono samples, and turn them into N log-spaced band levels.
///
/// Cost, measured on the target machine: one background thread that memcpy's ~192 kB/s and a
/// 1024-point FFT per rendered frame (~30 us). No third-party code; the COM interfaces are
/// hand-declared below in vtable order, exactly like AudioPeak does for the peak meter.
// ---------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Amc
{
    public static class AudioSpectrum
    {
        // ---- COM ------------------------------------------------------------


        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, [MarshalAs(UnmanagedType.Interface)] out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                         [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioClient
        {
            // Order matters: this is the vtable of audioclient.h.
            int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity,
                           IntPtr format, ref Guid sessionGuid, IntPtr sessionHandle);
            int GetBufferSize(out uint frames);
            int GetStreamLatency(out long latency);
            int GetCurrentPadding(out uint padding);
            int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
            int GetMixFormat(out IntPtr format);
            int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
            int Start();
            int Stop();
            int Reset();
            int SetEventHandle(IntPtr handle);
            int GetService(ref Guid iid, out IntPtr service);
        }

        [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioCaptureClient
        {
            int GetBuffer(out IntPtr data, out uint frames, out uint flags,
                          out ulong devicePosition, out ulong qpcPosition);
            int ReleaseBuffer(uint frames);
            int GetNextPacketSize(out uint frames);
        }

        const int eRender = 0;
        const int eConsole = 0;
        const int CLSCTX_ALL = 0x17;
        const int AUDCLNT_SHAREMODE_SHARED = 0;
        const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
        const int AUDCLNT_BUFFERFLAGS_SILENT = 0x00000002;

        // ---- state ---------------------------------------------------------

        const int FFTSize = 1024;
        const int RingSize = FFTSize * 4;

        static readonly object _lock = new object();
        static readonly float[] _ring = new float[RingSize];
        static int _ringWrite;
        static long _ringTotal;                  // samples ever written, for "is this stale?"
        static int _sampleRate = 48000;
        static int _channels = 2;

        static Thread _thread;
        static volatile bool _run;
        static int _failures;

        /// <summary>Last failure reason, for the card's log line. Empty while healthy.</summary>
        public static string LastError = "";

        // Per-band smoothing survives across frames so the bars do not chatter.
        static float[] _smooth;

        /// <summary>True once the capture thread has produced samples recently.</summary>
        public static bool Available
        {
            get
            {
                lock (_lock)
                {
                    if (_ringTotal <= 0) return false;
                    // Stale for more than half a second means the device went away.
                    return (Environment.TickCount - _lastWriteTick) < 500;
                }
            }
        }

        static int _lastWriteTick = -100000;

        /// <summary>Start capturing. Safe to call repeatedly; a no-op once running.</summary>
        public static void Start()
        {
            lock (_lock)
            {
                if (_thread != null) return;
                _run = true;
                Thread t = new Thread(Loop);
                t.IsBackground = true;
                t.Name = "AmcSpectrum";
                t.Priority = ThreadPriority.BelowNormal;
                _thread = t;
                t.Start();
            }
        }

        public static void Stop()
        {
            _run = false;
            _thread = null;
        }

        // ---- capture -------------------------------------------------------

        static void Loop()
        {
            IAudioClient client = null;
            IAudioCaptureClient capture = null;
            IntPtr mixPtr = IntPtr.Zero;

            while (_run)
            {
                if (client == null)
                {
                    string why;
                    if (!Open(out client, out capture, out mixPtr, out why))
                    {
                        LastError = why;
                        _failures++;
                        Thread.Sleep(2000);
                        continue;
                    }
                    _failures = 0;
                    LastError = "";
                }

                try
                {
                    uint packet;
                    capture.GetNextPacketSize(out packet);
                    bool got = false;
                    while (packet > 0)
                    {
                        IntPtr data;
                        uint frames;
                        uint flags;
                        ulong dev, qpc;
                        if (capture.GetBuffer(out data, out frames, out flags, out dev, out qpc) != 0) break;
                        if (frames > 0) { Push(data, frames, flags); got = true; }
                        capture.ReleaseBuffer(frames);
                        int ghr = capture.GetNextPacketSize(out packet);
                    if (ghr != 0) break;
                    }
                    Thread.Sleep(got ? 5 : 12);
                }
                catch
                {
                    Close(client, capture, mixPtr);
                    client = null; capture = null; mixPtr = IntPtr.Zero;
                    Thread.Sleep(1000);
                }
            }

            Close(client, capture, mixPtr);
        }

        static bool Open(out IAudioClient client, out IAudioCaptureClient capture, out IntPtr mixPtr,
                         out string why)
        {
            client = null; capture = null; mixPtr = IntPtr.Zero; why = "";
            try
            {
                // Activate by CLSID rather than by a [ComImport] coclass: AudioPeak already declares
                // its own coclass for the same CLSID, and two of them in one assembly make the cast
                // fail with a type name that looks identical ("cannot cast MMDeviceEnumerator to
                // MMDeviceEnumerator").
                Type coclass = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
                IMMDeviceEnumerator en = (IMMDeviceEnumerator)Activator.CreateInstance(coclass);
                IMMDevice dev;
                int hr = en.GetDefaultAudioEndpoint(eRender, eConsole, out dev);
                if (hr != 0 || dev == null) { why = "endpoint hr=0x" + hr.ToString("X8"); return false; }

                Guid iidClient = typeof(IAudioClient).GUID;
                object obj;
                hr = dev.Activate(ref iidClient, CLSCTX_ALL, IntPtr.Zero, out obj);
                if (hr != 0) { why = "activate hr=0x" + hr.ToString("X8"); return false; }
                client = (IAudioClient)obj;

                IntPtr fmt;
                hr = client.GetMixFormat(out fmt);
                if (hr != 0 || fmt == IntPtr.Zero) { why = "mixformat hr=0x" + hr.ToString("X8"); return false; }
                mixPtr = fmt;
                WAVEFORMATEX wf = (WAVEFORMATEX)Marshal.PtrToStructure(fmt, typeof(WAVEFORMATEX));
                _channels = wf.nChannels < 1 ? 1 : wf.nChannels;
                _sampleRate = (int)(wf.nSamplesPerSec == 0 ? 48000u : wf.nSamplesPerSec);

                Guid session = Guid.Empty;
                // 1 s of buffer, shared mode, loopback: we only ever look at the newest samples.
                hr = client.Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_LOOPBACK,
                                       10000000L, 0L, fmt, ref session, IntPtr.Zero);
                if (hr != 0) { why = "init hr=0x" + hr.ToString("X8") + " ch=" + wf.nChannels + " sr=" + wf.nSamplesPerSec + " bits=" + wf.wBitsPerSample + " tag=" + wf.wFormatTag; return false; }

                Guid iidCap = typeof(IAudioCaptureClient).GUID;
                IntPtr capPtr;
                hr = client.GetService(ref iidCap, out capPtr);
                if (hr != 0 || capPtr == IntPtr.Zero) { why = "service hr=0x" + hr.ToString("X8"); return false; }
                capture = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(capPtr);
                Marshal.Release(capPtr);

                hr = client.Start();
                if (hr != 0) { why = "start hr=0x" + hr.ToString("X8"); return false; }
                lock (_lock) { _ringWrite = 0; _ringTotal = 0; }
                return true;
            }
            catch (Exception ex)
            {
                why = "ex " + ex.GetType().Name + ": " + ex.Message;
                Close(client, capture, mixPtr);
                client = null; capture = null; mixPtr = IntPtr.Zero;
                return false;
            }
        }

        static void Close(IAudioClient client, IAudioCaptureClient capture, IntPtr mixPtr)
        {
            try { if (client != null) client.Stop(); } catch { }
            try { if (capture != null && Marshal.IsComObject(capture)) Marshal.ReleaseComObject(capture); } catch { }
            try { if (client != null && Marshal.IsComObject(client)) Marshal.ReleaseComObject(client); } catch { }
            try { if (mixPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(mixPtr); } catch { }
        }

        static void Push(IntPtr data, uint frames, uint flags)
        {
            int ch = _channels;
            int total = (int)frames * ch;
            // Mix formats on Windows are float32. Copy through a managed buffer (no /unsafe) and
            // fold the channels down to mono.
            float[] buf = null;
            if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) == 0)
            {
                if (total <= 0 || total > 1 << 20) return;
                if (_scratch == null || _scratch.Length < total) _scratch = new float[1 << 20];
                buf = _scratch;
                Marshal.Copy(data, buf, 0, total);
            }
            lock (_lock)
            {
                for (int i = 0; i < (int)frames; i++)
                {
                    if (buf == null) { Append(0f); continue; }
                    float sum = 0f;
                    for (int c = 0; c < ch; c++) sum += buf[i * ch + c];
                    Append(sum / ch);
                }
            }
        }

        static float[] _scratch;

        static void Append(float v)
        {
            _ring[_ringWrite] = v;
            _ringWrite = (_ringWrite + 1) % RingSize;
            _ringTotal++;
            _lastWriteTick = Environment.TickCount;
        }

        // ---- analysis ------------------------------------------------------

        /// <summary>
        /// Level per band, 0..1, low frequencies first. Returns null when capture is not available
        /// so the caller can fall back to the plain envelope.
        /// </summary>
        public static float[] Bands(int count)
        {
            if (count < 2) return null;
            Start();

            float[] mono = new float[FFTSize];
            int sr;
            lock (_lock)
            {
                if (_ringTotal < FFTSize) return null;
                if ((Environment.TickCount - _lastWriteTick) > 500) return null;
                int n = RingSize;
                int start = (_ringWrite - FFTSize + n) % n;
                for (int i = 0; i < FFTSize; i++) mono[i] = _ring[(start + i) % n];
                sr = _sampleRate;
            }

            int half;
            float[] mag = Magnitudes(mono, out half);

            if (_smooth == null || _smooth.Length != count) _smooth = new float[count];

            // Log-spaced bands from 40 Hz to 16 kHz: linear bins would put almost everything the ear
            // cares about into the first two bars.
            double lo = 40.0, hi = Math.Min(16000.0, sr * 0.5);
            double perBin = (double)sr / FFTSize;
            float[] outp = new float[count];
            for (int b = 0; b < count; b++)
            {
                double f0 = lo * Math.Pow(hi / lo, (double)b / count);
                double f1 = lo * Math.Pow(hi / lo, (double)(b + 1) / count);
                int i0 = (int)(f0 / perBin); if (i0 < 1) i0 = 1;
                int i1 = (int)(f1 / perBin); if (i1 <= i0) i1 = i0 + 1;
                if (i1 > half) i1 = half;

                float peak = 0f;
                for (int i = i0; i < i1; i++)
                {
                    // The FFT leaves magnitudes in mono[] (real part); take the loudest bin so a
                    // narrow tone does not disappear when its band is wide.
                    float m = mag[i];
                    if (m > peak) peak = m;
                }

                // Log-ish response: 0.25 keeps quiet bands visible, sqrt tames the peaks.
                float v = (float)(Math.Sqrt(peak) * 0.9);
                if (v > 1f) v = 1f;
                _smooth[b] += (v - _smooth[b]) * (v > _smooth[b] ? 0.55f : 0.16f);
                outp[b] = _smooth[b] < 0f ? 0f : _smooth[b];
            }
            return outp;
        }

        /// <summary>In-place iterative radix-2 FFT on a complex pair.</summary>
        static void Fft(float[] re, float[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    float tr = re[i]; re[i] = re[j]; re[j] = tr;
                    float ti = im[i]; im[i] = im[j]; im[j] = ti;
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2.0 * Math.PI / len;
                float wr = (float)Math.Cos(ang), wi = (float)Math.Sin(ang);
                int half = len >> 1;
                for (int i = 0; i < n; i += len)
                {
                    float cr = 1f, ci = 0f;
                    for (int k = 0; k < half; k++)
                    {
                        int p = i + k, q = i + k + half;
                        float xr = re[q] * cr - im[q] * ci;
                        float xi = re[q] * ci + im[q] * cr;
                        re[q] = re[p] - xr;
                        im[q] = im[p] - xi;
                        re[p] += xr;
                        im[p] += xi;
                        if (k + 1 < half)
                        {
                            float ncr = cr * wr - ci * wi;
                            ci = cr * wi + ci * wr;
                            cr = ncr;
                        }
                    }
                }
            }
        }

        /// <summary>Magnitude per bin, 0..n/2-1, already windowed and normalised by n.</summary>
        static float[] Magnitudes(float[] mono, out int half)
        {
            int n = mono.Length;
            float[] re = new float[n];
            float[] im = new float[n];
            for (int i = 0; i < n; i++) re[i] = mono[i] * (0.5f - 0.5f * (float)Math.Cos(2.0 * Math.PI * i / (n - 1)));
            Fft(re, im);
            half = n / 2;
            float[] mag = new float[half];
            float scale = 2f / n;
            for (int i = 0; i < half; i++)
            {
                float m = (float)Math.Sqrt(re[i] * re[i] + im[i] * im[i]) * scale;
                mag[i] = m;
            }
            return mag;
        }
    }
}
