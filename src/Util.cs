using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;

namespace FlightDeck
{
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


        /// <summary>Icon files that follow the accent colour (AppData\FlightDeck\icons).</summary>
        public static string IconPath(string args)
        {
            string name = args == "--start" ? "start-flight.ico" : args == "--end" ? "end-flight.ico" : "flightdeck.ico";
            return System.IO.Path.Combine(ConfigStore.Folder, "icons", name);
        }

        public static void WriteShortcutIcons()
        {
            Theme.SaveLogoIcon(IconPath(""));
            Theme.SaveFlightIcon(IconPath("--start"), true);
            Theme.SaveFlightIcon(IconPath("--end"), false);
        }

        /// <summary>
        /// Re-points every shortcut to FlightDeck at the icons in the current accent colour: Desktop,
        /// Start menu (incl. "pinned to Start") and the taskbar pins — Windows draws pinned apps from these .lnk files.
        /// </summary>
        public static int RefreshDesktopShortcuts()
        {
            string exe = Application.ExecutablePath;
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folders = new List<KeyValuePair<string, bool>>   // folder, recursive
            {
                new KeyValuePair<string, bool>(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), false),
                new KeyValuePair<string, bool>(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), false),
                new KeyValuePair<string, bool>(Environment.GetFolderPath(Environment.SpecialFolder.Programs), true),
                new KeyValuePair<string, bool>(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), true),
                new KeyValuePair<string, bool>(System.IO.Path.Combine(roaming, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"), false),
                new KeyValuePair<string, bool>(System.IO.Path.Combine(roaming, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\StartMenu"), false),
                new KeyValuePair<string, bool>(System.IO.Path.Combine(roaming, @"Microsoft\Internet Explorer\Quick Launch"), false),
            };
            var touched = new List<string>();
            object sh = null; Type t = null;
            try { t = Type.GetTypeFromProgID("WScript.Shell"); sh = Activator.CreateInstance(t); } catch { return 0; }
            foreach (var f in folders)
            {
                if (string.IsNullOrEmpty(f.Key) || !System.IO.Directory.Exists(f.Key)) continue;
                string[] files;
                try { files = System.IO.Directory.GetFiles(f.Key, "*.lnk", f.Value ? System.IO.SearchOption.AllDirectories : System.IO.SearchOption.TopDirectoryOnly); }
                catch { continue; }
                foreach (var p in files)
                {
                    try
                    {
                        object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { p });
                        var lt = lnk.GetType();
                        string target = (string)lt.InvokeMember("TargetPath", BindingFlags.GetProperty, null, lnk, null) ?? "";
                        if (!target.Equals(exe, StringComparison.OrdinalIgnoreCase)) continue;   // not a FlightDeck shortcut
                        string args = ((string)lt.InvokeMember("Arguments", BindingFlags.GetProperty, null, lnk, null) ?? "").Trim();
                        string kind = args.IndexOf("--start", StringComparison.OrdinalIgnoreCase) >= 0 ? "--start"
                                    : args.IndexOf("--end", StringComparison.OrdinalIgnoreCase) >= 0 ? "--end" : "";
                        lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { IconPath(kind) + ",0" });
                        lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                        touched.Add(p);
                    }
                    catch { }
                }
            }
            var refresh = new List<string>(touched) { IconPath(""), IconPath("--start"), IconPath("--end") };
            Native.RefreshShellIcons(refresh);
            // ask Explorer to redraw cached icons (taskbar, Start)
            try
            {
                var ie = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ie4uinit.exe");
                if (System.IO.File.Exists(ie))
                    using (var pr = Process.Start(new ProcessStartInfo(ie, "-show") { UseShellExecute = false, CreateNoWindow = true })) { pr.WaitForExit(5000); }
            }
            catch { }
            return touched.Count;
        }

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
}
