using System;
using System.Runtime.InteropServices;

namespace Amc
{
    /// <summary>
    /// System master volume, for the "wheel adjusts volume" mode.
    ///
    /// Nothing here is specific to a player: it drives the same slider the Windows volume flyout
    /// does, so it also changes system sounds. Per-application volume would need the session
    /// manager plus a process-to-session map, which is a different feature (not built).
    ///
    /// The COM surface is declared by hand because we compile with the in-box csc and carry no
    /// interop assemblies. Only the methods we call are exercised, but every member has to be
    /// declared in vtable order or the call lands on the wrong slot -- the unused ones are
    /// placeholders with the right arity for that reason.
    /// </summary>
    static class SystemVolume
    {
        // eRender / eConsole
        const int Render = 0;
        const int Console = 0;
        const int ClsCtxInproc = 1;

        // The enumerator CLSID is already declared in AudioPeak.cs. Declaring a second ComImport
        // class for it makes the CLR hand back the RCW it built for THAT class, and the cast to
        // our interface then fails with InvalidCastException -- the same trap that made the
        // spectrum fall back to a fake. Activate by CLSID instead of by a duplicate class.
        static readonly Guid EnumeratorClsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDeviceEnumerator
        {
            void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            void GetDefaultAudioEndpoint(int dataFlow, int role, out IDevice device);
            void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IDevice device);
            void RegisterEndpointNotificationCallback(IntPtr client);
            void UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDevice
        {
            void Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            void OpenPropertyStore(int access, out IntPtr store);
            void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            void GetState(out int state);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IEndpointVolume
        {
            void RegisterControlChangeNotify(IntPtr notify);
            void UnregisterControlChangeNotify(IntPtr notify);
            void GetChannelCount(out int count);
            void SetMasterVolumeLevel(float db, IntPtr context);
            void SetMasterVolumeLevelScalar(float level, IntPtr context);
            void GetMasterVolumeLevel(out float db);
            void GetMasterVolumeLevelScalar(out float level);
            void SetChannelVolumeLevel(uint channel, float db, IntPtr context);
            void SetChannelVolumeLevelScalar(uint channel, float level, IntPtr context);
            void GetChannelVolumeLevel(uint channel, out float db);
            void GetChannelVolumeLevelScalar(uint channel, out float level);
            void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr context);
            void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
            void GetVolumeStepInfo(out uint step, out uint stepCount);
            void VolumeStepUp(IntPtr context);
            void VolumeStepDown(IntPtr context);
            void QueryHardwareSupport(out uint mask);
            void GetVolumeRange(out float min, out float max, out float increment);
        }

        static readonly object Gate = new object();
        static IEndpointVolume _volume;
        static bool _unavailable;

        static IEndpointVolume Acquire()
        {
            lock (Gate)
            {
                if (_volume != null) return _volume;
                if (_unavailable) return null;
                try
                {
                    Type enType = Type.GetTypeFromCLSID(EnumeratorClsid);
                    if (enType == null) throw new InvalidOperationException("no MMDeviceEnumerator");
                    var en = (IDeviceEnumerator)Activator.CreateInstance(enType);
                    IDevice dev;
                    en.GetDefaultAudioEndpoint(Render, Console, out dev);
                    Guid iid = typeof(IEndpointVolume).GUID;
                    object o;
                    dev.Activate(ref iid, ClsCtxInproc, IntPtr.Zero, out o);
                    _volume = (IEndpointVolume)o;
                    return _volume;
                }
                catch (Exception ex)
                {
                    // No default output device, or COM is wedged. Report once; do not retry every notch.
                    _unavailable = true;
                    LyricsService.Log("volume: endpoint unavailable (" + ex.GetType().Name + ")");
                    return null;
                }
            }
        }

        /// <summary>Current level and mute state; false when there is no endpoint to talk to.</summary>
        public static bool TryRead(out int percent, out bool muted)
        {
            percent = 0;
            muted = false;
            IEndpointVolume v = Acquire();
            if (v == null) return false;
            try
            {
                float level;
                v.GetMasterVolumeLevelScalar(out level);
                v.GetMute(out muted);
                percent = (int)Math.Round(level * 100.0);
                return true;
            }
            catch (Exception ex)
            {
                _unavailable = true;
                LyricsService.Log("volume: read failed (" + ex.GetType().Name + ")");
                return false;
            }
        }

        /// <summary>
        /// Move the master volume by <paramref name="delta"/> (0..1 scale). Scrolling up while muted
        /// unmutes first -- otherwise the wheel looks broken on a muted machine.
        /// </summary>
        public static void Nudge(double delta)
        {
            IEndpointVolume v = Acquire();
            if (v == null) return;
            try
            {
                float level;
                v.GetMasterVolumeLevelScalar(out level);
                bool muted;
                v.GetMute(out muted);
                double next = level + delta;
                if (next < 0.0) next = 0.0;
                if (next > 1.0) next = 1.0;
                if (muted && delta > 0.0)
                {
                    v.SetMute(false, IntPtr.Zero);
                    muted = false;
                }
                v.SetMasterVolumeLevelScalar((float)next, IntPtr.Zero);
                LyricsService.Log("volume " + (int)Math.Round(next * 100.0) + "%" + (muted ? " (muted)" : ""));
            }
            catch (Exception ex)
            {
                _unavailable = true;
                LyricsService.Log("volume: write failed (" + ex.GetType().Name + ")");
            }
        }
    }
}
