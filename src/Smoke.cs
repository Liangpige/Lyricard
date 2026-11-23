using System;
using System.Threading;
using Windows.Media.Control;

class Smoke
{
    static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op)
    {
        var sw = new SpinWait();
        while (op.Status == Windows.Foundation.AsyncStatus.Started) sw.SpinOnce();
        if (op.Status != Windows.Foundation.AsyncStatus.Completed)
            throw new Exception("async failed: " + op.Status);
        return op.GetResults();
    }

    [STAThread]
    static void Main()
    {
        try
        {
            var mgr = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
            var sessions = mgr.GetSessions();
            Console.WriteLine("sessions=" + sessions.Count);
            foreach (var s in sessions)
            {
                var p = Wait(s.TryGetMediaPropertiesAsync());
                var pb = s.GetPlaybackInfo();
                var tl = s.GetTimelineProperties();
                Console.WriteLine("AUMID=" + s.SourceAppUserModelId);
                Console.WriteLine("  title=" + p.Title + " | artist=" + p.Artist + " | album=" + p.AlbumTitle);
                Console.WriteLine("  status=" + pb.PlaybackStatus + " pos=" + tl.Position + " end=" + tl.EndTime);
                Console.WriteLine("  thumb=" + (p.Thumbnail != null ? "yes" : "no"));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERR " + ex.GetType().Name + ": " + ex.Message);
        }
    }
}
