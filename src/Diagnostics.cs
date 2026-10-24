using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace FlightDeck
{
    /// <summary>Activity log file + a snapshot report to paste when asking for help.</summary>
    static class Diagnostics
    {
        public static string LogPath { get { return Path.Combine(ConfigStore.Folder, "activity.log"); } }
        public static string CrashPath { get { return Path.Combine(ConfigStore.Folder, "crash.log"); } }
        static readonly object gate = new object();

        public static void Write(string line)
        {
            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(ConfigStore.Folder);
                    var fi = new FileInfo(LogPath);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                    {
                        string old = LogPath + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(LogPath, old);
                    }
                    File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + line + Environment.NewLine);
                }
                catch { }
            }
        }

        public static string Tail(string path, int maxLines)
        {
            try
            {
                if (!File.Exists(path)) return "";
                var lines = File.ReadAllLines(path);
                return string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - maxLines)));
            }
            catch (Exception ex) { return "(could not read " + Path.GetFileName(path) + ": " + ex.Message + ")"; }
        }

        // ---- process elevation ----
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr token, int cls, out int info, int len, out int retLen);

        /// <summary>"yes", "no" or "probably (access denied)".</summary>
        public static string Elevated(int pid)
        {
            IntPtr p = OpenProcess(0x1000, false, pid);
            if (p == IntPtr.Zero) return "unknown";
            try
            {
                IntPtr tok;
                if (!OpenProcessToken(p, 0x0008, out tok)) return "probably (access denied)";
                try { int v, n; return GetTokenInformation(tok, 20, out v, 4, out n) ? (v != 0 ? "yes" : "no") : "unknown"; }
                finally { CloseHandle(tok); }
            }
            finally { CloseHandle(p); }
        }

        public static string ForApp(AppEntry a)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[" + a.Name + "]");
            sb.AppendLine("  enabled=" + a.Enabled + " admin=" + a.Admin + " autoClose=" + a.CloseOnEnd + " placeAfter=" + a.Delay + "s start=" + a.StartLabel);
            sb.AppendLine("  path=" + a.Path + (a.Args != "" ? "  args=" + a.Args : ""));
            sb.AppendLine("  process=\"" + a.Process + "\"" + (a.Title != "" ? "  titleFilter=\"" + a.Title + "\"" : ""));
            sb.AppendLine("  saved=" + (a.HasPos ? a.X + "," + a.Y + " " + a.W + "x" + a.H + (a.Maximized ? " maximized" : "") + "  (" + Util.Placement(a) + ")" : "none"));
            if (string.IsNullOrWhiteSpace(a.Process)) { sb.AppendLine("  (no process name)"); return sb.ToString(); }
            var procs = Engine.Processes(a);
            if (procs.Length == 0) sb.AppendLine("  running: no");
            var pids = new HashSet<int>();
            foreach (var p in procs)
            {
                pids.Add(p.Id);
                sb.AppendLine("  pid " + p.Id + "  elevated=" + Elevated(p.Id) + "  exe=" + Native.GetProcessPath(p.Id));
                p.Dispose();
            }
            if (pids.Count > 0)
            {
                var wins = Native.ListWindows().Where(w => pids.Contains(w.Pid)).ToList();
                var chosen = Engine.FindWindow(a);
                if (wins.Count == 0) sb.AppendLine("  visible windows: none");
                var every = Native.AllWindows(pids);
                sb.AppendLine("  all top-level windows (" + every.Count + "): " + string.Join(", ", every.Take(15).Select(x =>
                    "[" + x.Class + "]" + (x.Title != "" ? " \"" + x.Title + "\"" : "") + (x.Visible ? " visible" : ""))));
                foreach (var hw in Native.HiddenCandidates(pids, a.Title, a.Name).Take(3))
                    sb.AppendLine("  hidden window \"" + hw.Title + "\" [" + hw.Class + "] " + hw.Rect.Width + "x" + hw.Rect.Height + " score " + hw.Score);
                foreach (var w in wins)
                    sb.AppendLine("  window \"" + w.Title + "\"  " + w.Frame.Left + "," + w.Frame.Top + " " + w.Frame.Width + "x" + w.Frame.Height +
                        (w.Minimized ? " minimized" : w.Maximized ? " maximized" : "") + (Native.IsTopmost(w.Handle) ? " ALWAYS-ON-TOP" : "") + (chosen != null && chosen.Handle == w.Handle ? "  <= used" : ""));
            }
            // other processes with the same exe file name (wrong process name?)
            try
            {
                string exe = Path.GetFileNameWithoutExtension(a.Path ?? "");
                if (exe != "" && !exe.Equals(a.Process, StringComparison.OrdinalIgnoreCase))
                {
                    var others = Process.GetProcessesByName(exe);
                    if (others.Length > 0) sb.AppendLine("  note: process \"" + exe + "\" (from the path) is running — process name may be wrong");
                    foreach (var o in others) o.Dispose();
                }
            }
            catch { }
            return sb.ToString();
        }

        public static string Report(Config cfg)
        {
            var sb = new StringBuilder();
            sb.AppendLine("==== FlightDeck diagnostics ====");
            sb.AppendLine("Version " + Application.ProductVersion + " · " + Environment.OSVersion.VersionString + " · 64-bit OS: " + Environment.Is64BitOperatingSystem);
            sb.AppendLine("FlightDeck elevated: " + Util.IsElevated() + " · profile \"" + cfg.Active.Name + "\" · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            var screens = Util.Screens();
            for (int i = 0; i < screens.Length; i++)
                sb.AppendLine("Monitor " + (i + 1) + ": " + screens[i].Bounds + " work " + screens[i].WorkingArea + (screens[i].Primary ? " primary" : ""));
            try { sb.AppendLine("Tray icons: " + Tray.List()); } catch { }
            sb.AppendLine("Stacking order (front → back): " + string.Join(" › ", cfg.Active.Apps.Where(x => x.Enabled).Select(x => x.Name)));
            sb.AppendLine();
            foreach (var a in cfg.Active.Apps) sb.AppendLine(ForApp(a));
            return sb.ToString();
        }
    }
}
