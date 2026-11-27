// ---------------------------------------------------------------------------
// Smtc.cs
//
/// Thin wrapper over the WinRT SystemMediaTransportControls session manager. Marshals
/// every WinRT await onto a worker via a hand-written polling wait, because this project
/// must not depend on System.Runtime.WindowsRuntime.
// ---------------------------------------------------------------------------
using System;
using System.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Amc
{
    /// <summary>
    /// One sample of the current media session: metadata, playback state and the timeline.
    /// <see cref="Pos"/> / <see cref="StampLocal"/> carry a smoothed anchor rather than the raw
    /// position - see SmtcService.Read for why.
    /// </summary>
    public class Snapshot
    {
        public bool Has;
        public string Aumid = "";
        public string Title = "";
        public string Artist = "";
        public string Album = "";
        public TimeSpan Pos;
        public TimeSpan Dur;
        public DateTimeOffset Stamp;          // wall clock of the timeline sample
        public DateTime StampLocal;           // local clock when we received it
        public bool Playing;
        /// <summary>The player keeps reporting the same position (or zero): our own clock is the only truth.</summary>
        public bool PosFrozen;
        /// <summary>This track's position reporting proved fake (frozen, or reset to 0 on pause) - trust the clock only.</summary>
        public bool PosUntrusted;
        public bool CanSeek;
        public bool CanNext;
        public bool CanPrev;
        public bool CanPlayPause;
        public byte[] Art;

        /// <summary>Identity of "which track is this", shared by the artwork poll and the card.
        /// The Aumid is part of it: handing the card from one player to another (same song) is
        /// still a switch, and the new player's thumbnail has to be read again.</summary>
        public string Key
        {
            get { return Aumid + "|" + Title + "|" + Artist; }
        }

        public double EstimateSeconds(DateTime nowLocal)
        {
            double p = Pos.TotalSeconds;
            if (Playing)
            {
                p += (nowLocal - StampLocal).TotalSeconds;
                double d = Dur.TotalSeconds;
                if (d > 1 && p > d) p = d;
            }
            return p;
        }
    }

    /// <summary>
    /// Polls the WinRT media session (GlobalSystemMediaTransportControls) on a background thread and
    /// exposes the latest snapshot plus transport commands. Apple Music updates its position only
    /// every few seconds, so the timeline is anchored and free-run instead of copied verbatim.
    /// </summary>
    public class SmtcService
    {
        GlobalSystemMediaTransportControlsSessionManager _mgr;
        GlobalSystemMediaTransportControlsSession _sess;
        Thread _thread;
        volatile bool _stop;

        public volatile Snapshot Current = new Snapshot();

        /// <summary>
        /// Set from the config: returns true when this AUMID belongs to a player the user has
        /// bound. Null (or a null config list) keeps the legacy Apple-Music-first behaviour.
        /// </summary>
        public static Func<string, bool> BoundMatch;
        /// <summary>True when the "waiting for music" idle card is allowed (Apple Music bound).</summary>
        public static bool AppleMusicWanted = true;
        /// <summary>AUMIDs of every media session seen during the last poll, for the player list.</summary>
        public static string[] SessionAumids = new string[0];

        /// <summary>First session that is publishing but is not bound (null when none).</summary>
        public static string UnboundAumid;
        public bool ListenAll;

        byte[] _artBytes;
        string _artHash = "";          // FNV-1a of the cover currently on the card
        string _artPrevHash = "";      // hash of the previous track's cover
        bool _artPending;              // still waiting for THIS track's real cover
        DateTime _artDeadline;         // accept an unchanged picture after this (same album)
        DateTime _artWatchUntil;       // keep listening this long after a same-album accept
        DateTime _artNextTry;
        int _artTries;
        bool _artLoggedStale;
        string _artTrackKey = "";      // Aumid|Title of the track the artwork belongs to
        string _lastKey = "";
        // Timeline anchor: Apple Music refreshes Position only every ~3s while we poll at 5Hz,
        // so the anchor must NOT be re-stamped on every poll (the estimate would then crawl
        // forward 200ms and snap back, which reads as "歌词忽快忽慢").
        TimeSpan _anchorPos;
        DateTime _anchorAt;
        bool _anchorPlaying;
        bool _anchorValid;
        double _lastReported = -1;
        int _frozenPolls;
        int _audiblePolls;
        double _deltaSum;
        int _deltaN;

        public static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            int guard = 0;
            while (op.Status == Windows.Foundation.AsyncStatus.Started && guard < 20000)
            {
                Thread.Sleep(1);
                guard++;
            }
            if (op.Status != Windows.Foundation.AsyncStatus.Completed)
                throw new Exception("winrt async " + op.Status);
            return op.GetResults();
        }

        static void WaitAction(Windows.Foundation.IAsyncAction a)
        {
            int guard = 0;
            while (a.Status == Windows.Foundation.AsyncStatus.Started && guard < 20000)
            {
                Thread.Sleep(1);
                guard++;
            }
        }

        static void WaitAction(Windows.Foundation.IAsyncOperation<bool> a)
        {
            int guard = 0;
            while (a.Status == Windows.Foundation.AsyncStatus.Started && guard < 20000)
            {
                Thread.Sleep(1);
                guard++;
            }
        }

        public void Start()
        {
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "smtc-poll";
            _thread.Start();
        }

        public void Stop()
        {
            _stop = true;
        }

        void Loop()
        {
            while (!_stop)
            {
                try
                {
                    if (_mgr == null)
                        _mgr = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());

                    // Hard preference for Apple Music: the card is an Apple Music companion, so an
                    // existing (even paused) Apple Music session always wins over other players.
                    // "Listen to all players" only kicks in when Apple Music has no session at all.
                    var sessions = _mgr.GetSessions();
                    var ids = new System.Collections.Generic.List<string>(sessions.Count);
                    for (int i = 0; i < sessions.Count; i++)
                        ids.Add(sessions[i].SourceAppUserModelId ?? "");
                    SessionAumids = ids.ToArray();

                    GlobalSystemMediaTransportControlsSession pick = null;
                    var bound = BoundMatch;
                    if (bound != null)
                    {
                        // Bindings win: a bound player that is actually playing beats a paused one,
                        // and anything the user did not bind is ignored entirely (that is also how
                        // browsers stay out of the card).
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            if (!bound(sessions[i].SourceAppUserModelId ?? "")) continue;
                            bool playing = false;
                            try
                            {
                                playing = sessions[i].GetPlaybackInfo().PlaybackStatus ==
                                          GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                            }
                            catch { }
                            if (playing) { pick = sessions[i]; break; }
                            if (pick == null) pick = sessions[i];
                        }
                    }
                    else
                    {
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            var s = sessions[i];
                            string id = s.SourceAppUserModelId ?? "";
                            if (id.IndexOf("applemusic", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                pick = s;
                                break;
                            }
                        }
                        if (pick == null && ListenAll)
                        {
                            try { pick = _mgr.GetCurrentSession(); }
                            catch { pick = null; }
                            if (pick == null && sessions.Count > 0) pick = sessions[0];
                        }
                    }

                    UnboundAumid = null;
                    if (pick == null && bound != null && sessions.Count > 0)
                    {
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            string uid = sessions[i].SourceAppUserModelId ?? "";
                            if (uid.Length == 0 || Players.IsBrowser(uid)) continue;
                            UnboundAumid = uid;
                            break;
                        }
                    }

                    if (pick == null)
                    {
                        if (Current.Has) Current = new Snapshot();
                        _sess = null;
                        Thread.Sleep(300);
                        continue;
                    }

                    // Ghost session: NetEase (and a few others) leave an SMTC entry behind after the
                    // window is closed, so the card used to stay on screen with the last song. If the
                    // owning process is gone and nothing is audible for ~1s, treat it as "no session"
                    // so the card disappears together with the player.
                    if (IsGhost(pick))
                    {
                        LyricsService.Log("smtc ghost dropped aumid=" + (pick.SourceAppUserModelId ?? "?"));
                        if (Current.Has) Current = new Snapshot();
                        _sess = null;
                        Thread.Sleep(300);
                        continue;
                    }

                    // One line whenever the selected session changes: which player, what it
                    // claims, and whether its own audio meter backs that claim. Enough to answer
                    // "why did the card pop up while I was watching a video" from the log alone.
                    string pickId = pick.SourceAppUserModelId ?? "";
                    if (pickId != _pickLogged)
                    {
                        _pickLogged = pickId;
                        string claim = "?";
                        try { claim = pick.GetPlaybackInfo().PlaybackStatus.ToString(); } catch { }
                        LyricsService.Log("smtc pick aumid=" + pickId + " status=" + claim
                            + " audible=" + (SessionAudible(pickId) ? "y" : "n"));
                    }

                    Read(pick);
                    _failPolls = 0;
                }
                catch (Exception ex)
                {
                    // Hold the last good snapshot: blanking the card for a hiccup makes it flash
                    // away and back, which is far worse than showing a stale second of metadata.
                    _failPolls++;
                    LyricsService.Log("smtc poll fail " + _failPolls + ": " + ex.GetType().Name + " " + ex.Message);
                    if (_failPolls >= 5)
                    {
                        _mgr = null;
                        _sess = null;
                        if (Current.Has) Current = new Snapshot();
                    }
                    Thread.Sleep(800);
                }
                Thread.Sleep(200);
            }
        }

        /// <summary>
        /// Audio actually coming out of THIS player (not the whole machine). Players that report
        /// their state correctly are unaffected; the ones that lie (NetEase) get their icon and
        /// clock corrected, and a paused player no longer looks alive because a video is playing
        /// in another window.
        /// </summary>
        static bool SessionAudible(string aumid)
        {
            // Deliberately no machine-wide fallback: with one, any other app's audio (Douyin,
            // a game, a video) could "prove" that a paused player is playing, and the card
            // would pop up out of nowhere. Unknown sessions simply keep the reported state.
            string exe = Players.ExeFor(aumid);
            if (string.IsNullOrEmpty(exe)) return false;
            return AudioPeak.IsProcessAudible(exe, 0.010);
        }

        int _failPolls;
        int _ghostPolls;
        string _pickLogged;
        string _trustKey = "";
        bool _trusted = true;
        string _durLogged;

        /// <summary>
        /// True once the session's player process has been gone (and silent) for about a second.
        /// Sessions without a guessable executable are never treated as ghosts, so an unknown
        /// player can not hide itself by accident.
        /// </summary>
        bool IsGhost(GlobalSystemMediaTransportControlsSession s)
        {
            try
            {
                // NOTE: this used to bail out whenever *anything* on the machine was audible.
                // That made a dead session immortal while the user watched a video (Douyin in
                // the browser, a game, ...): the process was long gone, but the card kept
                // showing that player's last song because "sound exists". Match sound to the
                // player instead -- its own process is the only thing that can produce it.
                string aumid = s.SourceAppUserModelId ?? "";
                var info = Players.MatchAumid(aumid);
                string exe = (info != null && info.Exes.Length > 0) ? info.Exes[0] : null;
                if (string.IsNullOrEmpty(exe) && aumid.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe = aumid;
                if (string.IsNullOrEmpty(exe)) { _ghostPolls = 0; return false; }

                string name = System.IO.Path.GetFileNameWithoutExtension(exe);
                if (name.Length == 0) { _ghostPolls = 0; return false; }
                var procs = System.Diagnostics.Process.GetProcessesByName(name);
                bool alive = procs.Length > 0;
                for (int i = 0; i < procs.Length; i++) procs[i].Dispose();
                if (alive) { _ghostPolls = 0; return false; }

                _ghostPolls++;
                return _ghostPolls >= 5;
            }
            catch { _ghostPolls = 0; return false; }
        }

        void Read(GlobalSystemMediaTransportControlsSession s)
        {
            var np = new Snapshot();
            np.Has = true;
            np.Aumid = s.SourceAppUserModelId ?? "";

            var props = Wait(s.TryGetMediaPropertiesAsync());
            np.Title = props.Title ?? "";
            np.Artist = props.Artist ?? "";
            np.Album = props.AlbumTitle ?? "";

            // A new song must never inherit the previous one's anchor: otherwise the first poll
            // predicts 16s into a song that just started at 0, marks it "untrusted" and the bar
            // sits still for the whole track.
            bool newTrack = np.Key != _lastKey;
            if (newTrack)
            {
                _lastKey = np.Key;
                _anchorValid = false;
                _frozenPolls = 0;
                _lastReported = -999;
                _trusted = true;
                _trustKey = (np.Aumid ?? "") + "|" + (np.Title ?? "");
            }

            var pb = s.GetPlaybackInfo();
            np.Playing = pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            // Trust the speakers over the reported state: NetEase Cloud Music keeps
            // saying "Paused" while it is audibly playing, which froze the lyric clock
            // (and made it look like the lyrics never loaded). Require ~0.6s of sound
            // so a single UI beep cannot flip the play icon.
            if (SessionAudible(np.Aumid)) _audiblePolls++;
            else _audiblePolls = 0;
            // ~1 s of real sound before we overrule a "paused" report; a short fade-out or a beep
            // from another app must not resurrect the play icon.
            if (!np.Playing && _audiblePolls >= 5) np.Playing = true;

            var tl = s.GetTimelineProperties();
            np.Dur = tl.EndTime > tl.StartTime
                ? (tl.EndTime - tl.StartTime)
                : TimeSpan.Zero;
            // NetEase reports EndTime = 0; its own play queue still knows the real length, and the
            // progress bar has no scale without it.
            if (np.Dur.TotalSeconds < 1 && Players.IsNetEase(np.Aumid))
            {
                double known = NetEaseQueue.LookupDuration(np.Title, np.Artist);
                if (known > 1)
                {
                    np.Dur = TimeSpan.FromSeconds(known);
                    if (_durLogged != np.Title)
                    {
                        _durLogged = np.Title;
                        LyricsService.Log("netease dur from queue " + known.ToString("0.0") + "s " + np.Title);
                    }
                }
            }
            np.Stamp = tl.LastUpdatedTime;
            var nowLocal = DateTime.Now;
            double reported = tl.Position.TotalSeconds;
            bool frozen = false;
            double predicted = 0.0;
            if (!_anchorValid)
            {
                // start our free-run clock slightly ahead of the truncated value: the app reports
                // whole seconds, so the true position at that instant is somewhere in [P, P+1).
                _anchorPos = tl.Position + TimeSpan.FromSeconds(0.35);
                _anchorPlaying = np.Playing;
                _anchorAt = nowLocal;
                _anchorValid = true;
            }
            else if (np.Playing != _anchorPlaying)
            {
                // Play state changed. Do NOT re-read the reported position here: clients that lie
                // report 0.35 while playing and 0.0 the moment you pause, which used to restart the
                // bar. Freeze the clock where our own estimate says it is.
                double est = _anchorPos.TotalSeconds + (_anchorPlaying ? (nowLocal - _anchorAt).TotalSeconds : 0.0);
                if (est < 0 || est > np.Dur.TotalSeconds + 1.0) est = tl.Position.TotalSeconds;
                _anchorPos = TimeSpan.FromSeconds(est);
                _anchorAt = nowLocal;
                _anchorPlaying = np.Playing;
            }
            else
            {
                // Some players (Win32 media clients, browser sessions) report a frozen position:
                // they keep saying 0 while audio is playing. Correcting against that value would
                // drag our clock back to zero every few seconds, which looks like "lyrics never
                // advance". Track how long the reported number has been identical and switch to
                // free-run while it is frozen; the first change resumes normal correction.
                // Half a second of tolerance: some clients (MD3Music) jitter by tenths while
                // the position is actually stuck, and a 1 ms epsilon made every tick look like
                // fresh data -- the clock then kept re-syncing to the stale value and the bar
                // visibly snapped backwards every few seconds.
                if (Math.Abs(reported - _lastReported) < 0.5) _frozenPolls++;
                else { _frozenPolls = 0; _lastReported = reported; }
                frozen = _frozenPolls > 15;   // ~3s at the 200ms poll rate

                // Apple reports the position truncated to whole seconds and refreshes it in
                // 3-8s chunks, so it is only good to a second or two. Our own clock runs at 1x:
                // treat small deviations as staleness, and only correct real discontinuities.
                predicted = _anchorPos.TotalSeconds + (np.Playing ? (nowLocal - _anchorAt).TotalSeconds : 0.0);
                double delta = reported - predicted;
                double ad = Math.Abs(delta);
                np.PosFrozen = frozen;
                // A player with a laggy timeline (NetEase, KuGou) never updates the position, so
                // "frozen" tells us nothing about a seek: the user dragging the bar backwards would
                // look exactly like the bogus jump-to-zero on pause. Distinguish them by value and
                // state: a *playing* player that suddenly reports a plausible, much smaller number
                // (> 1 s, so not the 0.3 s lie) really was moved.
                bool seekBack = Players.IsLaggyTimeline(np.Aumid) && np.Playing
                                && reported > 1.0 && delta < -6.0;
                if (seekBack)
                {
                    LyricsService.Log("smtc seek-follow reported=" + reported.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                        + " predicted=" + predicted.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                    _anchorPos = tl.Position + TimeSpan.FromSeconds(0.35);
                    _anchorAt = nowLocal;
                    _frozenPolls = 0;
                    _lastReported = reported;
                    _trusted = true;
                    frozen = false;
                }
                else if (frozen)
                {
                    // Reported value is stale or never updated (NetEase stays at 0.35 forever, and a
                    // pause makes it look like a seek to zero). Keep our own clock and re-anchor to
                    // the predicted value so nothing jumps when the number finally moves again.
                    _anchorPos = TimeSpan.FromSeconds(predicted);
                    _anchorAt = nowLocal;
                }
                else if (ad > 4.0 && !Players.IsLaggyTimeline(np.Aumid))
                {
                    // Not for NetEase / KuGou / MD3Music: their "position" wanders (0.35 for a
                    // while, then a jump to a number tens of seconds off), and re-anchoring to
                    // it made the bar - and the lyric line - snap backwards mid-song.
                    LyricsService.Log("smtc resync seek delta=" + delta.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                        + " reported=" + reported.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                        + " frozen=" + frozen + " play=" + np.Playing);
                    _anchorPos = tl.Position + TimeSpan.FromSeconds(0.35);
                    _anchorAt = nowLocal;
                }
                else if (ad > 2.0 && !Players.IsLaggyTimeline(np.Aumid))
                {
                    double fix = delta * 0.4;
                    if (fix > 1.5) fix = 1.5;
                    if (fix < -1.5) fix = -1.5;
                    LyricsService.Log("smtc nudge " + fix.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                        + " (delta=" + delta.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ")");
                    _anchorPos = TimeSpan.FromSeconds(predicted + fix);
                    _anchorAt = nowLocal;
                }
                else
                {
                    _deltaSum += delta;
                    _deltaN++;
                    if (_deltaN >= 20)
                    {
                        LyricsService.Log("smtc bias avg=" + (_deltaSum / _deltaN).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                            + " n=" + _deltaN);
                        _deltaSum = 0;
                        _deltaN = 0;
                    }
                }
            }
            np.Pos = _anchorPos;
            np.StampLocal = _anchorAt;

            // Some clients lie about the position in a way freezing alone does not catch: they hold
            // 0.35 for ever while playing, then jump to 0.0 the moment you pause (= "progress
            // restarted"). Once a track proves it does that, stop believing its numbers until the
            // track changes; the free-run clock plus the real duration is good enough.
            string trustKey = (np.Aumid ?? "") + "|" + (np.Title ?? "");
            if (trustKey != _trustKey)
            {
                _trustKey = trustKey;
                _trusted = true;
            }
            if (_trusted)
            {
                // Only a *playing* player can prove its numbers are fake: a paused one legitimately
                // repeats the same position for ever, and Smtc no longer re-reads it on pause.
                //
                // "Frozen" is only evidence for the players that are known to lie about it: Apple
                // Music and friends also hand out the same position for several seconds by design
                // (coarse 3~8 s updates) while remaining perfectly accurate. The one pattern that is
                // never legitimate is "playing, and it says 0.3 s after we already saw 30 s".
                bool frozenIsLying = frozen && Players.IsLaggyTimeline(np.Aumid);
                bool fakeZero = reported < 1.0 && predicted > 5.0;
                if (np.Playing && (frozenIsLying || fakeZero))
                {
                    _trusted = false;
                    LyricsService.Log("pos untrusted t=" + np.Title
                        + " reported=" + reported.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                        + " predicted=" + predicted.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            np.PosUntrusted = !_trusted;

            try
            {
                var ctl = pb.Controls;
                np.CanSeek = ctl.IsPlaybackPositionEnabled;
                np.CanNext = ctl.IsNextEnabled;
                np.CanPrev = ctl.IsPreviousEnabled;
                np.CanPlayPause = ctl.IsPlayEnabled || ctl.IsPauseEnabled;
            }
            catch { }

            // Identity for the artwork is "which song on which player". The artist is left out on
            // purpose: players often publish title and artist in different polls, and treating the
            // second poll as another new track would drop a perfectly good cover and re-read it.
            string trackId = (np.Aumid ?? "") + "|" + (np.Title ?? "");
            bool newArtTrack = trackId != _artTrackKey;
            _artTrackKey = trackId;
            string key = trackId;
            if (newArtTrack)
            {
                // Players announce the title a beat before they swap their thumbnail, so the first
                // read after a switch still hands back the PREVIOUS cover. Latching that read under
                // the new key is what pinned the old album to the card for a whole song (v0.2.x and
                // v0.3.0), and no repaint in CardWindow could cure it: the bytes never changed.
                // So: keep the old hash, wait for a picture that differs, and only then accept.
                if (_artHash.Length > 0) _artPrevHash = _artHash;
                _artBytes = null;
                _artHash = "";
                _artPending = true;
                _artDeadline = DateTime.Now.AddMilliseconds(2000);
                _artWatchUntil = DateTime.MinValue;
                _artNextTry = DateTime.MinValue;
                _artTries = 0;
                _artLoggedStale = false;
                LyricsService.Log("art wait key=" + key
                    + " prev=" + (_artPrevHash.Length > 0 ? _artPrevHash : "none"));
            }

            bool haveThumb = props.Thumbnail != null;

            if (_artPending && haveThumb && DateTime.Now >= _artNextTry)
            {
                byte[] bytes = null;
                bool failed = false;
                try
                {
                    using (var stream = Wait(props.Thumbnail.OpenReadAsync())) bytes = ReadAll(stream);
                }
                catch (Exception ex)
                {
                    // A thumbnail we cannot open is not a reason to give up on the track.
                    failed = true;
                    _artTries++;
                    _artNextTry = DateTime.Now.AddMilliseconds(Math.Min(1000, 200 * _artTries));
                    if (_artTries == 1) LyricsService.Log("art read failed (" + ex.GetType().Name + "), retrying");
                }

                if (!failed && bytes != null && bytes.Length > 0)
                {
                    string h = ArtHash(bytes);
                    bool changed = !string.Equals(h, _artPrevHash, StringComparison.Ordinal);
                    if (changed || DateTime.Now >= _artDeadline)
                    {
                        _artBytes = bytes;
                        _artHash = h;
                        _artPending = false;
                        // "changed" means this really is the new track's cover. An unchanged picture
                        // is either a same-album run (legitimate) or a slow player, so keep a slow
                        // watch for a while and let a late one win.
                        _artWatchUntil = changed ? DateTime.MinValue : DateTime.Now.AddSeconds(30);
                        _artNextTry = DateTime.Now.AddSeconds(2);
                        LyricsService.Log("art ok " + bytes.Length + " bytes changed=" + (changed ? "True" : "False"));
                    }
                    else
                    {
                        if (!_artLoggedStale)
                        {
                            _artLoggedStale = true;
                            LyricsService.Log("art stale " + bytes.Length
                                + " bytes (player is still publishing the previous cover)");
                        }
                        _artNextTry = DateTime.Now.AddMilliseconds(150);
                    }
                }
                else if (!failed)
                {
                    _artNextTry = DateTime.Now.AddMilliseconds(300);
                }
            }

            if (_artPending && DateTime.Now >= _artDeadline)
            {
                _artPending = false;
                if (!haveThumb)
                {
                    // The player has not published ANY thumbnail for this track yet (Kugou does this
                    // for a few seconds, and for some local/radio items never). Blanking the circle
                    // here is what reads as "封面丢了"; keep what is on screen and keep listening.
                    _artWatchUntil = DateTime.Now.AddSeconds(120);
                    _artNextTry = DateTime.Now.AddSeconds(2);
                    LyricsService.Log("art none (no thumbnail published yet) keep=" + (_artHash.Length > 0 ? "1" : "0"));
                }
                else
                {
                    // It published something we could not use: show nothing rather than the previous
                    // song's cover, and keep listening in case the artwork lands late.
                    _artBytes = null;
                    _artHash = "";
                    _artWatchUntil = DateTime.Now.AddSeconds(120);
                    _artNextTry = DateTime.Now.AddSeconds(2);
                    LyricsService.Log("art none (thumbnail unreadable)");
                }
            }

            if (!_artPending && _artWatchUntil != DateTime.MinValue && DateTime.Now < _artWatchUntil
                && haveThumb && DateTime.Now >= _artNextTry)
            {
                try
                {
                    byte[] bytes;
                    using (var stream = Wait(props.Thumbnail.OpenReadAsync())) bytes = ReadAll(stream);
                    if (bytes != null && bytes.Length > 0)
                    {
                        string h = ArtHash(bytes);
                        if (!string.Equals(h, _artHash, StringComparison.Ordinal))
                        {
                            _artBytes = bytes;
                            _artHash = h;
                            _artWatchUntil = DateTime.MinValue;
                            LyricsService.Log("art late " + bytes.Length + " bytes");
                        }
                        else
                        {
                            _artNextTry = DateTime.Now.AddSeconds(2);
                        }
                    }
                    else
                    {
                        _artNextTry = DateTime.Now.AddSeconds(2);
                    }
                }
                catch
                {
                    _artNextTry = DateTime.Now.AddSeconds(2);
                }
            }

            np.Art = _artBytes;
            _sess = s;
            // Debug hook: pretend no player publishes a session (support / UI testing).
            if (Environment.GetEnvironmentVariable("AMC_NO_SESSION") == "1") Current = new Snapshot();
            else Current = np;
        }

        /// <summary>FNV-1a over the thumbnail bytes: enough to tell "the same picture" from "a new
        /// picture" without a crypto provider, and cheap on a 200 ms poll. Empty means "nothing
        /// worth hashing".</summary>
        static string ArtHash(byte[] data)
        {
            if (data == null || data.Length == 0) return "";
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < data.Length; i++)
            {
                h ^= data[i];
                h *= 1099511628211UL;
            }
            return h.ToString("x16");
        }

        static byte[] ReadAll(IRandomAccessStreamWithContentType stream)
        {
            uint size = (uint)stream.Size;
            if (size == 0 || size > 8 * 1024 * 1024) return null;
            var reader = new DataReader(stream.GetInputStreamAt(0));
            uint loaded = Wait(reader.LoadAsync(size));
            var bytes = new byte[loaded];
            reader.ReadBytes(bytes);
            reader.Dispose();
            return bytes;
        }

        // ---- transport controls (fire and forget) ----
        public void PlayPause()
        {
            var s = _sess;
            if (s == null) return;
            RunOnWorker(delegate
            {
                try
                {
                    // Decide from the raw session status, not from our corrected snapshot: the
                    // "is it audible?" override can mark a paused NetEase as playing, and then the
                    // play button used to send Pause - the music never started and the icon bounced.
                    bool playing = false;
                    try
                    {
                        playing = s.GetPlaybackInfo().PlaybackStatus ==
                                  GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    }
                    catch { playing = Current != null && Current.Playing; }
                    if (playing) WaitAction(s.TryPauseAsync());
                    else WaitAction(s.TryPlayAsync());
                    LyricsService.Log("transport " + (playing ? "pause" : "play"));
                }
                catch (Exception ex) { LyricsService.Log("transport failed: " + ex.Message); }
            });
        }

        public void Next()
        {
            var s = _sess;
            if (s == null) return;
            RunOnWorker(delegate { try { Wait(s.TrySkipNextAsync()); } catch { } });
        }

        public void Prev()
        {
            var s = _sess;
            if (s == null) return;
            RunOnWorker(delegate { try { Wait(s.TrySkipPreviousAsync()); } catch { } });
        }

        public void Seek(double seconds)
        {
            var s = _sess;
            if (s == null) return;
            RunOnWorker(delegate
            {
                try
                {
                    var t = TimeSpan.FromSeconds(seconds);
                    if (t < TimeSpan.Zero) t = TimeSpan.Zero;
                    Wait(s.TryChangePlaybackPositionAsync((long)t.Ticks));
                }
                catch { }
            });
        }

        // Each transport click used to spawn a raw thread that can spin for seconds inside Wait();
        // hammering next/prev/seek piled them up. One at a time is enough for a media widget --
        // extra clicks during an in-flight command are dropped rather than queued.
        static int _transportBusy;

        static void RunOnWorker(Action a)
        {
            if (Interlocked.CompareExchange(ref _transportBusy, 1, 0) != 0) return;
            var th = new Thread(delegate ()
            {
                try { a(); }
                finally { Interlocked.Exchange(ref _transportBusy, 0); }
            });
            th.IsBackground = true;
            th.Start();
        }
    }
}
