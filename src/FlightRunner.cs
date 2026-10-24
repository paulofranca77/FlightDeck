using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace FlightDeck
{
    /// <summary>Runs Start Flight / End Flight on a background thread.</summary>
    class FlightRunner
    {
        public event Action<string> Log;
        public event Action<AppEntry, string> Status;
        public event Action<string, bool, int> Done;   // kind, cancelled, problems
        public event Action<string> BeforeStacking;     // kind
        public event Action<int, int, string> Progress; // done, total, label
        int lastDone = -1;
        void P(int done, int total, string label) { lastDone = done; var h = Progress; if (h != null) h(done, total, label); }

        // Each app moves through small steps (opened → window found → each placement pass → ready),
        // so the bar climbs a little with every action instead of waiting for whole apps.
        Dictionary<AppEntry, int> units = new Dictionary<AppEntry, int>();
        int unitsPerApp, extraUnits, extraDone;
        void BeginUnits(IEnumerable<AppEntry> list, int perApp, int extra)
        {
            units = list.ToDictionary(a => a, a => 0);
            unitsPerApp = perApp; extraUnits = extra; extraDone = 0;
        }
        int ReadyCount { get { return units.Count(kv => kv.Value >= unitsPerApp); } }
        void Up(AppEntry a, int u, string label)
        {
            int cur;
            if (a != null && units.TryGetValue(a, out cur)) units[a] = Math.Max(cur, Math.Min(u, unitsPerApp));
            int done = units.Values.Sum() + extraDone, total = units.Count * unitsPerApp + extraUnits;
            P(done, total, label);
        }
        void UpExtra(int step, string label) { extraDone = Math.Min(extraUnits, step); Up(null, 0, label); }
        volatile bool busy;
        public bool Busy { get { return busy; } private set { busy = value; } }   // read by the window while the run thread works
        public int Problems { get; private set; }
        volatile bool cancel;
        readonly Config cfg;

        public FlightRunner(Config c) { cfg = c; }

        void L(string m) { var h = Log; if (h != null) h(m); }
        void S(AppEntry a, string s) { if (s == "Error" || s == "Not found" || s == "Running" || s == "Needs admin") Problems++; var h = Status; if (h != null) h(a, s); }

        void Run(string kind, Action body)
        {
            Busy = true; cancel = false; Problems = 0;
            var t = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { L("Error: " + ex.Message); }
                finally
                {
                    bool c = cancel; int p = Problems;
                    Busy = false;
                    var d = Done; if (d != null) d(kind, c, p);
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        public void Cancel() { cancel = true; }

        // -------------------- START FLIGHT --------------------
        public void StartFlight(IList<AppEntry> list, bool positionOnly = false)
        {
            var apps = list.Where(a => a.Enabled).ToList();
            Deferred = new List<AppEntry>();
            Run(positionOnly ? "apply" : "start", () => DoStart(apps, positionOnly, null));
        }

        /// <summary>Apps held back by their START setting during the last Start Flight (not opened yet).</summary>
        public List<AppEntry> Deferred = new List<AppEntry>();

        /// <summary>Their moment has come: open the held-back apps, then re-apply the stacking order of the whole profile.</summary>
        public void StartDeferred(List<AppEntry> held, IList<AppEntry> profile)
        {
            var apps = held.ToList();
            var stack = profile.Where(a => a.Enabled).ToList();
            Deferred = new List<AppEntry>();
            Run("later", () => DoStart(apps, false, stack));
        }

        class Job
        {
            public AppEntry App; public bool WasRunning; public DateTime? Found; public int Passes; public bool Warned;
            public IntPtr Handle; public string Title; public DateTime? VerifyStart; public DateTime LastFix; public int Good, Fixes; public bool Refused;
            public string LastOff; public int SameOff;
            public DateTime Launched = DateTime.Now; public DateTime LastReveal; public int Reveals, TrayClicks; public bool InTray;
        }

        public event Action<string> Debug;     // detailed lines: written to the log file only
        void D(string m) { var h = Debug; if (h != null) h(m); }

        static string Fr(RECT r) { return r.Left + "," + r.Top + " " + r.Width + "x" + r.Height; }

        /// <summary>A few pixels from the target with the right size (the app adjusts itself slightly).</summary>
        static bool Near(WinInfo w, AppEntry a)
        {
            const int near = 8, tol = 3;
            return !a.Maximized && !w.Minimized && !w.Maximized &&
                   Math.Abs(w.Frame.Left - a.X) <= near && Math.Abs(w.Frame.Top - a.Y) <= near &&
                   Math.Abs(w.Frame.Width - a.W) <= tol && Math.Abs(w.Frame.Height - a.H) <= tol;
        }

        static bool AtTarget(WinInfo w, AppEntry a)
        {
            if (a.Maximized) return w.Maximized;
            const int tol = 3;
            return !w.Minimized && !w.Maximized &&
                   Math.Abs(w.Frame.Left - a.X) <= tol && Math.Abs(w.Frame.Top - a.Y) <= tol &&
                   Math.Abs(w.Frame.Width - a.W) <= tol && Math.Abs(w.Frame.Height - a.H) <= tol;
        }

        static bool MustWait(AppEntry a)
        {
            switch (a.StartMode)
            {
                case "delay": return a.StartDelay > 0;
                case "sim": return !Native.SimConnectReady();
                case "flight": return !SimLink.FlightLoaded;
                default: return false;
            }
        }

        public static string WaitStatus(AppEntry a)
        {
            return a.StartMode == "flight" ? "Waiting for flight" : a.StartMode == "sim" ? "Waiting for sim" : "Starting in " + a.StartDelay + " s";
        }

        void DoStart(List<AppEntry> apps, bool positionOnly, List<AppEntry> stackApps)
        {
            if (apps.Count == 0) { L("No apps enabled — add some on the Apps page."); return; }
            L(positionOnly ? "Applying layout…" : stackApps != null ? "Opening " + string.Join(", ", apps.Select(a => a.Name)) + "…" : "Starting flight…");
            bool elevated = Util.IsElevated();
            var jobs = new List<Job>();
            const int U = 8;   // per app: launching, opened (3), window found (4), placement passes (5-7), ready (8)
            BeginUnits(apps, U, (stackApps ?? apps).Count > 1 ? 2 : 0);
            Func<string> tally = () => " (" + ReadyCount + " of " + apps.Count + " ready)";
            Up(null, 0, positionOnly ? "Looking for your windows…" : "Getting ready…");

            foreach (var a in apps)
            {
                if (cancel) return;
                Up(null, 0, (positionOnly ? "Looking for " : "Opening ") + a.Name + "…");
                var w = Engine.FindWindow(a);
                if (w == null && Engine.IsRunning(a))
                {
                    // already running but no window on screen: it's parked in the tray
                    S(a, "Positioning");
                    jobs.Add(new Job { App = a, WasRunning = true, InTray = true, Launched = DateTime.Now.AddSeconds(-60) });
                    Up(a, 3, a.Name + " is in the tray…");
                    continue;
                }
                if (w == null && !positionOnly && stackApps == null && MustWait(a))
                {
                    // START column: this app opens later (after a delay, when the sim is ready, or once the flight has loaded)
                    Deferred.Add(a);
                    S(a, WaitStatus(a));
                    L(a.Name + " will open " + a.WhenText + ".");
                    Up(a, U, a.Name + " opens " + a.WhenText);
                    continue;
                }
                if (w == null && !positionOnly)
                {
                    if (string.IsNullOrWhiteSpace(a.Path)) { L(a.Name + ": not running and no program path set — skipped."); S(a, "Skipped"); Up(a, U, a.Name + " skipped"); continue; }
                    Up(a, 1, "Opening " + a.Name + "…");
                    try
                    {
                        Engine.Launch(a, elevated);
                        L("Launching " + a.Name + "…");
                        S(a, "Starting");
                    }
                    catch (Exception ex) { L(a.Name + ": could not start (" + ex.Message + ")."); S(a, "Error"); Up(a, U, a.Name + " could not start"); continue; }
                    Up(a, 3, a.Name + " opened");
                    Thread.Sleep(400);
                }
                else if (w == null) { Up(a, U, a.Name + " isn't open"); continue; }
                else { S(a, "Positioning"); Up(a, 4, a.Name + " is already open"); }
                jobs.Add(new Job { App = a, WasRunning = w != null });
            }

            int nApps = apps.Count;
            var jobApps = jobs.Select(x => x.App).ToList();
            Up(null, 0, "Waiting for the windows…" + tally());
            double[] launchedPasses = { 0, 3, 8 };      // apps we just started: they often move themselves while loading
            double[] runningPasses = { 0, 1.5, 4 };     // apps that were already open
            const double settleSeconds = 30;            // after the passes, keep checking for up to this long
            var start = DateTime.Now;
            while (jobs.Count > 0 && !cancel && (DateTime.Now - start).TotalSeconds < cfg.LaunchTimeout + settleSeconds)
            {
                var all = Native.ListWindows();
                foreach (var j in jobs.ToArray())
                {
                    var a = j.App;
                    var w = Engine.FindWindow(a, all);
                    if (w == null && j.Reveals < 10 && Engine.IsRunning(a) &&
                        (DateTime.Now - j.Launched).TotalSeconds >= Math.Max(0, a.Delay) + 3 &&
                        (DateTime.Now - j.LastReveal).TotalSeconds >= 3)
                    {
                        // the app started minimized to the tray: bring its window out so it can be placed
                        j.LastReveal = DateTime.Now; j.Reveals++;
                        string shown = Engine.RevealFromTray(a);
                        if (shown != null)
                        {
                            if (j.Reveals == 1) L(a.Name + " started in the tray — opening its window.");
                            D(a.Name + ": revealed hidden window \"" + shown + "\"");
                        }
                        else
                        {
                            // no window exists yet: the app creates it only when its tray icon is clicked.
                            // escalate: click message → double-click message → click the icon on the taskbar
                            string how = null;
                            if (j.Reveals == 2 || j.Reveals == 5)
                            {
                                if (Native.PokeTrayIcon(Engine.Pids(a), false, D) > 0) how = "click";
                            }
                            else if (j.Reveals == 3 || j.Reveals == 6)
                            {
                                if (Native.PokeTrayIcon(Engine.Pids(a), true, D) > 0) how = "double-click";
                            }
                            else if (j.Reveals == 4 || j.Reveals == 8)
                            {
                                string icon = Tray.ClickIcon(a, D);
                                if (icon != null) how = "taskbar icon \"" + icon + "\"";
                            }
                            if (how != null)
                            {
                                j.TrayClicks++;
                                if (j.TrayClicks == 1) L(a.Name + " is waiting in the tray — opening it.");
                                D(a.Name + ": tray " + how);
                                Thread.Sleep(1500);
                            }
                            else D(a.Name + ": running, no window yet");
                        }

                        if (shown != null || j.TrayClicks > 0)
                        {
                            Thread.Sleep(300);
                            all = Native.ListWindows();
                            w = Engine.FindWindow(a, all);
                        }
                    }
                    if (w == null)
                    {
                        if (j.Found == null && (DateTime.Now - start).TotalSeconds > cfg.LaunchTimeout) { jobs.Remove(j); NotFound(j); }
                        continue;
                    }
                    if (j.Found == null) { j.Found = DateTime.Now; S(a, "Positioning"); Up(a, 4, a.Name + " is up" + tally()); D(a.Name + ": window found \"" + w.Title + "\" at " + Fr(w.Frame) + (w.Minimized ? " (minimized)" : w.Maximized ? " (maximized)" : "")); }
                    if (j.Handle != IntPtr.Zero && j.Handle != w.Handle)
                        D(a.Name + ": main window changed \"" + j.Title + "\" → \"" + w.Title + "\" (" + Fr(w.Frame) + ")");
                    j.Handle = w.Handle; j.Title = w.Title;

                    if (!a.HasPos) { jobs.Remove(j); S(a, "Ready"); L(a.Name + ": open (no saved position)."); continue; }

                    var offsets = j.WasRunning ? runningPasses : launchedPasses;
                    double delay = j.WasRunning ? 0 : Math.Max(0, a.Delay);
                    double since = (DateTime.Now - j.Found.Value).TotalSeconds;

                    if (j.Passes < offsets.Length)
                    {
                        // fixed passes
                        if (since < delay + offsets[j.Passes]) continue;
                        if (j.Passes == 0 && j.WasRunning) L(a.Name + " was already open — moving it to its saved position.");
                        PlaceJob(j, w);
                        j.Passes++;
                        Up(a, 4 + Math.Min(3, j.Passes), "Placing " + a.Name + "…" + tally());
                        if (j.Refused) { jobs.Remove(j); S(a, AtTarget(w, a) ? "Ready" : "Needs admin"); }
                        continue;
                    }

                    // verify phase: done once the window has stayed on target for ~2 s;
                    // if it moved (or a different main window appeared, e.g. after a splash/login screen) place it again.
                    if (j.VerifyStart == null) j.VerifyStart = DateTime.Now;
                    if (AtTarget(w, a))
                    {
                        if (++j.Good >= 5) { jobs.Remove(j); S(a, "Ready"); L(a.Name + ": in position." + (j.Fixes > 0 ? " (needed " + j.Fixes + " extra correction" + (j.Fixes == 1 ? "" : "s") + ")" : "")); }
                        continue;
                    }
                    j.Good = 0;
                    // Some apps (tebs Traffic Audio…) always nudge themselves a few pixels after every move.
                    // When the window keeps coming back to the same spot right next to the target, that's its final place.
                    if (j.Fixes >= 3 && Near(w, a))
                    {
                        if (j.LastOff == w.Frame.Left + "," + w.Frame.Top + "," + w.Frame.Width + "," + w.Frame.Height) j.SameOff++;
                        else { j.SameOff = 0; j.LastOff = w.Frame.Left + "," + w.Frame.Top + "," + w.Frame.Width + "," + w.Frame.Height; }
                        if (j.SameOff >= 3)
                        {
                            jobs.Remove(j); S(a, "Ready");
                            L(a.Name + ": in position (it shifts itself " + Math.Max(Math.Abs(w.Frame.Left - a.X), Math.Abs(w.Frame.Top - a.Y)) + " px after every move, so FlightDeck leaves it there).");
                            continue;
                        }
                    }
                    if ((DateTime.Now - j.VerifyStart.Value).TotalSeconds > settleSeconds)
                    {
                        // the window is open, just not exactly in place: worth a line in the log, not a failed start
                        jobs.Remove(j); S(a, "Moved itself");
                        L(a.Name + ": keeps moving away from its position (now " + Fr(w.Frame) + "). Try a longer PLACE AFTER for it (Apps page).");
                        continue;
                    }
                    if ((DateTime.Now - j.LastFix).TotalSeconds >= 1.2)
                    {
                        D(a.Name + ": off target at " + Fr(w.Frame) + " — correcting");
                        j.Fixes++;
                        PlaceJob(j, w);
                    }
                }
                foreach (var fa in jobApps)
                    if (units[fa] < U && !jobs.Any(x => x.App == fa)) Up(fa, U, fa.Name + " ready" + tally());
                Thread.Sleep(400);
            }
            if (!cancel)
                foreach (var j in jobs)
                {
                    if (j.Found == null) NotFound(j);
                    else { S(j.App, "Error"); L(j.App.Name + ": didn't settle in its position in time."); D(Diagnostics.ForApp(j.App)); }
                }
            jobs.Clear();
            if (cancel) { L("Start cancelled."); return; }
            var order = stackApps ?? apps;
            if (order.Count > 1)
            {
                // Final step, after the last (slowest) app was placed: set which window is in front.
                var bs = BeforeStacking; if (bs != null) bs(positionOnly ? "apply" : stackApps != null ? "later" : "start");
                foreach (var fa in apps) units[fa] = U;
                UpExtra(1, "Setting the window order…");
                Thread.Sleep(1500);
                if (cancel) return;
                ApplyStacking(order, false);
                Thread.Sleep(2500);            // some apps bring themselves forward when they finish loading
                if (cancel) return;
                ApplyStacking(order, true);
                L("Stacking order applied (front → back): " + string.Join(" › ", order.Select(a => a.Name)));
            }
            foreach (var fa in apps) units[fa] = U;
            UpExtra(2, positionOnly ? "Layout applied" : Deferred.Count > 0 ? "Ready — more apps open later" : "All set");
            if (positionOnly) L("Layout applied.");
            else if (Deferred.Count > 0) L("Ready. Still to open: " + string.Join(", ", Deferred.Select(a => a.Name + " (" + a.WhenText + ")")) + ".");
            else L(stackApps != null ? "Done." : "Flight ready. Have a good one ✈");
        }

        void NotFound(Job j)
        {
            S(j.App, "Not found");
            L(j.App.Name + ": window not found after " + cfg.LaunchTimeout + " s — check Process name / Window title.");
            D(Diagnostics.ForApp(j.App));
        }

        void PlaceJob(Job j, WinInfo w)
        {
            var a = j.App;
            j.LastFix = DateTime.Now;
            bool ok = Native.Place(w.Handle, a.X, a.Y, a.W, a.H, a.Maximized);
            RECT after = Native.GetFrame(w.Handle);
            D(a.Name + ": place → " + a.X + "," + a.Y + " " + a.W + "x" + a.H + (a.Maximized ? " max" : "") + " | result " + (ok ? "ok" : "REFUSED") + ", now " + Fr(after));
            if (!ok && !j.Warned)
            {
                j.Warned = true;
                L(a.Name + ": Windows refused to move it — it runs as administrator. Turn on \"Run FlightDeck as administrator\" in Settings.");
            }
            if (!ok) j.Refused = true;
        }

        public static Action<string> DebugSink;

        /// <summary>Top of the list ends up in front: raise windows from the bottom of the list up.</summary>
        void ApplyStacking(List<AppEntry> apps, bool final)
        {
            var all = Native.ListWindows();
            var wins = apps.Select(a => Engine.FindWindow(a, all)).ToList();

            // Apps that make themselves "always on top" would ignore the list order: take that away first.
            for (int i = 0; i < apps.Count; i++)
                if (wins[i] != null && Native.IsTopmost(wins[i].Handle))
                {
                    Native.ClearTopmost(wins[i].Handle);
                    D("stacking: " + apps[i].Name + " was set to always-on-top — turned that off" + (Native.IsTopmost(wins[i].Handle) ? " (REFUSED)" : ""));
                }

            for (int i = apps.Count - 1; i >= 0; i--)
            {
                var w = wins[i];
                if (w != null && !w.Minimized) { Native.BringToTop(w.Handle); Thread.Sleep(60); }
                else if (w == null) D("stacking: no window for " + apps[i].Name);
            }

            if (!final) return;
            // report apps that put themselves back on top
            Thread.Sleep(800);
            for (int i = 0; i < apps.Count; i++)
                if (wins[i] != null && Native.IsTopmost(wins[i].Handle))
                    L(apps[i].Name + " forces itself to stay on top of other windows, so it can't follow the stacking order. Check its own \"always on top\" setting.");
        }

        // -------------------- END FLIGHT --------------------
        public void EndFlight(IList<AppEntry> list)
        {
            var apps = list.Where(a => a.Enabled && a.CloseOnEnd && !string.IsNullOrWhiteSpace(a.Process)).ToList();
            Run("end", () => DoEnd(apps));
        }

        void DoEnd(List<AppEntry> apps)
        {
            if (apps.Count == 0) { L("Nothing to close — no app has \"Auto-close\" on."); return; }
            L("Ending flight…");
            string sim = Engine.RunningSim();
            var all = Native.ListWindows();
            var closing = new List<AppEntry>();
            var skipped = new HashSet<AppEntry>();
            foreach (var a in apps)
            {
                if (Engine.IsSim(a))
                {
                    // safety net: the simulator is never closed by FlightDeck, even if it was added as an app
                    skipped.Add(a); S(a, "Skipped");
                    L(a.Name + ": that's the simulator — FlightDeck never closes it.");
                    continue;
                }
                var pids = Engine.Pids(a);
                if (pids.Count == 0) { S(a, "Closed"); continue; }
                foreach (var w in all) if (pids.Contains(w.Pid)) Native.PostMessage(w.Handle, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                S(a, "Closing");
                closing.Add(a);
            }

            const int E = 3;   // per app: asked to close, (in tray / asked to exit), closed
            BeginUnits(apps, E, 0);
            foreach (var a in apps) Up(a, closing.Contains(a) ? 1 : E, closing.Contains(a) ? "Closing " + a.Name + "…" : skipped.Contains(a) ? a.Name + " left open" : a.Name + " was already closed");
            var start = DateTime.Now;
            var told = new Dictionary<AppEntry, int>();     // how many times the app got the "Windows is closing you" messages
            int timeout = Math.Max(5, cfg.CloseTimeout);
            while (closing.Count > 0 && !cancel && (DateTime.Now - start).TotalSeconds < timeout)
            {
                var now = Native.ListWindows();
                double t = (DateTime.Now - start).TotalSeconds;
                foreach (var a in closing.ToArray())
                {
                    if (!Engine.IsRunning(a))
                    {
                        closing.Remove(a); S(a, "Closed");
                        L(a.Name + (told.ContainsKey(a) ? ": closed cleanly." : ": closed."));
                        Up(a, E, a.Name + " closed");
                        continue;
                    }
                    int n; told.TryGetValue(a, out n);
                    // still running after the normal close (usually: it went to the tray instead of quitting).
                    // Ask it the way Windows does at shutdown, so it saves and exits cleanly; once more later.
                    if ((n == 0 && t > 1.5) || (n == 1 && t > 9))
                    {
                        told[a] = n + 1;
                        if (n == 0)
                        {
                            bool tray = Engine.FindWindow(a, now) == null;
                            if (tray) S(a, "In tray");
                            Up(a, 2, a.Name + (tray ? " went to the tray — asking it to exit…" : " is still open — asking it to exit…"));
                            L(a.Name + (tray ? " went to the tray instead of closing — asking it to exit properly." : " is still open — asking it to exit properly."));
                        }
                        int got = Native.AskToEndSession(Engine.Pids(a));
                        D(a.Name + ": end-session request #" + (n + 1) + " sent to " + got + " window(s)");
                    }
                }
                if (closing.Count > 0) Thread.Sleep(400);
            }

            if (closing.Count > 0 && !cancel) Up(null, 0, "Some apps are still running…");
            foreach (var a in closing)
            {
                if (cancel) break;
                if (!cfg.ForceClose) { S(a, "Running"); L(a.Name + ": still running (force close is off)."); Up(a, E, a.Name + " left open"); continue; }
                if (sim != null && !cfg.ForceWhileSimRunning)
                {
                    // Killing an add-on that is connected to the simulator can crash the simulator.
                    S(a, "Running");
                    L(a.Name + ": still running after " + timeout + " s. Not forcing it closed while " + sim + " is open (that can crash the sim) — close it from its own window or tray icon.");
                    Up(a, E, a.Name + " left open");
                    continue;
                }
                bool denied = false;
                foreach (var p in Engine.Processes(a))
                {
                    try { p.Kill(); } catch { denied = true; }
                    p.Dispose();
                }
                Thread.Sleep(500);
                if (Engine.IsRunning(a))
                {
                    S(a, "Running");
                    L(a.Name + (denied ? ": could not close it — it runs as administrator. Turn on \"Run FlightDeck as administrator\" in Settings." : ": still running."));
                }
                else { S(a, "Closed"); L(a.Name + ": closed (forced)."); }
                Up(a, E, a.Name + " closed");
            }
            foreach (var a in apps) units[a] = E;
            Up(null, 0, "Flight ended");
            L("Flight ended.");
        }
    }
}
