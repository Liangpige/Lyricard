using System;
using System.Runtime.InteropServices;

class Peak
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

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
                     [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        int GetPeakValue(out float peak);
    }

    static void Main()
    {
        var en = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
        for (int i = 0; i < 6; i++)
        {
            IMMDevice dev;
            int hr = en.GetDefaultAudioEndpoint(0, 1, out dev);
            Guid iid = typeof(IAudioMeterInformation).GUID;
            object o;
            int hr2 = dev.Activate(ref iid, 23, IntPtr.Zero, out o);
            var m = (IAudioMeterInformation)o;
            float p;
            int hr3 = m.GetPeakValue(out p);
            Console.WriteLine("hr=" + hr + "," + hr2 + "," + hr3 + " peak=" + p.ToString("0.0000"));
            System.Threading.Thread.Sleep(350);
        }
    }
}
