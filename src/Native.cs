using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace FlightDeck
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
    }

    public class WinInfo
    {
        public IntPtr Handle;
        public int Pid;
        public string Title;
        public string ProcessName;
        public RECT Frame;          // visible bounds (what you see on screen)
        public bool Maximized;
        public bool Minimized;
        public long Area { get { return Minimized ? 0 : (long)Frame.Width * Frame.Height; } }
    }

    static class Native
    {
        delegate bool EnumProc(IntPtr h, IntPtr l);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[] ids, uint count, uint flags);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("shell32.dll")] public static extern void DragAcceptFiles(IntPtr h, bool accept);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint DragQueryFile(IntPtr hDrop, uint i, StringBuilder file, uint len);
        [DllImport("shell32.dll")] static extern void DragFinish(IntPtr hDrop);

        public static string[] DroppedFiles(IntPtr hDrop)
        {
            var list = new List<string>();
            uint n = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
            for (uint i = 0; i < n; i++)
            {
                var sb = new StringBuilder(1024);
                DragQueryFile(hDrop, i, sb, (uint)sb.Capacity);
                list.Add(sb.ToString());
            }
            DragFinish(hDrop);
            return list.ToArray();
        }
        [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        /// <summary>Tells Explorer to drop cached icons for these files (exe and its shortcuts).</summary>
        public static void RefreshShellIcons(System.Collections.Generic.IEnumerable<string> files)
        {
            foreach (var f in files)
            {
                IntPtr p = Marshal.StringToHGlobalUni(f);
                try { SHChangeNotify(0x00002000 /*SHCNE_UPDATEITEM*/, 0x0005 /*SHCNF_PATHW*/, p, IntPtr.Zero); }
                finally { Marshal.FreeHGlobal(p); }
            }
            SHChangeNotify(0x08000000 /*SHCNE_ASSOCCHANGED*/, 0, IntPtr.Zero, IntPtr.Zero);
        }
        [DllImport("user32.dll")] public static extern bool ChangeWindowMessageFilterEx(IntPtr h, uint msg, uint action, IntPtr info);

        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idList);

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);

        public const uint WM_CLOSE = 0x0010;

        // ---------- graceful shutdown ----------
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);

        /// <summary>Every top-level window of these processes, hidden ones included (tray apps keep theirs hidden).</summary>
        public static List<IntPtr> AllWindowsOf(HashSet<int> pids)
        {
            var list = new List<IntPtr>();
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pids.Contains((int)pid)) list.Add(h);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        /// <summary>
        /// Tells the processes that Windows is closing them, the same messages a shutdown or an
        /// installer restart sends. Well-behaved apps (WPF, WinForms, Electron, Chromium) save and
        /// exit cleanly instead of hiding in the tray. Returns how many windows were told.
        /// </summary>
        public static int AskToEndSession(HashSet<int> pids)
        {
            const uint WM_QUERYENDSESSION = 0x0011, WM_ENDSESSION = 0x0016;
            const uint SMTO_ABORTIFHUNG = 0x0002;
            var ENDSESSION_CLOSEAPP = new IntPtr(1);
            int n = 0;
            var wins = AllWindowsOf(pids);
            foreach (var h in wins)
            {
                IntPtr r;
                if (SendMessageTimeout(h, WM_QUERYENDSESSION, IntPtr.Zero, ENDSESSION_CLOSEAPP, SMTO_ABORTIFHUNG, 3000, out r) != IntPtr.Zero) n++;
            }
            foreach (var h in wins)
            {
                IntPtr r;
                SendMessageTimeout(h, WM_ENDSESSION, new IntPtr(1), ENDSESSION_CLOSEAPP, SMTO_ABORTIFHUNG, 3000, out r);
            }
            return n;
        }

        // ---------- SimConnect ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WIN32_FIND_DATA
        {
            public uint attr; public long created, accessed, written; public uint sizeHigh, sizeLow, r0, r1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string alt;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr FindFirstFile(string path, out WIN32_FIND_DATA d);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool FindNextFile(IntPtr h, out WIN32_FIND_DATA d);
        [DllImport("kernel32.dll")] static extern bool FindClose(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool WaitNamedPipe(string name, uint timeout);

        /// <summary>
        /// True once a simulator's SimConnect server is up (MSFS 2020/2024, P3D…). The simulator opens
        /// its SimConnect pipe during its main load, so this is the moment add-ons can safely connect.
        /// Only looks at the pipe's name: nothing connects to the simulator.
        /// </summary>
        public static bool SimConnectReady()
        {
            try
            {
                WIN32_FIND_DATA d;
                var h = FindFirstFile(@"\\.\pipe\*", out d);
                if (h != new IntPtr(-1))
                {
                    try
                    {
                        do { if (d.name != null && d.name.IndexOf("SimConnect", StringComparison.OrdinalIgnoreCase) >= 0) return true; }
                        while (FindNextFile(h, out d));
                    }
                    finally { FindClose(h); }
                }
            }
            catch { }
            // fallback: the default MSFS pipe (exists but busy = 121/231 also counts as up)
            try
            {
                if (WaitNamedPipe(@"\\.\pipe\Microsoft Flight Simulator\SimConnect", 1)) return true;
                int err = Marshal.GetLastWin32Error();
                return err == 121 || err == 231;
            }
            catch { return false; }
        }
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        // ---------- windows ----------
        public static RECT GetFrame(IntPtr h)
        {
            RECT r;
            if (DwmGetWindowAttribute(h, 9 /*DWMWA_EXTENDED_FRAME_BOUNDS*/, out r, Marshal.SizeOf(typeof(RECT))) == 0 && r.Width > 0 && r.Height > 0)
                return r;
            GetWindowRect(h, out r);
            return r;
        }

        static bool IsCloaked(IntPtr h)
        {
            int v;
            return DwmGetWindowAttribute(h, 14 /*DWMWA_CLOAKED*/, out v, 4) == 0 && v != 0;
        }

        /// <summary>Top-level, visible, titled application windows.</summary>
        public static List<WinInfo> ListWindows()
        {
            var list = new List<WinInfo>();
            var names = new Dictionary<int, string>();
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h)) return true;
                if (GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero) return true;
                if ((GetWindowLong(h, -20) & 0x80 /*WS_EX_TOOLWINDOW*/) != 0) return true;
                int len = GetWindowTextLength(h);
                if (len == 0) return true;
                if (IsCloaked(h)) return true;
                var sb = new StringBuilder(len + 1);
                GetWindowText(h, sb, sb.Capacity);
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                string pn;
                if (!names.TryGetValue((int)pid, out pn))
                {
                    try { using (var p = Process.GetProcessById((int)pid)) pn = p.ProcessName; } catch { pn = ""; }
                    names[(int)pid] = pn;
                }
                list.Add(new WinInfo
                {
                    Handle = h,
                    Pid = (int)pid,
                    Title = sb.ToString(),
                    ProcessName = pn,
                    Frame = GetFrame(h),
                    Maximized = IsZoomed(h),
                    Minimized = IsIconic(h)
                });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        /// <summary>
        /// Places a window so that its VISIBLE area matches x/y/w/h exactly
        /// (Windows 10/11 windows have invisible resize borders; snapped
        /// windows are measured by their visible edges).
        /// Returns false when Windows refused (usually: target runs as administrator).
        /// </summary>
        public static bool Place(IntPtr h, int x, int y, int w, int hgt, bool maximize)
        {
            for (int i = 0; i < 4 && (IsIconic(h) || IsZoomed(h)); i++)
            {
                // minimized → show again without stealing focus; maximized → back to a normal window
                ShowWindow(h, IsIconic(h) ? 4 /*SW_SHOWNOACTIVATE*/ : 9 /*SW_RESTORE*/);
                System.Threading.Thread.Sleep(150);   // let the restore animation finish before measuring
            }

            RECT win; GetWindowRect(h, out win);
            RECT fr = GetFrame(h);
            int l = fr.Left - win.Left, t = fr.Top - win.Top, r = win.Right - fr.Right, b = win.Bottom - fr.Bottom;
            if (l < 0 || l > 50) l = 0;
            if (t < 0 || t > 50) t = 0;
            if (r < 0 || r > 50) r = 0;
            if (b < 0 || b > 50) b = 0;

            const uint flags = 0x0004 | 0x0010; // NOZORDER | NOACTIVATE
            bool ok = SetWindowPos(h, IntPtr.Zero, x - l, y - t, w + l + r, hgt + t + b, flags);

            // Verify what is actually visible and correct small differences
            // (apps whose borders/DPI handling differ leave gaps of a few pixels otherwise).
            for (int i = 0; ok && i < 3; i++)
            {
                RECT f = GetFrame(h);
                int dx = x - f.Left, dy = y - f.Top, dw = w - f.Width, dh = hgt - f.Height;
                if (dx == 0 && dy == 0 && dw == 0 && dh == 0) break;
                if (Math.Abs(dx) > 64 || Math.Abs(dy) > 64 || Math.Abs(dw) > 64 || Math.Abs(dh) > 64) break;   // app enforces its own size
                GetWindowRect(h, out win);
                ok = SetWindowPos(h, IntPtr.Zero, win.Left + dx, win.Top + dy, win.Width + dw, win.Height + dh, flags);
            }

            if (ok && maximize) ShowWindow(h, 3 /*SW_MAXIMIZE*/);
            return ok;
        }

        /// <summary>Raises a window to the top of the normal z-order without activating it.</summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);

        static readonly string[] helperClasses = { "IME", "MSCTFIME UI", "GDI+ Hook Window Class", "tooltips_class32", ".NET-BroadcastEventWindow",
            "Chrome_SystemMessageWindow", "CicMarshalWndClass", "OleMainThreadWndClass", "SystemTray_Main", "NotifyIconOverflowWindow" };
        static readonly string[] helperTitles = { "Default IME", "MSCTFIME UI", "GDI+ Window", "Hidden Window", "SystemResourceNotifyWindow", "DDE Server Window" };

        public class HiddenWin { public IntPtr Handle; public string Title, Class; public RECT Rect; public int Score; }

        /// <summary>Hidden top-level windows of these processes that look like a real app window (apps parked in the tray).</summary>
        public static List<HiddenWin> HiddenCandidates(HashSet<int> pids, string titleFilter, string appName)
        {
            var list = new List<HiddenWin>();
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                uint pid; GetWindowThreadProcessId(h, out pid);
                if (!pids.Contains((int)pid)) return true;
                if (IsWindowVisible(h) && !IsCloaked(h)) return true;           // visible ones are handled normally
                if (GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero) return true;
                int ex = GetWindowLong(h, -20), style = GetWindowLong(h, -16);
                if ((ex & 0x80 /*TOOLWINDOW*/) != 0 || (style & 0x40000000 /*WS_CHILD*/) != 0) return true;
                int len = GetWindowTextLength(h);
                if (len == 0) return true;
                var sb = new StringBuilder(len + 1); GetWindowText(h, sb, sb.Capacity);
                string title = sb.ToString();
                var cb = new StringBuilder(256); GetClassName(h, cb, cb.Capacity);
                string cls = cb.ToString();
                foreach (var c in helperClasses) if (cls.Equals(c, StringComparison.OrdinalIgnoreCase)) return true;
                foreach (var t in helperTitles) if (title.Equals(t, StringComparison.OrdinalIgnoreCase)) return true;
                if (!string.IsNullOrWhiteSpace(titleFilter) && title.IndexOf(titleFilter, StringComparison.OrdinalIgnoreCase) < 0) return true;
                RECT r; GetWindowRect(h, out r);
                bool iconic = IsIconic(h);
                if (!iconic && (r.Width < 160 || r.Height < 100)) return true;
                int score = 0;
                if ((style & 0x00C00000 /*WS_CAPTION*/) == 0x00C00000) score += 2;
                if ((style & 0x00040000 /*WS_THICKFRAME*/) != 0 || (style & 0x00020000 /*WS_MINIMIZEBOX*/) != 0) score += 1;
                if (!string.IsNullOrEmpty(appName) && title.IndexOf(appName.Split(' ')[0], StringComparison.OrdinalIgnoreCase) >= 0) score += 2;
                list.Add(new HiddenWin { Handle = h, Title = title, Class = cls, Rect = r, Score = score });
                return true;
            }, IntPtr.Zero);
            return list.OrderByDescending(x => x.Score).ThenByDescending(x => (long)x.Rect.Width * x.Rect.Height).ToList();
        }

        public class AnyWin { public IntPtr Handle; public string Title, Class; public bool Visible; }

        /// <summary>Every top-level window of these processes, hidden ones included (diagnostics + tray messages).</summary>
        public static List<AnyWin> AllWindows(HashSet<int> pids)
        {
            var list = new List<AnyWin>();
            EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                uint pid; GetWindowThreadProcessId(h, out pid);
                if (!pids.Contains((int)pid)) return true;
                int len = GetWindowTextLength(h);
                var sb = new StringBuilder(len + 1); if (len > 0) GetWindowText(h, sb, sb.Capacity);
                var cb = new StringBuilder(256); GetClassName(h, cb, cb.Capacity);
                list.Add(new AnyWin { Handle = h, Title = sb.ToString(), Class = cb.ToString(), Visible = IsWindowVisible(h) });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        /// <summary>
        /// Sends the app the same message Windows sends when its tray icon is clicked
        /// (WPF/Hardcodet/H.NotifyIcon and WinForms NotifyIcon). Returns how many icon windows got it.
        /// </summary>
        public static int PokeTrayIcon(HashSet<int> pids, bool doubleClick, Action<string> debug)
        {
            int n = 0;
            foreach (var w in AllWindows(pids))
            {
                uint cb;
                if (w.Class.StartsWith("WPFTaskbarIcon_", StringComparison.OrdinalIgnoreCase) ||
                    w.Class.StartsWith("H.NotifyIcon", StringComparison.OrdinalIgnoreCase)) cb = 0x0400;          // WM_USER
                else if (w.Class.StartsWith("WindowsForms10.Window", StringComparison.OrdinalIgnoreCase) && w.Title == "" && !w.Visible) cb = 0x0800; // WM_USER+1024
                else continue;
                uint[] seq = doubleClick ? new uint[] { 0x0201, 0x0202, 0x0203, 0x0202 } : new uint[] { 0x0200, 0x0201, 0x0202 };
                foreach (var m in seq)
                {
                    PostMessage(w.Handle, cb, IntPtr.Zero, new IntPtr((int)m));
                    System.Threading.Thread.Sleep(40);
                }
                if (debug != null) debug("tray message (" + (doubleClick ? "double-click" : "click") + ") → [" + w.Class + "]");
                n++;
            }
            return n;
        }

        /// <summary>Shows a window that an app hid in the tray, without taking focus.</summary>
        public static void Reveal(IntPtr h)
        {
            ShowWindow(h, IsIconic(h) ? 4 /*SW_SHOWNOACTIVATE*/ : 8 /*SW_SHOWNA*/);
            if (IsIconic(h)) ShowWindow(h, 4);
        }

        public static bool IsTopmost(IntPtr h) { return (GetWindowLong(h, -20) & 0x8 /*WS_EX_TOPMOST*/) != 0; }

        /// <summary>Takes away an app's own "always on top" so the stacking order from the list applies.</summary>
        public static void ClearTopmost(IntPtr h)
        {
            SetWindowPos(h, new IntPtr(-2) /*HWND_NOTOPMOST*/, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        public static void BringToTop(IntPtr h)
        {
            const uint f = 0x0001 | 0x0002 | 0x0010; // NOSIZE | NOMOVE | NOACTIVATE
            // Topmost → not-topmost puts the window above every normal window,
            // even when FlightDeck is not the foreground app.
            SetWindowPos(h, new IntPtr(-1) /*HWND_TOPMOST*/, 0, 0, 0, 0, f);
            SetWindowPos(h, new IntPtr(-2) /*HWND_NOTOPMOST*/, 0, 0, 0, 0, f);
        }

        // ---------- processes ----------
        public static string GetProcessPath(int pid)
        {
            IntPtr h = OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, pid);
            if (h == IntPtr.Zero) return "";
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : "";
            }
            finally { CloseHandle(h); }
        }

        // ---------- cosmetics ----------
        public static void DarkTitleBar(IntPtr h)
        {
            int v = Theme.IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(h, 20, ref v, 4) != 0) DwmSetWindowAttribute(h, 19, ref v, 4);
        }

        public static void RoundCorners(IntPtr h)
        {
            int v = 2; // DWMWCP_ROUND (Windows 11)
            try { DwmSetWindowAttribute(h, 33, ref v, 4); } catch { }
        }

        public static void DarkScrollbars(IntPtr h)
        {
            try { SetWindowTheme(h, Theme.IsDark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }
    }
}
