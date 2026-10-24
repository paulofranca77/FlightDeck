using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FlightDeck
{
    /// <summary>
    /// Finds an app's icon in the notification area (system tray) and clicks it — for apps that start
    /// "in the tray" and only create their window when the icon is clicked.
    /// Uses Windows UI Automation (loaded at runtime from the .NET Framework).
    /// </summary>
    static class Tray
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
        struct POINT { public int X, Y; }

        static Assembly client, types;
        static Type tAE, tCond, tPropCond, tTreeScope, tControlType, tInvoke;
        static bool loaded, failed;

        static bool Load()
        {
            if (loaded) return true;
            if (failed) return false;
            try
            {
                client = Assembly.Load("UIAutomationClient, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                types = Assembly.Load("UIAutomationTypes, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                tAE = client.GetType("System.Windows.Automation.AutomationElement", true);
                tCond = client.GetType("System.Windows.Automation.Condition", true);
                tPropCond = client.GetType("System.Windows.Automation.PropertyCondition", true);
                tInvoke = client.GetType("System.Windows.Automation.InvokePattern", true);
                tTreeScope = types.GetType("System.Windows.Automation.TreeScope", true);
                tControlType = types.GetType("System.Windows.Automation.ControlType", true);
                loaded = true;
                return true;
            }
            catch { failed = true; return false; }
        }

        class Btn { public object El; public string Name; public double X, Y, W, H; }

        static object Static(Type t, string field)
        {
            var f = t.GetField(field, BindingFlags.Public | BindingFlags.Static);
            return f != null ? f.GetValue(null) : t.GetProperty(field, BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
        }

        static List<Btn> Buttons(IntPtr root)
        {
            var list = new List<Btn>();
            if (root == IntPtr.Zero) return list;
            try
            {
                object el = tAE.GetMethod("FromHandle").Invoke(null, new object[] { root });
                object cond = Activator.CreateInstance(tPropCond, Static(tAE, "ControlTypeProperty"), Static(tControlType, "Button"));
                object scope = Enum.ToObject(tTreeScope, 4 /*Descendants*/);
                var all = (IEnumerable)tAE.GetMethod("FindAll").Invoke(el, new[] { scope, cond });
                foreach (object b in all)
                {
                    object cur = tAE.GetProperty("Current").GetValue(b, null);
                    string name = (string)cur.GetType().GetProperty("Name").GetValue(cur, null) ?? "";
                    object rect = cur.GetType().GetProperty("BoundingRectangle").GetValue(cur, null);
                    Func<string, double> rp = p => { try { return Convert.ToDouble(rect.GetType().GetProperty(p).GetValue(rect, null)); } catch { return 0; } };
                    list.Add(new Btn { El = b, Name = name, X = rp("X"), Y = rp("Y"), W = rp("Width"), H = rp("Height") });
                }
            }
            catch { }
            return list;
        }

        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);

        static IEnumerable<IntPtr> Roots()
        {
            var roots = new List<IntPtr> { FindWindow("Shell_TrayWnd", null) };
            // "hidden icons" flyout: Windows 11 (TopLevelWindowForOverflowXamlIsland), Windows 10 (NotifyIconOverflowWindow),
            // or whatever newer builds call it
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                var cb = new StringBuilder(256); GetClassName(h, cb, cb.Capacity);
                string c = cb.ToString();
                if (c.IndexOf("Overflow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (c.IndexOf("XamlIsland", StringComparison.OrdinalIgnoreCase) >= 0 && IsWindowVisible(h)))
                    roots.Add(h);
                return true;
            }, IntPtr.Zero);
            return roots.Where(r => r != IntPtr.Zero).Distinct();
        }

        static readonly string[] chevronNames = { "hidden icons", "ícones ocultos", "iconos ocultos", "icônes cachées", "ausgeblendete symbole" };

        static string Norm(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in (s ?? "").ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        static List<string> Keys(AppEntry a)
        {
            var keys = new List<string>();
            Action<string> add = s =>
            {
                if (string.IsNullOrWhiteSpace(s)) return;
                string n = Norm(s); if (n.Length >= 4) keys.Add(n);
                string first = Norm(s.Split(new[] { ' ', '_', '-', '.', '(' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());
                if (first.Length >= 5) keys.Add(first);
            };
            add(a.Name); add(a.Process); add(a.Title);
            try
            {
                if (!string.IsNullOrEmpty(a.Path) && File.Exists(a.Path))
                {
                    add(Path.GetFileNameWithoutExtension(a.Path));
                    add(FileVersionInfo.GetVersionInfo(a.Path).FileDescription);
                    add(FileVersionInfo.GetVersionInfo(a.Path).ProductName);
                }
            }
            catch { }
            return keys.Distinct().ToList();
        }

        static Btn Match(List<Btn> btns, List<string> keys)
        {
            foreach (var b in btns)
            {
                string n = Norm(b.Name);
                if (n.Length < 3 || chevronNames.Any(c => b.Name.ToLowerInvariant().Contains(c))) continue;
                foreach (var k in keys) if (n.Contains(k) || (n.Length >= 6 && k.Contains(n))) return b;
            }
            return null;
        }

        static bool Click(Btn b)
        {
            try
            {
                object pattern; var args = new object[] { Static(tInvoke, "Pattern"), null };
                if ((bool)tAE.GetMethod("TryGetCurrentPattern").Invoke(b.El, args))
                {
                    pattern = args[1];
                    tInvoke.GetMethod("Invoke").Invoke(pattern, null);
                    return true;
                }
            }
            catch { }
            // fallback: a real left click on the icon, then put the mouse back
            if (b.W <= 0 || b.H <= 0) return false;
            POINT old; GetCursorPos(out old);
            SetCursorPos((int)(b.X + b.W / 2), (int)(b.Y + b.H / 2));
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(40); mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(60);
            SetCursorPos(old.X, old.Y);
            return true;
        }

        /// <summary>Clicks the app's tray icon. Returns the icon's name, or null if it wasn't found.</summary>
        public static string ClickIcon(AppEntry a, Action<string> debug)
        {
            if (!Load()) { if (debug != null) debug("tray: UI Automation not available"); return null; }
            var keys = Keys(a);
            foreach (var root in Roots())
            {
                var hit = Match(Buttons(root), keys);
                if (hit != null) return Click(hit) ? hit.Name : null;
            }
            // not among the visible icons: open the "hidden icons" flyout and look there
            var chevron = Buttons(FindWindow("Shell_TrayWnd", null)).FirstOrDefault(b => chevronNames.Any(c => b.Name.ToLowerInvariant().Contains(c)));
            if (chevron == null) { if (debug != null) debug("tray: no icon matching " + string.Join("/", keys)); return null; }
            Click(chevron);
            Thread.Sleep(700);
            var seen = new List<string>();
            foreach (var root in Roots())
            {
                var btns = Buttons(root);
                seen.AddRange(btns.Select(b => b.Name));
                var hit = Match(btns, keys);
                if (hit != null) { bool ok = Click(hit); return ok ? hit.Name : null; }
            }
            Click(chevron);   // close the flyout again
            if (debug != null) debug("tray: no icon matching " + string.Join("/", keys) + " — icons seen with hidden icons open: " +
                string.Join(" | ", seen.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Select(x => x.Replace("\r", " ").Replace("\n", " "))));
            return null;
        }

        /// <summary>Names of the icons currently in the tray (for diagnostics).</summary>
        public static string List()
        {
            if (!Load()) return "(UI Automation not available)";
            var names = new List<string>();
            foreach (var root in Roots()) names.AddRange(Buttons(root).Select(b => b.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
            return names.Count == 0 ? "(none found)" : string.Join(" | ", names.Distinct().Select(x => x.Replace("\r", " ").Replace("\n", " ")));
        }
    }
}
