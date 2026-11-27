// ---------------------------------------------------------------------------
// Program.cs
//
/// Entry point: single-instance mutex, command-line switches, and the tray/card/main
/// window wiring for both the normal and the --background start.
// ---------------------------------------------------------------------------
using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace Amc
{
    /// <summary>
    /// Entry point plus the command line modes: normal run, <c>--ensure</c>, <c>--smtcdump</c>,
    /// <c>--lyrtest</c>, <c>--proccount</c> and <c>--uninstall</c>.
    /// </summary>
    static class Program
    {
        // Both the instance guard and the --ensure probe must use this name; they had drifted
        // apart, so --ensure always thought nothing was running and spawned a doomed copy.
        internal const string SingleInstanceMutex = "Local\\Lyricard-m7Q2";

        [STAThread]
        static void Main(string[] args)
        {
            if (args != null && args.Length >= 3 && args[0] == "--lyrtest")
            {
                RunLyricTest(args);
                return;
            }

            // The update helper: a second copy of this exe that waits for us to exit, swaps the
            // file and relaunches. It must run before the single-instance mutex, because the
            // instance it is waiting on still owns that mutex.
            if (args != null && args.Length >= 3 && args[0] == "--apply-update")
            {
                int waitPid = 0;
                int.TryParse(args[2], out waitPid);
                Environment.Exit(Updater.RunApply(args[1], waitPid));
                return;
            }

            // Housekeeping: a previous update may have left the renamed exe behind.
            try
            {
                string selfPath = System.Reflection.Assembly.GetEntryAssembly().Location;
                if (!string.IsNullOrEmpty(selfPath) && File.Exists(selfPath + ".old")) File.Delete(selfPath + ".old");
            }
            catch { }

            if (args != null && args.Length >= 1 && args[0] == "--ensure")
            {
                RunEnsure();
                return;
            }

            // --updcheck: run the update check from the console and print what happened. The
            // About page reports the same failures, but a headless run is the quickest way to tell
            // "GitHub is unreachable" apart from "the payload changed shape".
            if (args != null && args.Length >= 1 && args[0] == "--updcheck")
            {
                try
                {
                    UpdateInfo u = Updater.Check();
                    Console.WriteLine(u == null
                        ? "up to date (" + Strings.T("app.version") + ")"
                        : "update " + u.Tag + " asset=" + u.AssetName + " size=" + u.Size);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("check failed: " + ex.GetType().Name + ": " + ex.Message);
                    if (ex.InnerException != null)
                        Console.WriteLine("  inner: " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message);
                }
                return;
            }

            if (args != null && args.Length >= 1 && args[0] == "--boardtest")
            {
                RunBoardTest();
                return;
            }

            if (args != null && args.Length >= 1 && args[0] == "--smtcdump")
            {
                RunSmtcDump();
                return;
            }

            if (args != null && args.Length >= 1 && args[0] == "--proccount")
            {
                RunProcCount();
                return;
            }

            // Uninstall runs before the single-instance guard: it has to be able to run while a
            // normal copy is still alive, and it kills that copy itself.
            if (args != null && args.Length >= 1 && args[0] == "--shortcut")
            {
                // Create a shortcut where the user can find it, then exit.
                string where = args.Length >= 2 ? args[1] : "desktop";
                string made = (where == "startmenu") ? Shortcut.CreateStartMenu() : Shortcut.CreateDesktop();
                LyricsService.Log("shortcut: --shortcut " + where + " -> " + (made == null ? "failed" : made));
                return;
            }

            if (args != null && args.Length >= 1 && args[0] == "--uninstall")
            {
                bool silent = HasArg(args, "--silent");
                try { Config c0 = Config.Load(); Strings.Resolve(c0.Lang); } catch { }
                Uninstall.Run(false, silent);
                return;
            }

            bool createdNew;
            using (var mutex = new Mutex(true, SingleInstanceMutex, out createdNew))
            {
                if (!createdNew)
                {
                    // Already running: if this launch was a plain double-click (not the logon
                    // --background entry), ask the live instance to raise its main page instead of
                    // silently exiting. That is what makes the shortcut feel like it works.
                    if (!HasArg(args, "--background"))
                    {
                        try
                        {
                            using (var ev = System.Threading.EventWaitHandle.OpenExisting("Local\\Lyricard-show"))
                                ev.Set();
                        }
                        catch { }
                    }
                    return;
                }

                // Created by the first instance, waited on by a background thread below.
                System.Threading.EventWaitHandle showEvent = null;
                try
                {
                    showEvent = new System.Threading.EventWaitHandle(
                        false, System.Threading.EventResetMode.AutoReset, "Local\\Lyricard-show");
                }
                catch { }

                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                AppDomain.CurrentDomain.UnhandledException += delegate(object s2, UnhandledExceptionEventArgs e2)
                {
                    LyricsService.Log("UNHANDLED " + e2.ExceptionObject);
                };
                app.DispatcherUnhandledException += delegate(object s2, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e2)
                {
                    LyricsService.Log("UI-EX " + e2.Exception.GetType().Name + ": " + e2.Exception.Message);
                    e2.Handled = true;
                };

                LyricsService.Log("=== app start ===");

                var cfg = Config.Load();
            // Player bindings: when the user bound at least one player, only those players may
            // drive the card (that is also what keeps browsers out of it).
            Players.ApplyBindings(cfg);
            Strings.Resolve(cfg.Lang);

                // Portable entry point: a shortcut with the app icon next to the exe.
                // Created on first start because a build-time .lnk would point at the build machine.
                Shortcut.EnsureFolderShortcut();

                // Only the integrity level and elevation are logged: they are what actually explains
                // "the card cannot see Apple Music". The account and machine name are deliberately
                // left out, because users paste this log into bug reports.
                try
                {
                    var wi = System.Security.Principal.WindowsIdentity.GetCurrent();
                    bool admin = new System.Security.Principal.WindowsPrincipal(wi)
                        .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                    LyricsService.Log("token intg=" + TokenInfo.IntegrityLevel() + " admin=" + admin);
                }
                catch { }

                try
                {
                    AutoStart.RemoveLegacy();
                    if (cfg.AutoStart != AutoStart.IsEnabled()) AutoStart.Set(cfg.AutoStart);
                    LyricsService.Log("autostart=" + cfg.AutoStart + " reg=" + AutoStart.IsEnabled() + " err=" + AutoStart.LastError);
                }
                catch { }

                var smtc = new SmtcService();
                smtc.ListenAll = cfg.ListenAll;
                smtc.Start();

                var win = new CardWindow(cfg, smtc);
                // Launching the exe means 'open the app': show the main page, and keep the
                // card on the desktop as before. Logon launches pass --background and stay quiet.
                if (!HasArg(args, "--background")) MainWindow.Open(cfg, win);
                // Logon launch: no main window, and the card waits for a player.
                win.HideWhenIdle = HasArg(args, "--background");
                win.HideFullscreen = cfg.HideFullscreen;

                // A second launch (shortcut double-click while we are already running) pings this
                // event; hop back to the UI thread and show the page.
                if (showEvent != null)
                {
                    var waiter = new System.Threading.Thread(delegate()
                    {
                        while (true)
                        {
                            try { if (!showEvent.WaitOne()) break; }
                            catch { break; }
                            try
                            {
                                app.Dispatcher.BeginInvoke((Action)delegate { MainWindow.Open(cfg, win); });
                            }
                            catch { }
                        }
                    });
                    waiter.IsBackground = true;
                    waiter.Name = "lyricard-show-watch";
                    waiter.Start();
                }
                // --settings must not depend on the card ever being shown: with no player the
                // card stays hidden and its OnLoaded never fires, which used to swallow the flag.
                if (HasArg(args, "--settings"))
                {
                    app.Dispatcher.BeginInvoke((Action)delegate
                    {
                        try
                        {
                            MainWindow.Open(cfg, win).ShowPage("look");
                            LyricsService.Log("settings -> appearance page (startup)");
                        }
                        catch (Exception ex) { LyricsService.Log("settings startup failed: " + ex.Message); }
                    });
                }
                // --page <home|look|lyrics|about>: open the panel on a given page. Same idea as
                // --settings, but for screenshots and for jumping straight at the page you mean.
                string pageArg = ArgValue(args, "--page");
                if (pageArg != null)
                {
                    app.Dispatcher.BeginInvoke((Action)delegate
                    {
                        try
                        {
                            MainWindow.Open(cfg, win).ShowPage(pageArg);
                            LyricsService.Log("page -> " + pageArg);
                        }
                        catch (Exception ex) { LyricsService.Log("page startup failed: " + ex.Message); }
                    });
                }
                // --cover "title|artist": exercise the online cover fallback and log the result.
                string coverArg = ArgValue(args, "--cover");
                if (coverArg != null)
                {
                    var coverWorker = new System.Threading.Thread(delegate()
                    {
                        string[] parts = coverArg.Split('|');
                        string ct = parts[0];
                        string ca = parts.Length > 1 ? parts[1] : "";
                        byte[] cb = CoverArt.Fetch(ct, ca);
                        LyricsService.Log("cover test t=" + ct + " a=" + ca + " -> " + (cb == null ? "none" : cb.Length + " bytes"));
                    });
                    coverWorker.IsBackground = true;
                    coverWorker.Start();
                }

                // --wheel <n>: run the wheel dispatch n notches without synthesising input.
                string wheelArg = ArgValue(args, "--wheel");
                int wheelNotches;
                if (wheelArg != null && int.TryParse(wheelArg, out wheelNotches))
                {
                    app.Dispatcher.BeginInvoke((Action)delegate
                    {
                        try { win.ApplyWheel(wheelNotches); }
                        catch (Exception ex) { LyricsService.Log("wheel startup failed: " + ex.Message); }
                    });
                }
                // --pick: jump straight to the manual lyric picker (debugging aid, and handy
                // when the card is not on screen because nothing is playing right now).
                if (HasArg(args, "--pick"))
                {
                    app.Dispatcher.BeginInvoke((Action)delegate
                    {
                        try { win.OpenCandidates(); LyricsService.Log("picker -> startup"); }
                        catch (Exception ex) { LyricsService.Log("picker startup failed: " + ex.Message); }
                    });
                }
                // Silent update probe: one request, no window, no poller. Skipped versions stay
                // quiet. Kept off the UI thread because it is a network round-trip.
                if (cfg.AutoCheckUpdate)
                {
                    var upd = new System.Threading.Thread(delegate()
                    {
                        try
                        {
                            UpdateInfo info = Updater.Check();
                            if (info != null && info.Version.ToString() != cfg.SkippedVersion)
                            {
                                Updater.Pending = info;
                                LyricsService.Log("update available " + info.Version + " (" + info.AssetName + ")");
                            }
                        }
                        catch (Exception ex) { LyricsService.Log("update check failed: " + ex.Message); }
                    });
                    upd.IsBackground = true;
                    upd.Start();
                }
                var tray = new Tray(win);
                win.TrayRef = tray;

                // Dev switch: pop the tray menu at the cursor so its styling can be screenshotted
                // without hunting for the notification-area icon.
                if (HasArg(args, "--traymenu"))
                {
                    win.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try { tray.ShowMenu(); } catch (Exception ex) { LyricsService.Log("traymenu: " + ex.Message); }
                    }));
                }

                app.Exit += delegate
                {
                    tray.Dispose();
                    smtc.Stop();
                };

                // Scheduled lyric-cache cleanup. One attempt at startup (so a long-running machine
                // that was rebooted still gets it) and an hourly re-check for machines left on for
                // days. Both are no-ops unless the user turned the option on.
                try { LyricsService.AutoCleanIfDue(cfg); } catch (Exception ex) { LyricsService.Log("cache autoclean failed: " + ex.Message); }
                var cleanTimer = new System.Windows.Threading.DispatcherTimer();
                cleanTimer.Interval = TimeSpan.FromHours(1);
                cleanTimer.Tick += delegate { try { LyricsService.AutoCleanIfDue(cfg); } catch { } };
                cleanTimer.Start();

                app.Run();
                GC.KeepAlive(mutex);
            }
        }

        static void RunProcCount()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var all = System.Diagnostics.Process.GetProcesses();
                sb.AppendLine("all=" + all.Length);
                var am = System.Diagnostics.Process.GetProcessesByName("AppleMusic");
                sb.AppendLine("am=" + am.Length);
                for (int i = 0; i < all.Length; i++)
                {
                    string nm = "";
                    try { nm = all[i].ProcessName; } catch { continue; }
                    if (nm.IndexOf("Apple", StringComparison.OrdinalIgnoreCase) >= 0)
                        sb.AppendLine("  hit " + nm + " " + all[i].Id);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("err " + ex.GetType().Name + ": " + ex.Message);
            }
            string path = System.IO.Path.Combine(Config.Dir, "proccount.txt");
            try { System.IO.File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8); } catch { }
        }

        static void RunSmtcDump()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var mgr = SmtcService.Wait(Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
                var list = mgr.GetSessions();
                sb.AppendLine("sessions=" + list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    string aumid = s.SourceAppUserModelId;
                    string info = "";
                    try
                    {
                        var mp = SmtcService.Wait(s.TryGetMediaPropertiesAsync());
                        info = mp.Title + " | " + mp.Artist;
                    }
                    catch (Exception ex)
                    {
                        info = "media fail " + ex.GetType().Name + ": " + ex.Message;
                    }
                    string pb = "";
                    bool canSeek = false, canRate = false;
                    try
                    {
                        var pi = s.GetPlaybackInfo();
                        // PlaybackType is how Windows tells music from video; browsers report it too.
                        pb = pi.PlaybackStatus + " type=" + pi.PlaybackType;
                        var ctl = pi.Controls;
                        canSeek = ctl.IsPlaybackPositionEnabled;
                        canRate = ctl.IsPlaybackRateEnabled;
                    }
                    catch { }
                    string tl = "";
                    try
                    {
                        var t = s.GetTimelineProperties();
                        tl = " pos=" + t.Position.TotalSeconds.ToString("0.0") + " end=" + t.EndTime.TotalSeconds.ToString("0.0");
                    }
                    catch { }
                    sb.AppendLine("  [" + i + "] " + aumid + " :: " + pb + " :: seek=" + canSeek + " rate=" + canRate + tl + " :: " + info);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("dump fail " + ex.GetType().Name + ": " + ex.Message);
            }
            string path = System.IO.Path.Combine(Config.Dir, "smtc.txt");
            try { System.IO.File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8); }
            catch { }
            LyricsService.Log("smtcdump -> " + path);
        }

        /// <summary>Value after <paramref name="name"/> ("--page about" -> "about"), or null.</summary>
        static string ArgValue(string[] args, string name)
        {
            if (args == null) return null;
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++) if (args[i] == name) return true;
            return false;
        }

        static void RunEnsure()
        {
            LyricsService.Log("ensure: start");
            bool am = false;
            try
            {
                var ps = System.Diagnostics.Process.GetProcessesByName("AppleMusic");
                am = ps != null && ps.Length > 0;
                LyricsService.Log("ensure: found=" + (ps == null ? -1 : ps.Length));
            }
            catch (Exception ex)
            {
                LyricsService.Log("ensure scan fail " + ex.GetType().Name + ": " + ex.Message);
            }
            if (!am) return;
            bool createdNew;
            using (var mutex = new Mutex(true, SingleInstanceMutex, out createdNew))
            {
                if (!createdNew) return;
                try { mutex.ReleaseMutex(); } catch { }
            }

            try
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var psi = new System.Diagnostics.ProcessStartInfo(exe);
                psi.UseShellExecute = false;
                psi.WorkingDirectory = System.IO.Path.GetDirectoryName(exe);
                System.Diagnostics.Process.Start(psi);
                LyricsService.Log("ensure: Apple Music is running, card launched");
            }
            catch (Exception ex)
            {
                LyricsService.Log("ensure fail " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        static void RunLyricTest(string[] args)
        {
            string title = args[1];
            string artist = args[2];
            double dur = 0;
            if (args.Length > 3) double.TryParse(args[3], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out dur);
            var svc = new LyricsService();
            var doc = svc.Get(title, artist, dur, true);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("query=" + title + " | " + artist + " | " + dur);
            if (doc == null)
            {
                sb.AppendLine("RESULT=NULL");
            }
            else
            {
                sb.AppendLine("RESULT src=" + doc.Source + " lines=" + doc.Lines.Count + " instr=" + doc.Instrumental + " matched=" + doc.Matched);
                if (doc.Lines.Count > 0)
                {
                    sb.AppendLine("first=" + doc.Lines[0].Time.ToString("0.00") + " " + doc.Lines[0].Text);
                    sb.AppendLine("last=" + doc.Lines[doc.Lines.Count - 1].Time.ToString("0.00") + " " + doc.Lines[doc.Lines.Count - 1].Text);
                    sb.AppendLine("--- all ---");
                    foreach (var l in doc.Lines) sb.AppendLine(l.Time.ToString("0.00") + "  " + l.Text);
                }
            }
            try
            {
                var outp = System.IO.Path.Combine(Config.Dir, "lyrtest.txt");
                System.IO.File.WriteAllText(outp, sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch { }
            try
            {
                Console.WriteLine(sb.ToString());
            }
            catch { }
        }
    
        // Head-less check of the template undo path (what the board's Cancel button runs): mutate a
        // module, stash the previous template, save, then restore. Lets the logic be verified
        // without driving a mouse.
        static void RunBoardTest()
        {
            string live = CardConfig.DefaultPath;
            CardConfig model = CardConfig.LoadOr(live);
            ModuleConfig lyrics = model.Find(ModuleKind.Lyrics);
            double before = lyrics == null ? double.NaN : lyrics.Y;
            if (lyrics != null) lyrics.Y = (double.IsNaN(before) ? 0.0 : before) - 40.0;
            model.Save(live + ".prev");                  // the pre-Apply snapshot
            model.Save(live);
            CardConfig saved = CardConfig.LoadOr(live);
            ModuleConfig a = saved.Find(ModuleKind.Lyrics);
            Console.WriteLine("saved lyrics.Y=" + (a == null ? "?" : a.Y.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)));

            CardConfig prev = CardConfig.Parse(File.ReadAllText(live + ".prev", System.Text.Encoding.UTF8));
            ModuleConfig b = prev.Find(ModuleKind.Lyrics);
            prev.Save(live);
            Console.WriteLine("restored lyrics.Y=" + (b == null ? "?" : b.Y.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                + " (before " + before.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ")");
        }
}
}
