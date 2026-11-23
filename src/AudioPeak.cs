using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Amc
{
    /// <summary>
    /// Reads the peak level of the default playback device, so the card can tell
    /// "something is actually coming out of the speakers" from what the player
    /// reports. Some Windows clients (NetEase Cloud Music is the known one) keep
    /// announcing Paused while their audio is clearly playing, which used to
    /// freeze the lyric clock at 0 and look like "no lyrics found".
    /// </summary>
    public static class AudioPeak
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                         [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioMeterInformation
        {
            int GetPeakValue(out float peak);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2
        {
            int GetAudioSessionControl(ref Guid audioSessionGuid, int streamFlags, out IntPtr sessionControl);
            int GetSimpleAudioVolume(ref Guid audioSessionGuid, int streamFlags, out IntPtr audioVolume);
            int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator
        {
            int GetCount(out int count);
            int GetSession(int index, out IAudioSessionControl session);
        }

        [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl
        {
            int GetState(out int state);
            int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid eventContext);
            int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid eventContext);
            int GetGroupingParam(out Guid groupingParam);
            int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
            int RegisterAudioSessionNotification(IntPtr client);
            int UnregisterAudioSessionNotification(IntPtr client);
        }

        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            // vtable: the nine inherited members must be repeated verbatim
            int GetState(out int state);
            int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid eventContext);
            int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid eventContext);
            int GetGroupingParam(out Guid groupingParam);
            int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
            int RegisterAudioSessionNotification(IntPtr client);
            int UnregisterAudioSessionNotification(IntPtr client);
            int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            int GetProcessId(out int pid);
            int IsSystemSoundsSession();
            int SetDuckingPreference(bool optOut);
        }

        const int eRender = 0;
        const int eMultimedia = 1;
        const int CLSCTX_ALL = 23;

        static IAudioMeterInformation _meter;

        static bool Ensure()
        {
            if (_meter != null) return true;
            try
            {
                var en = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(eRender, eMultimedia, out dev) != 0) return false;
                Guid iid = typeof(IAudioMeterInformation).GUID;
                object o;
                if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out o) != 0) return false;
                _meter = (IAudioMeterInformation)o;
                return _meter != null;
            }
            catch { _meter = null; return false; }
        }

        static int _pidCached;
        static string _pidName;
        static DateTime _pidAt;

        static int PidOf(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return 0;
            string name = Path.GetFileNameWithoutExtension(exe);
            if (string.IsNullOrEmpty(name)) return 0;
            DateTime now = DateTime.Now;
            if (_pidName == name && (now - _pidAt).TotalMilliseconds < 900) return _pidCached;
            int pid = 0;
            try
            {
                Process[] ps = Process.GetProcessesByName(name);
                if (ps.Length > 0) pid = ps[0].Id;
            }
            catch { pid = 0; }
            _pidName = name;
            _pidCached = pid;
            _pidAt = now;
            return pid;
        }

        /// <summary>
        /// Peak level of one player's own audio session (per process), so another app playing
        /// sound cannot make a paused player look like it is playing. Falls back to false when
        /// the session cannot be found.
        /// </summary>
        public static bool IsProcessAudible(string exe, double threshold)
        {
            int pid = PidOf(exe);
            if (pid == 0) return false;
            try
            {
                var en = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(eRender, eMultimedia, out dev) != 0 || dev == null) return false;
                Guid iid = typeof(IAudioSessionManager2).GUID;
                object o;
                if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out o) != 0 || o == null) return false;
                var mgr = (IAudioSessionManager2)o;
                IAudioSessionEnumerator list;
                if (mgr.GetSessionEnumerator(out list) != 0 || list == null) return false;
                int count;
                if (list.GetCount(out count) != 0) return false;
                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl ctl;
                    if (list.GetSession(i, out ctl) != 0 || ctl == null) continue;
                    int spid = 0;
                    try
                    {
                        var c2 = ctl as IAudioSessionControl2;
                        if (c2 != null) c2.GetProcessId(out spid);
                    }
                    catch { spid = 0; }
                    if (spid != pid) continue;
                    var meter = ctl as IAudioMeterInformation;
                    if (meter == null) continue;
                    float peak;
                    if (meter.GetPeakValue(out peak) != 0) continue;
                    if (peak >= threshold) return true;
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>True when the default output device is producing sound above
        /// <paramref name="threshold"/> (0..1 peak). Returns false on any failure.</summary>
        public static bool IsAudible(double threshold)
        {
            if (!Ensure()) return false;
            try
            {
                float peak;
                if (_meter.GetPeakValue(out peak) != 0) { _meter = null; return false; }
                if (peak <= 0.00001f) return false;
                return peak >= threshold;
            }
            catch { _meter = null; return false; }
        }
    }
}
