using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FlightDeck
{
    public class AppEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Args { get; set; }
        public string Process { get; set; }
        public string Title { get; set; }
        public bool Enabled { get; set; }
        public bool Admin { get; set; }
        public bool CloseOnEnd { get; set; }
        public bool HasPos { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
        public bool Maximized { get; set; }
        public double Delay { get; set; }

        public AppEntry()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = ""; Path = ""; Args = ""; Process = ""; Title = "";
            Enabled = true; CloseOnEnd = true; Delay = 2;
        }

        public AppEntry Clone() { return (AppEntry)MemberwiseClone(); }

        public void CopyFrom(AppEntry o)
        {
            foreach (var p in typeof(AppEntry).GetProperties()) if (p.CanWrite) p.SetValue(this, p.GetValue(o, null), null);
        }

        public void SetPlacement(WinInfo w)
        {
            X = w.Frame.Left; Y = w.Frame.Top; W = w.Frame.Width; H = w.Frame.Height;
            Maximized = w.Maximized; HasPos = true;
        }

        [ScriptIgnore]
        public bool IsUrl { get { return Regex.IsMatch(Path ?? "", @"^[a-zA-Z][a-zA-Z0-9+.-]*://"); } }
    }

    public class Profile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<AppEntry> Apps { get; set; }

        public Profile() { Id = Guid.NewGuid().ToString("N"); Name = "Default"; Apps = new List<AppEntry>(); }

        public Profile CloneAs(string name)
        {
            var p = new Profile { Name = name };
            foreach (var a in Apps) { var c = a.Clone(); c.Id = Guid.NewGuid().ToString("N"); p.Apps.Add(c); }
            return p;
        }
    }

    public class Config
    {
        public List<Profile> Profiles { get; set; }
        public string ActiveProfile { get; set; }
        public List<AppEntry> Apps { get; set; }      // legacy (single list) — migrated into a profile on load
        public int LaunchTimeout { get; set; }
        public int CloseTimeout { get; set; }
        public bool ForceClose { get; set; }
        public bool RunAsAdmin { get; set; }
        public bool TrayHintShown { get; set; }
        public bool LightMode { get; set; }
        public string IconRefreshVersion { get; set; }
        public string ElevatedTaskPath { get; set; }
        public bool SkipAdminPrompt { get; set; }

        public Config()
        {
            Profiles = new List<Profile> { new Profile() };
            LaunchTimeout = 120; CloseTimeout = 10; ForceClose = true;
        }

        [ScriptIgnore]
        public Profile Active
        {
            get
            {
                if (Profiles == null || Profiles.Count == 0) Profiles = new List<Profile> { new Profile() };
                var p = Profiles.FirstOrDefault(x => x.Id == ActiveProfile);
                if (p == null) { p = Profiles[0]; ActiveProfile = p.Id; }
                return p;
            }
        }
    }

    static class ConfigStore
    {
        public static string Folder { get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlightDeck"); } }
        static string FilePath { get { return System.IO.Path.Combine(Folder, "config.json"); } }

        public static Config Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var c = new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(FilePath));
                    if (c != null)
                    {
                        if (c.Profiles == null) c.Profiles = new List<Profile>();
                        if (c.Profiles.Count == 0) c.Profiles.Add(new Profile { Name = "Default", Apps = c.Apps ?? new List<AppEntry>() });
                        c.Apps = null;
                        foreach (var p in c.Profiles)
                        {
                            if (string.IsNullOrEmpty(p.Id)) p.Id = Guid.NewGuid().ToString("N");
                            if (string.IsNullOrWhiteSpace(p.Name)) p.Name = "Profile";
                            if (p.Apps == null) p.Apps = new List<AppEntry>();
                            foreach (var a in p.Apps) if (string.IsNullOrEmpty(a.Id)) a.Id = Guid.NewGuid().ToString("N");
                        }
                        if (c.LaunchTimeout <= 0) c.LaunchTimeout = 120;
                        if (c.CloseTimeout <= 0) c.CloseTimeout = 10;
                        return c;
                    }
                }
            }
            catch
            {
                // keep the unreadable file instead of overwriting it
                try { File.Copy(FilePath, System.IO.Path.Combine(Folder, "config.unreadable.json"), true); } catch { }
            }
            return new Config();
        }

        public static void Save(Config c)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string json = Pretty(new JavaScriptSerializer().Serialize(c));
                File.WriteAllText(FilePath + ".tmp", json);
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(FilePath + ".tmp", FilePath);
            }
            catch { }
        }

        // Minimal JSON indenter so the file stays readable/editable by hand.
        static string Pretty(string json)
        {
            var sb = new System.Text.StringBuilder();
            int ind = 0; bool str = false, esc = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (str)
                {
                    sb.Append(c);
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') str = false;
                    continue;
                }
                if (c == '"') { str = true; sb.Append(c); continue; }
                switch (c)
                {
                    case '{': case '[': sb.Append(c).Append('\n').Append(' ', ++ind * 2); break;
                    case '}': case ']': sb.Append('\n').Append(' ', --ind * 2).Append(c); break;
                    case ',': sb.Append(c).Append('\n').Append(' ', ind * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }

    static class Util
    {
        public static bool IsElevated()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        /// <summary>Monitors ordered left→right, top→bottom (Monitor 1, 2, ...).</summary>
        public static Screen[] Screens()
        {
            return Screen.AllScreens.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToArray();
        }

        public static int MonitorIndex(Rectangle r)
        {
            var screens = Screens();
            var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            for (int i = 0; i < screens.Length; i++) if (screens[i].Bounds.Contains(c)) return i;
            int best = 0; long bestArea = -1;
            for (int i = 0; i < screens.Length; i++)
            {
                var x = Rectangle.Intersect(screens[i].Bounds, r);
                long a = (long)x.Width * x.Height;
                if (a > bestArea) { bestArea = a; best = i; }
            }
            return best;
        }

        public static string Placement(AppEntry a)
        {
            if (!a.HasPos) return "Not set";
            int m = MonitorIndex(new Rectangle(a.X, a.Y, Math.Max(1, a.W), Math.Max(1, a.H))) + 1;
            return a.Maximized ? "Monitor " + m + " · maximized" : "Monitor " + m + " · " + a.W + "×" + a.H;
        }

        /// <summary>
        /// Closes tiny gaps/overlaps (up to 12 px) between saved windows and against the
        /// monitor's work area, so snapped layouts come back edge to edge.
        /// </summary>
        public static void TidyEdges(IList<AppEntry> apps)
        {
            const int T = 12;
            var list = apps.Where(a => a.HasPos && !a.Maximized && a.W > 0 && a.H > 0).ToList();
            var screens = Screens();
            foreach (var a in list)
            {
                var wa = screens[MonitorIndex(new Rectangle(a.X, a.Y, a.W, a.H))].WorkingArea;
                int r = a.X + a.W, b = a.Y + a.H;
                if (Math.Abs(a.X - wa.Left) <= T) a.X = wa.Left;
                if (Math.Abs(a.Y - wa.Top) <= T) a.Y = wa.Top;
                if (Math.Abs(r - wa.Right) <= T) r = wa.Right;
                if (Math.Abs(b - wa.Bottom) <= T) b = wa.Bottom;
                a.W = r - a.X; a.H = b - a.Y;
            }
            for (int i = 0; i < list.Count; i++)
                for (int j = 0; j < list.Count; j++)
                {
                    if (i == j) continue;
                    var a = list[i]; var c = list[j];
                    // a on the left, c on the right
                    int overlapV = Math.Min(a.Y + a.H, c.Y + c.H) - Math.Max(a.Y, c.Y);
                    int gapX = c.X - (a.X + a.W);
                    if (overlapV > 0 && gapX != 0 && Math.Abs(gapX) <= T && c.X > a.X)
                    {
                        int mid = (a.X + a.W + c.X) / 2;
                        int cr = c.X + c.W;
                        a.W = mid - a.X; c.X = mid; c.W = cr - mid;
                    }
                    // a on top, c below
                    int overlapH = Math.Min(a.X + a.W, c.X + c.W) - Math.Max(a.X, c.X);
                    int gapY = c.Y - (a.Y + a.H);
                    if (overlapH > 0 && gapY != 0 && Math.Abs(gapY) <= T && c.Y > a.Y)
                    {
                        int mid = (a.Y + a.H + c.Y) / 2;
                        int cb = c.Y + c.H;
                        a.H = mid - a.Y; c.Y = mid; c.H = cb - mid;
                    }
                }
        }

        public static string Initials(string name)
        {
            var parts = (name ?? "").Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
            return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();
        }

        public static bool ResolveShortcut(string lnk, out string target, out string args)
        {
            target = ""; args = "";
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                object sh = Activator.CreateInstance(t);
                object l = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                var lt = l.GetType();
                target = (string)lt.InvokeMember("TargetPath", BindingFlags.GetProperty, null, l, null) ?? "";
                args = (string)lt.InvokeMember("Arguments", BindingFlags.GetProperty, null, l, null) ?? "";
                return target != "";
            }
            catch { return false; }
        }

        public static void CreateShortcut(string lnkPath, string args, string description) { CreateShortcut(lnkPath, args, description, null); }

        public static void CreateShortcut(string lnkPath, string args, string description, string iconPath)
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(t);
            object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnkPath });
            var lt = lnk.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { Application.ExecutablePath });
            lt.InvokeMember("Arguments", BindingFlags.SetProperty, null, lnk, new object[] { args });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { System.IO.Path.GetDirectoryName(Application.ExecutablePath) });
            lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { (iconPath ?? Application.ExecutablePath) + ",0" });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { description });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
        }
    }

    static class Engine
    {
        public static bool IsRunning(AppEntry a)
        {
            if (string.IsNullOrWhiteSpace(a.Process)) return false;
            var ps = Process.GetProcessesByName(a.Process);
            foreach (var p in ps) p.Dispose();
            return ps.Length > 0;
        }

        public static WinInfo FindWindow(AppEntry a, List<WinInfo> all = null)
        {
            if (string.IsNullOrWhiteSpace(a.Process)) return null;
            var ps = Process.GetProcessesByName(a.Process);
            if (ps.Length == 0) return null;
            var pids = new HashSet<int>(ps.Select(p => p.Id));
            foreach (var p in ps) p.Dispose();

            WinInfo best = null;
            foreach (var w in all ?? Native.ListWindows())
            {
                if (!pids.Contains(w.Pid)) continue;
                if (!string.IsNullOrWhiteSpace(a.Title) && w.Title.IndexOf(a.Title.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (best == null || w.Area > best.Area) best = w;
            }
            return best;
        }

        public static HashSet<int> Pids(AppEntry a)
        {
            var set = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(a.Process)) return set;
            foreach (var p in Process.GetProcessesByName(a.Process)) { set.Add(p.Id); p.Dispose(); }
            return set;
        }

        /// <summary>If the app is running but hiding in the tray, show its main window. Returns the window title shown, or null.</summary>
        public static string RevealFromTray(AppEntry a)
        {
            var pids = Pids(a);
            if (pids.Count == 0) return null;
            var c = Native.HiddenCandidates(pids, a.Title, a.Name);
            if (c.Count == 0) return null;
            Native.Reveal(c[0].Handle);
            return c[0].Title;
        }

        public static void Launch(AppEntry a, bool selfElevated)
        {
            string path = Environment.ExpandEnvironmentVariables((a.Path ?? "").Trim().Trim('"'));
            ProcessStartInfo psi;
            if (a.IsUrl)
            {
                psi = new ProcessStartInfo(path) { UseShellExecute = true };
            }
            else
            {
                if (!File.Exists(path)) throw new FileNotFoundException("file not found: " + path);
                bool isExe = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                if (selfElevated && !a.Admin && isExe && string.IsNullOrWhiteSpace(a.Args))
                {
                    // Start as a normal user even though FlightDeck is elevated.
                    psi = new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true };
                }
                else
                {
                    psi = new ProcessStartInfo(path, a.Args ?? "") { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(path) };
                    if (a.Admin && !selfElevated) psi.Verb = "runas";
                }
            }
            try { using (Process.Start(psi)) { } }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) throw new Exception("administrator prompt was cancelled");
                throw;
            }
        }
    }

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
        public bool Busy { get; private set; }
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
            Run(positionOnly ? "apply" : "start", () => DoStart(apps, positionOnly));
        }

        class Job
        {
            public AppEntry App; public bool WasRunning; public DateTime? Found; public int Passes; public bool Warned;
            public IntPtr Handle; public string Title; public DateTime? VerifyStart; public DateTime LastFix; public int Good, Fixes; public bool Refused;
            public DateTime Launched = DateTime.Now; public DateTime LastReveal; public int Reveals, TrayClicks; public bool InTray;
        }

        public event Action<string> Debug;     // detailed lines: written to the log file only
        void D(string m) { var h = Debug; if (h != null) h(m); }

        static string Fr(RECT r) { return r.Left + "," + r.Top + " " + r.Width + "x" + r.Height; }

        static bool AtTarget(WinInfo w, AppEntry a)
        {
            if (a.Maximized) return w.Maximized;
            const int tol = 3;
            return !w.Minimized && !w.Maximized &&
                   Math.Abs(w.Frame.Left - a.X) <= tol && Math.Abs(w.Frame.Top - a.Y) <= tol &&
                   Math.Abs(w.Frame.Width - a.W) <= tol && Math.Abs(w.Frame.Height - a.H) <= tol;
        }

        void DoStart(List<AppEntry> apps, bool positionOnly)
        {
            if (apps.Count == 0) { L("No apps enabled — add some on the Apps page."); return; }
            L(positionOnly ? "Applying layout…" : "Starting flight…");
            bool elevated = Util.IsElevated();
            var jobs = new List<Job>();

            foreach (var a in apps)
            {
                if (cancel) return;
                var w = Engine.FindWindow(a);
                if (w == null && Engine.IsRunning(a))
                {
                    // already running but no window on screen: it's parked in the tray
                    S(a, "Positioning");
                    jobs.Add(new Job { App = a, WasRunning = true, InTray = true, Launched = DateTime.Now.AddSeconds(-60) });
                    continue;
                }
                if (w == null && !positionOnly)
                {
                    if (string.IsNullOrWhiteSpace(a.Path)) { L(a.Name + ": not running and no program path set — skipped."); S(a, "Skipped"); continue; }
                    try
                    {
                        Engine.Launch(a, elevated);
                        L("Launching " + a.Name + "…");
                        S(a, "Starting");
                    }
                    catch (Exception ex) { L(a.Name + ": could not start (" + ex.Message + ")."); S(a, "Error"); continue; }
                    Thread.Sleep(400);
                }
                else if (w == null) { continue; }
                else S(a, "Positioning");
                jobs.Add(new Job { App = a, WasRunning = w != null });
            }

            int nApps = apps.Count, totalSteps = nApps + (nApps > 1 ? 1 : 0);
            P(nApps - jobs.Count, totalSteps, positionOnly ? "Placing windows…" : "Opening apps…");
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
                    if (j.Found == null) { j.Found = DateTime.Now; S(a, "Positioning"); D(a.Name + ": window found \"" + w.Title + "\" at " + Fr(w.Frame) + (w.Minimized ? " (minimized)" : w.Maximized ? " (maximized)" : "")); }
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
                    if ((DateTime.Now - j.VerifyStart.Value).TotalSeconds > settleSeconds)
                    {
                        jobs.Remove(j); S(a, "Error");
                        L(a.Name + ": keeps moving away from its position (now " + Fr(w.Frame) + "). Try a longer WAIT for it.");
                        continue;
                    }
                    if ((DateTime.Now - j.LastFix).TotalSeconds >= 1.2)
                    {
                        D(a.Name + ": off target at " + Fr(w.Frame) + " — correcting");
                        j.Fixes++;
                        PlaceJob(j, w);
                    }
                }
                if (nApps - jobs.Count != lastDone) P(nApps - jobs.Count, totalSteps, (nApps - jobs.Count) + " of " + nApps + " apps ready");
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
            if (apps.Count > 1)
            {
                // Final step, after the last (slowest) app was placed: set which window is in front.
                var bs = BeforeStacking; if (bs != null) bs(positionOnly ? "apply" : "start");
                P(nApps, totalSteps, "Setting the window order…");
                Thread.Sleep(1500);
                if (cancel) return;
                ApplyStacking(apps, false);
                Thread.Sleep(2500);            // some apps bring themselves forward when they finish loading
                if (cancel) return;
                ApplyStacking(apps, true);
                L("Stacking order applied (front → back): " + string.Join(" › ", apps.Select(a => a.Name)));
            }
            P(totalSteps, totalSteps, positionOnly ? "Layout applied" : "All set");
            L(positionOnly ? "Layout applied." : "Flight ready. Have a good one ✈");
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
            var all = Native.ListWindows();
            var closing = new List<AppEntry>();
            foreach (var a in apps)
            {
                var ps = Process.GetProcessesByName(a.Process);
                if (ps.Length == 0) { S(a, "Closed"); continue; }
                var pids = new HashSet<int>(ps.Select(p => p.Id));
                foreach (var p in ps) p.Dispose();
                foreach (var w in all) if (pids.Contains(w.Pid)) Native.PostMessage(w.Handle, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                S(a, "Closing");
                closing.Add(a);
            }

            int nEnd = apps.Count;
            P(nEnd - closing.Count, nEnd, "Closing apps…");
            var start = DateTime.Now;
            var inTray = new HashSet<AppEntry>();
            while (closing.Count > 0 && (DateTime.Now - start).TotalSeconds < cfg.CloseTimeout)
            {
                var now = Native.ListWindows();
                foreach (var a in closing.ToArray())
                {
                    if (!Engine.IsRunning(a)) { closing.Remove(a); S(a, "Closed"); L(a.Name + ": closed."); continue; }
                    // window gone but the program keeps running: it went to the tray instead of quitting
                    if (!inTray.Contains(a) && (DateTime.Now - start).TotalSeconds > 1 && Engine.FindWindow(a, now) == null)
                    {
                        inTray.Add(a);
                        S(a, "In tray");
                        int left = (int)Math.Ceiling(cfg.CloseTimeout - (DateTime.Now - start).TotalSeconds);
                        L(a.Name + " went to the tray instead of closing" + (cfg.ForceClose ? " — FlightDeck will close it in " + Math.Max(1, left) + " s." : "."));
                    }
                }
                if (nEnd - closing.Count != lastDone) P(nEnd - closing.Count, nEnd, (nEnd - closing.Count) + " of " + nEnd + " apps closed");
                if (closing.Count > 0) Thread.Sleep(400);
            }

            if (closing.Count > 0) P(nEnd - closing.Count, nEnd, "Closing the apps that are still running…");
            foreach (var a in closing)
            {
                if (!cfg.ForceClose) { S(a, "Running"); L(a.Name + ": still running (force close is off)."); continue; }
                bool denied = false;
                foreach (var p in Process.GetProcessesByName(a.Process))
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
                else { S(a, "Closed"); L(a.Name + (inTray.Contains(a) ? ": closed from the tray." : ": closed (forced).")); }
            }
            P(nEnd, nEnd, "Flight ended");
            L("Flight ended.");
        }
    }
}
