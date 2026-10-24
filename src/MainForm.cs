using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FlightDeck
{
    class MainForm : DarkForm
    {
        public readonly Config Cfg;
        public readonly FlightRunner Runner;
        public readonly Dictionary<string, string> Live = new Dictionary<string, string>();   // app id → transient status
        public static uint CmdMessage;

        /// <summary>Apps of the active profile.</summary>
        public List<AppEntry> Apps { get { return Cfg.Active.Apps; } }

        // flight state
        public bool InFlight { get; private set; }
        Profile flightProfile;
        string runKind = "";          // "start" | "end" | "apply"
        bool exiting, handedOver;

        Panel sidebar, content;
        readonly List<KeyValuePair<NavButton, Page>> nav = new List<KeyValuePair<NavButton, Page>>();
        NavButton navSettings;
        Label version;
        readonly List<string> logLines = new List<string>();
        readonly NotifyIcon tray;
        readonly ContextMenuStrip trayMenu;
        public DashboardPage Dash;
        public AppsPage AppsPg;
        public LayoutPage LayoutPg;
        public SettingsPage SettingsPg;
        readonly string[] startArgs;

        public MainForm(Config cfg, string[] args)
        {
            Cfg = cfg;
            startArgs = args;
            Runner = new FlightRunner(cfg);
            try { foreach (var p in cfg.Profiles) Util.TidyEdges(p.Apps); } catch { }
            Text = "FlightDeck";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Theme.S(1280), Theme.S(760));
            MinimumSize = new Size(Theme.S(1260), Theme.S(660));
            DoubleBuffered = true;

            BuildUI();
            Log("Ready. Click Start Flight when you're set.");

            // ---- tray ----
            trayMenu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, Font = Theme.F10 };
            trayMenu.Opening += (s, e) => { BuildTrayMenu(); e.Cancel = false; };
            tray = new NotifyIcon { ContextMenuStrip = trayMenu, Visible = true };
            try { tray.Icon = Theme.MakeLogoIcon(SystemInformation.SmallIconSize.Width); } catch { tray.Icon = Icon; }
            tray.DoubleClick += (s, e) => RestoreFromTray();
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) RestoreFromTray(); };
            tray.BalloonTipClicked += (s, e) => RestoreFromTray();
            UpdateTrayText();

            // ---- runner ----
            Runner.Log += m => Ui(() => Log(m));
            Runner.Debug += m => Diagnostics.Write("  · " + m);
            SimLink.Debug = m => Diagnostics.Write("  · " + m);
            FlightRunner.DebugSink = m => Diagnostics.Write("  · " + m);
            Diagnostics.Write("==== FlightDeck " + Application.ProductVersion + " started (elevated: " + Util.IsElevated() + ") ====");
            Runner.Status += (a, st) => Ui(() =>
            {
                Live[a.Id] = st;
                // held back by its START setting: count it down / watch for the sim from now on
                if (a.StartMode != "now" && (st.StartsWith("Starting in") || st.StartsWith("Waiting for")) && !pendingSim.Contains(a))
                {
                    pendingSim.Add(a);
                    StartSimWatch();
                }
                Dash.RefreshStatus();
            });
            Runner.Done += (kind, cancelled, problems) => Ui(() => OnRunnerDone(kind, cancelled, problems));
            Runner.Progress += (done, total, label) => Ui(() => Dash.ShowProgress(done, total, label, Runner.Problems > 0));
            // Get out of the way before the final stacking pass, so FlightDeck itself never ends up covering anything.
            Runner.BeforeStacking += kind => Ui(() => { if (kind == "start" && Visible) { TopMost = false; TopMost = true; } });

            ShowPage(Dash);
            RefreshAll();

            // Drag & drop programs onto the window to add them.
            dropOverlay = new DropOverlay(this) { Visible = false, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            if (!Util.IsElevated()) HookDrop(this);
        }

        // =================================================================
        //  Drag & drop to add apps
        // =================================================================
        DropOverlay dropOverlay;
        static readonly string[] droppable = { ".exe", ".lnk", ".url", ".bat", ".cmd", ".appref-ms" };

        public static List<string> AddableFiles(IEnumerable<string> files)
        {
            var list = new List<string>();
            if (files != null)
                foreach (var f in files)
                    if (droppable.Contains(Path.GetExtension(f ?? "").ToLowerInvariant())) list.Add(f);
            return list;
        }

        public static List<string> AddableFiles(IDataObject d)
        {
            if (d == null || !d.GetDataPresent(DataFormats.FileDrop)) return new List<string>();
            return AddableFiles(d.GetData(DataFormats.FileDrop) as string[]);
        }

        void HookDrop(Control c)
        {
            if (c == dropOverlay) return;
            c.AllowDrop = true;
            c.DragEnter += (s, e) =>
            {
                var files = AddableFiles(e.Data);
                e.Effect = files.Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
                if (files.Count > 0) ShowDropOverlay(files.Count);
            };
            c.ControlAdded += (s, e) => HookDrop(e.Control);
            foreach (Control child in c.Controls) HookDrop(child);
        }

        public void ShowDropOverlay(int count)
        {
            dropOverlay.Count = count;
            if (dropOverlay.Parent != this) Controls.Add(dropOverlay);
            dropOverlay.Bounds = new Rectangle(Point.Empty, ClientSize);
            dropOverlay.Visible = true;
            dropOverlay.BringToFront();
            dropOverlay.Invalidate();
        }

        public void HideDropOverlay() { dropOverlay.Visible = false; }

        public void AddFromFiles(IEnumerable<string> files)
        {
            if (!Visible) RestoreFromTray();
            int added = 0;
            var skipped = new List<string>();
            foreach (var f in AddableFiles(files))
            {
                var a = AppFromFile(f);
                if (a == null) continue;
                bool dup = Apps.Any(x => (!string.IsNullOrEmpty(x.Path) && x.Path.Equals(a.Path, StringComparison.OrdinalIgnoreCase)) ||
                                         (a.Process != "" && x.Process.Equals(a.Process, StringComparison.OrdinalIgnoreCase)));
                if (dup) { skipped.Add(a.Name); continue; }
                Apps.Add(a);
                added++;
                Log(a.Name + " added — it has no position yet. Open it, place the window, then use Apps › Capture windows.");
            }
            if (skipped.Count > 0) Log("Already in \"" + Cfg.Active.Name + "\": " + string.Join(", ", skipped));
            if (added > 0) { Save(); RefreshAll(); }
        }

        static AppEntry AppFromFile(string file)
        {
            string path = file, args = "";
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".lnk")
            {
                string target, a;
                if (Util.ResolveShortcut(file, out target, out a) && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
                { path = target; args = a ?? ""; }
            }
            bool exe = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            string name = Path.GetFileNameWithoutExtension(file);
            if (exe && ext == ".exe")
                try { var fd = FileVersionInfo.GetVersionInfo(path).FileDescription; if (!string.IsNullOrWhiteSpace(fd)) name = fd.Trim(); } catch { }
            return new AppEntry { Name = name, Path = path, Args = args, Process = exe ? Path.GetFileNameWithoutExtension(path) : "" };
        }

        public void Log(string m)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "   " + m;
            logLines.Add(line);
            if (logLines.Count > 500) logLines.RemoveRange(0, 100);
            if (Dash != null) Dash.AddLine(line);
            Diagnostics.Write(m);
        }

        /// <summary>Builds sidebar + pages with the current Theme colors.</summary>
        void BuildUI()
        {
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            nav.Clear();
            content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            sidebar = new Panel { Dock = DockStyle.Left, Width = Theme.S(244), BackColor = Theme.Sidebar };
            typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(sidebar, true, null);
            sidebar.Paint += PaintSidebar;
            Controls.Add(content);
            Controls.Add(sidebar);

            Dash = new DashboardPage(this);
            AppsPg = new AppsPage(this);
            LayoutPg = new LayoutPage(this);
            SettingsPg = new SettingsPage(this);
            foreach (var p in new Page[] { Dash, AppsPg, LayoutPg, SettingsPg }) { p.Visible = false; content.Controls.Add(p); }

            AddNav("Dashboard", Theme.Glyph.Home, Dash);
            AddNav("Apps", Theme.Glyph.Apps, AppsPg);
            AddNav("Layout", Theme.Glyph.Monitor, LayoutPg);
            navSettings = AddNav("Settings", Theme.Glyph.Settings, SettingsPg);
            version = Theme.MakeLabel("v2.4.0" + (Util.IsElevated() ? "  ·  administrator" : ""), Theme.F9, Theme.Faint, Theme.Sidebar);
            sidebar.Controls.Add(version);
            sidebar.Resize += (s, e) => LayoutSidebar();
            LayoutSidebar();
        }

        /// <summary>Switches light/dark and rebuilds the window in place.</summary>
        public void SetTheme(bool light)
        {
            // deferred: the request comes from a control that is about to be replaced
            BeginInvoke(new Action(() => DoSetTheme(light)));
        }

        /// <summary>Changes the accent colour everywhere: UI, window/tray icons and desktop shortcuts.</summary>
        public void SetAccent(Color c)
        {
            BeginInvoke(new Action(() =>
            {
                Cfg.AccentColor = c.ToArgb() == Theme.DefaultAccent.ToArgb() ? null : Theme.ToHex(c);
                Theme.BaseAccent = c;
                Save();
                RebuildTheme();
                try { Icon = Theme.AppIcon() ?? Icon; } catch { }
                try { var old = tray.Icon; tray.Icon = Theme.MakeLogoIcon(SystemInformation.SmallIconSize.Width); } catch { }
                Log("Accent colour: #" + Theme.ToHex(c));
                UpdateShortcutIcons(true);
            }));
        }

        /// <summary>Redraws the icon files and re-points FlightDeck's shortcuts (desktop, Start, taskbar) at them.</summary>
        void UpdateShortcutIcons(bool report)
        {
            string key = Theme.ToHex(Theme.BaseAccent) + "|" + Application.ExecutablePath;
            new System.Threading.Thread(() =>
            {
                try
                {
                    Util.WriteShortcutIcons();
                    int n = Util.RefreshDesktopShortcuts();
                    Ui(() =>
                    {
                        Cfg.ShortcutIconsFor = key; Save();
                        if (report && n > 0) Log("Updated " + n + " FlightDeck shortcut" + (n == 1 ? "" : "s") + " (desktop, Start menu, taskbar) to the new colour.");
                    });
                }
                catch { }
            }) { IsBackground = true }.Start();
        }

        void DoSetTheme(bool light)
        {
            if (light == Cfg.LightMode && Theme.IsDark == !light) return;
            Cfg.LightMode = light;
            Save();
            RebuildTheme();
        }

        void RebuildTheme()
        {
            Theme.Apply(!Cfg.LightMode);
            SuspendLayout();
            var oldContent = content; var oldSidebar = sidebar;
            Controls.Remove(oldContent); Controls.Remove(oldSidebar);
            BuildUI();
            oldContent.Dispose(); oldSidebar.Dispose();
            ResumeLayout(true);
            Native.DarkTitleBar(Handle);
            foreach (var l in logLines) Dash.AddLine(l);
            Dash.SetBusy(Runner.Busy);
            RefreshAll();
            ShowPage(SettingsPg);
            Invalidate(true);
        }

        // =================================================================
        //  Navigation / layout
        // =================================================================
        NavButton AddNav(string text, string glyph, Page page)
        {
            var b = new NavButton(text, glyph);
            b.Click += (s, e) => ShowPage(page);
            sidebar.Controls.Add(b);
            nav.Add(new KeyValuePair<NavButton, Page>(b, page));
            return b;
        }

        void LayoutSidebar()
        {
            int w = sidebar.Width;
            int y = Theme.S(108);
            nav[0].Key.SetBounds(0, y + Theme.S(24), w, Theme.S(42));                  // FLIGHT: Dashboard
            nav[1].Key.SetBounds(0, y + Theme.S(24 + 42 + 40), w, Theme.S(42));        // SETUP: Apps
            nav[2].Key.SetBounds(0, y + Theme.S(24 + 42 + 40 + 44), w, Theme.S(42));   //        Layout
            navSettings.SetBounds(0, sidebar.Height - Theme.S(108), w, Theme.S(42));
            version.Location = new Point(Theme.S(26), sidebar.Height - Theme.S(48));
            sidebar.Invalidate();
        }

        void PaintSidebar(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int s = Theme.S(42);
            Theme.DrawLogo(g, new RectangleF(Theme.S(24), Theme.S(27), s, s));
            int bx = Theme.S(24) + s + Theme.S(12);
            using (var tag = new Font("Segoe UI", 8.5f))
            {
                const string tagline = "Your add-ons in one click";
                var tsz = TextRenderer.MeasureText(g, tagline, tag, Size.Empty, TextFormatFlags.NoPadding);
                // wordmark sits on top of the tagline and spans exactly the same width
                Theme.DrawBrand(g, "FlightDeck", new RectangleF(bx, Theme.S(27), tsz.Width, Theme.S(27)), Theme.Text, tsz.Width);
                TextRenderer.DrawText(g, tagline, tag, new Point(bx, Theme.S(55)), Theme.Muted, TextFormatFlags.NoPadding);
            }
            int y = Theme.S(108);
            TextRenderer.DrawText(g, "FLIGHT", Theme.F8, new Point(Theme.S(26), y), Theme.Faint, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "SETUP", Theme.F8, new Point(Theme.S(26), y + Theme.S(24 + 42 + 16)), Theme.Faint, TextFormatFlags.NoPadding);
            using (var pen = new Pen(Theme.SidebarLine)) g.DrawLine(pen, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
        }

        public void ShowPage(Page p)
        {
            foreach (var kv in nav) { kv.Key.Selected = kv.Value == p; kv.Value.Visible = kv.Value == p; }
            p.OnShown();
        }

        public void Ui(Action a)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(a); } catch { } } else a();
        }

        public void Save() { ConfigStore.Save(Cfg); }

        public void RefreshAll()
        {
            AppIcons.Clear();
            LayoutPg.Map.Apps = Apps;
            Dash.Rebuild();
            AppsPg.Rebuild();
            LayoutPg.Map.Invalidate();
            UpdateTrayText();
        }

        // =================================================================
        //  Flight
        // =================================================================
        public void StartFlight()
        {
            if (Runner.Busy) return;
            if (!Visible) RestoreFromTray();

            // Apps that run as administrator can only be moved by an elevated FlightDeck.
            if (!Util.IsElevated() && !Cfg.SkipAdminPrompt)
            {
                var adminApps = Apps.Where(a => a.Enabled && a.HasPos && (a.Admin || RunsElevated(a))).Select(a => a.Name).ToList();
                if (adminApps.Count > 0)
                {
                    using (var d = new AdminNeededDialog(adminApps))
                    {
                        var r = d.ShowDialog(this);
                        if (d.DontAskAgain) { Cfg.SkipAdminPrompt = true; Save(); }
                        if (r == DialogResult.Cancel) return;
                        if (r == DialogResult.Yes) { RelaunchElevated("start"); return; }
                    }
                }
            }

            StopSimWatch();
            flightClock = DateTime.Now;
            flightProblems = 0;
            Live.Clear();
            InFlight = true;
            flightProfile = Cfg.Active;
            runKind = "start";
            TopMost = true;          // stay in front while the apps open and move behind FlightDeck
            Dash.SetBusy(true);
            ShowPage(Dash);
            UpdateTrayText();
            Dash.ShowProgress(0, 1, "Getting ready…", false);   // visible right away, before the runner's first step
            Runner.StartFlight(flightProfile.Apps);
        }

        static bool RunsElevated(AppEntry a)
        {
            return Engine.Pids(a).Any(pid => Diagnostics.Elevated(pid) != "no");
        }

        /// <summary>Restarts FlightDeck as administrator and (optionally) continues with "start"/"end".</summary>
        public void RelaunchElevated(string command)
        {
            Cfg.RunAsAdmin = true;
            Save();
            Program.ReleaseSingleInstance();
            if (Elevation.Relaunch(Cfg, command))
            {
                Diagnostics.Write("Restarting as administrator" + (string.IsNullOrEmpty(command) ? "" : " (then: " + command + ")"));
                exiting = true;
                handedOver = true;      // the new instance owns the config now: don't overwrite it on close
                tray.Visible = false;
                Close();
            }
            else
            {
                Cfg.RunAsAdmin = false;
                Save();
                Log("Administrator prompt was cancelled — FlightDeck keeps running as a normal user.");
            }
        }

        public void EndFlight()
        {
            if (Runner.Busy) { Runner.Cancel(); for (int i = 0; i < 30 && Runner.Busy; i++) System.Threading.Thread.Sleep(100); }
            if (Runner.Busy) return;
            StopSimWatch();
            Live.Clear();
            runKind = "end";
            Dash.SetBusy(true);
            ShowPage(Dash);
            Dash.ShowProgress(0, 1, "Getting ready…", false);
            Runner.EndFlight((flightProfile ?? Cfg.Active).Apps);
        }

        public void ApplyLayout()
        {
            if (Runner.Busy) return;
            runKind = "apply";
            Dash.SetBusy(true);
            Dash.ShowProgress(0, 1, "Looking for your windows…", false);
            Runner.StartFlight((InFlight && flightProfile != null ? flightProfile : Cfg.Active).Apps, true);
        }

        void OnRunnerDone(string kind, bool cancelled, int n)
        {
            if (!Runner.Busy) { Dash.SetBusy(false); runKind = ""; }

            if (kind == "start" && !cancelled)
            {
                flightProblems += n;
                if (pendingSim.Count > 0 || Runner.Busy)
                {
                    // more apps open later: stay on screen with the countdown; the celebration comes at the very end
                    TopMost = false;
                    Dash.ShowProgress(1, 1, "Waiting to open " + Names(pendingSim) + "…", false);
                }
                else FinishStart();
            }
            else if (kind == "start") TopMost = false;
            else if (kind == "later" && !cancelled)
            {
                flightProblems += n;
                if (pendingSim.Count == 0 && !Runner.Busy) FinishStart();
            }
            else if (kind == "end")
            {
                int closed = (flightProfile ?? Cfg.Active).Apps.Count(a => a.Enabled && a.CloseOnEnd);
                InFlight = false;
                flightProfile = null;
                RestoreFromTray();
                UpdateTrayText();
                if (n == 0 && !cancelled) Celebrate(closed, () => { }, true);
            }

            var t = new Timer { Interval = 6000 };
            t.Tick += (s, e) =>
            {
                t.Stop(); t.Dispose();
                if (Runner.Busy) return;
                Live.Clear();
                ShowPending();
                Dash.RefreshStatus();
            };
            t.Start();
        }

        CelebrationOverlay celebration;

        // ---------- apps that open later (START column) ----------
        List<AppEntry> pendingSim = new List<AppEntry>();   // waiting to be opened
        DateTime flightClock;                               // when Start Flight was clicked ("After N s")
        Timer simWatch;

        static string Names(List<AppEntry> apps)
        {
            var n = apps.Select(a => a.Name).ToList();
            return n.Count <= 1 ? string.Join("", n) : string.Join(", ", n.Take(n.Count - 1)) + " and " + n.Last();
        }

        void StartSimWatch()
        {
            if (simWatch == null)
            {
                simWatch = new Timer { Interval = 1000 };
                simWatch.Tick += (s, e) => CheckSim();
            }
            if (pendingSim.Any(a => a.StartMode == "flight")) SimLink.Start();
            simWatch.Start();
            ShowPending();
            UpdateTrayText();
        }

        void StopSimWatch()
        {
            if (simWatch != null) simWatch.Stop();
            pendingSim.Clear();
            SimLink.Stop();
            UpdateTrayText();
        }

        bool Due(AppEntry a)
        {
            switch (a.StartMode)
            {
                case "delay": return (DateTime.Now - flightClock).TotalSeconds >= a.StartDelay;
                case "sim": return Native.SimConnectReady();
                case "flight": return SimLink.FlightLoaded;
                default: return true;
            }
        }

        /// <summary>Tile status for the apps still waiting ("Starts in 40 s", "Waiting for flight"…).</summary>
        void ShowPending()
        {
            foreach (var a in pendingSim)
            {
                if (a.StartMode == "delay")
                {
                    int left = (int)Math.Ceiling(a.StartDelay - (DateTime.Now - flightClock).TotalSeconds);
                    Live[a.Id] = left > 0 ? "Starting in " + (left >= 120 ? (left / 60) + " min" : left + " s") : "Starting";
                }
                else Live[a.Id] = FlightRunner.WaitStatus(a);
            }
            Dash.RefreshStatus();   // cheap: no process lookups here
        }

        void CheckSim()
        {
            if (!InFlight || pendingSim.Count == 0) { StopSimWatch(); return; }
            ShowPending();
            if (Runner.Busy) return;
            var ready = pendingSim.Where(Due).ToList();
            if (ready.Count == 0) return;
            foreach (var a in ready) pendingSim.Remove(a);
            if (!pendingSim.Any(a => a.StartMode == "flight")) SimLink.Stop();
            if (pendingSim.Count == 0) simWatch.Stop();
            UpdateTrayText();

            string why = ready.All(a => a.StartMode == "flight") ? "Your flight has loaded"
                       : ready.All(a => a.StartMode == "sim") ? "The simulator is ready"
                       : ready.All(a => a.StartMode == "delay") ? "Time's up"
                       : "Ready";
            Log(why + " — opening " + Names(ready) + "…");
            runKind = "later";
            Dash.SetBusy(true);
            Dash.ShowProgress(0, 1, "Opening " + Names(ready) + "…", false);
            Runner.StartDeferred(ready, (flightProfile ?? Cfg.Active).Apps);
        }

        /// <summary>
        /// Every app of the flight is open and in place (the ones with a START delay too):
        /// the "All set" celebration, then off to the tray. If something went wrong, stay on screen instead.
        /// </summary>
        void FinishStart()
        {
            if (!InFlight) return;
            int count = (flightProfile ?? Cfg.Active).Apps.Count(a => a.Enabled);
            const string text = "Your apps are in position. Right-click the FlightDeck icon to end the flight.";
            if (flightProblems == 0)
            {
                if (!Visible) RestoreFromTray();
                TopMost = true;    // a moment in front, even over the simulator, so it's clear everything is ready
                Celebrate(count, () => { TopMost = false; GoToTray("Flight ready ✈", text); });
            }
            else
            {
                TopMost = false;
                RestoreFromTray();
                Toast.Show("Flight started — check " + flightProblems + " app" + (flightProblems == 1 ? "" : "s"), "See the Activity log in FlightDeck for what went wrong.", RestoreFromTray);
            }
        }

        int flightProblems;

        void Celebrate(int apps, Action then, bool landing = false)
        {
            if (celebration != null) { celebration.Dispose(); celebration = null; }
            celebration = new CelebrationOverlay(apps, landing);
            celebration.Bounds = new Rectangle(Point.Empty, ClientSize);
            Controls.Add(celebration);
            celebration.BringToFront();
            celebration.Finished += () =>
            {
                then();
                if (celebration != null) { Controls.Remove(celebration); celebration.Dispose(); celebration = null; }
            };
            celebration.Start();
        }

        // =================================================================
        //  Tray
        // =================================================================
        void GoToTray(string title, string text)
        {
            Hide();
            UpdateTrayText();
            try { Toast.Show(title, text, RestoreFromTray); } catch { }
        }

        bool minimizeHintShown;

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Settings → Minimize to tray: minimizing goes next to the clock instead of the taskbar
            if (WindowState == FormWindowState.Minimized && Visible && tray != null && Cfg != null && !Cfg.KeepOnTaskbar)
            {
                Hide();
                UpdateTrayText();
                if (!minimizeHintShown)
                {
                    minimizeHintShown = true;
                    try { Toast.Show("FlightDeck is in the tray", "Click its icon next to the clock to open it again. You can change this in Settings.", RestoreFromTray); } catch { }
                }
            }
        }

        public void RestoreFromTray()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            Native.SetForegroundWindow(Handle);
        }

        void UpdateTrayText()
        {
            if (tray == null) return;
            string t = "FlightDeck · " + (InFlight ? (pendingSim != null && pendingSim.Count > 0 ? "more apps open later · " : "in flight · ") : "") + Cfg.Active.Name;
            tray.Text = t.Length > 63 ? t.Substring(0, 62) + "…" : t;
        }

        void BuildTrayMenu()
        {
            var it = trayMenu.Items;
            it.Clear();
            it.Add(new ToolStripMenuItem("Open FlightDeck", null, (s, e) => RestoreFromTray()) { Font = Theme.F10S });
            if (InFlight || (Runner.Busy && runKind == "start"))
                it.Add(new ToolStripMenuItem("End Flight", null, (s, e) => EndFlight()) { Enabled = runKind != "end" });
            else
                it.Add(new ToolStripMenuItem("Start Flight", null, (s, e) => StartFlight()) { Enabled = !Runner.Busy });
            it.Add(new ToolStripSeparator());
            it.Add(new ToolStripMenuItem("Exit FlightDeck", null, (s, e) => ExitApp()));
        }

        void ExitApp()
        {
            exiting = true;
            Close();
        }

        // =================================================================
        //  Profiles
        // =================================================================
        public void FillProfileMenu(ToolStripItemCollection items)
        {
            items.Clear();
            foreach (var p in Cfg.Profiles)
            {
                var pp = p;
                items.Add(new ToolStripMenuItem(p.Name, null, (s, e) => SwitchProfile(pp))
                {
                    Checked = p == Cfg.Active,
                    Enabled = !InFlight || p == Cfg.Active,
                    ToolTipText = InFlight && p != Cfg.Active ? "End the flight to switch profiles" : null
                });
            }
            items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem("New profile…", null, (s, e) => NewProfile()) { Enabled = !InFlight });
            items.Add(new ToolStripMenuItem("Duplicate \"" + Cfg.Active.Name + "\"…", null, (s, e) => DuplicateProfile()) { Enabled = !InFlight });
            items.Add(new ToolStripMenuItem("Rename \"" + Cfg.Active.Name + "\"…", null, (s, e) => RenameProfile()));
            items.Add(new ToolStripMenuItem("Delete \"" + Cfg.Active.Name + "\"", null, (s, e) => DeleteProfile()) { Enabled = Cfg.Profiles.Count > 1 && !InFlight });
        }

        public void SwitchProfile(Profile p)
        {
            if (p == Cfg.Active) return;
            if (InFlight) { MessageBox.Show(this, "End the current flight before switching profiles.", "FlightDeck"); return; }
            Cfg.ActiveProfile = p.Id;
            Live.Clear();
            Save();
            RefreshAll();
            Log("Profile: " + p.Name);
        }

        string UniqueName(string baseName)
        {
            string n = baseName; int i = 2;
            while (Cfg.Profiles.Any(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase))) n = baseName + " " + i++;
            return n;
        }

        void NewProfile()
        {
            string name = PromptDialog.Ask(this, "New profile", "Name for the new (empty) profile:", UniqueName("New profile"));
            if (name == null) return;
            var p = new Profile { Name = UniqueName(name) };
            Cfg.Profiles.Add(p);
            SwitchProfile(p);
            ShowPage(AppsPg);
        }

        void DuplicateProfile()
        {
            string name = PromptDialog.Ask(this, "Duplicate profile", "Name for the copy of \"" + Cfg.Active.Name + "\":", UniqueName(Cfg.Active.Name + " copy"));
            if (name == null) return;
            var p = Cfg.Active.CloneAs(UniqueName(name));
            Cfg.Profiles.Insert(Cfg.Profiles.IndexOf(Cfg.Active) + 1, p);
            SwitchProfile(p);
        }

        void RenameProfile()
        {
            var cur = Cfg.Active;
            string name = PromptDialog.Ask(this, "Rename profile", "New name for \"" + cur.Name + "\":", cur.Name);
            if (name == null || name == cur.Name) return;
            cur.Name = Cfg.Profiles.Any(p => p != cur && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ? UniqueName(name) : name;
            Save();
            RefreshAll();
        }

        void DeleteProfile()
        {
            var cur = Cfg.Active;
            if (Cfg.Profiles.Count <= 1) return;
            if (MessageBox.Show(this, "Delete the profile \"" + cur.Name + "\" and its " + cur.Apps.Count + " app(s)?\nThe programs themselves are not touched.",
                "FlightDeck", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            int i = Cfg.Profiles.IndexOf(cur);
            Cfg.Profiles.Remove(cur);
            Cfg.ActiveProfile = Cfg.Profiles[Math.Max(0, i - 1)].Id;
            Live.Clear();
            Save();
            RefreshAll();
        }

        // =================================================================
        //  Apps
        // =================================================================
        public void EditApp(AppEntry a)
        {
            bool isNew = !Apps.Contains(a);
            using (var d = new EditAppDialog(a.Clone(), isNew))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                a.CopyFrom(d.App);
                if (isNew) Apps.Add(a);
                Save();
                RefreshAll();
            }
        }

        public void DeleteApp(AppEntry a)
        {
            if (MessageBox.Show(this, "Remove \"" + a.Name + "\" from the profile \"" + Cfg.Active.Name + "\"?", "FlightDeck", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Apps.Remove(a);
            Save();
            RefreshAll();
        }

        public void CaptureWindows()
        {
            using (var d = new CaptureDialog(Apps))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                int added = 0, updated = 0;
                foreach (var w in d.Selected)
                {
                    var existing = Apps.FirstOrDefault(a => string.Equals(a.Process, w.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrWhiteSpace(a.Title) || w.Title.IndexOf(a.Title, StringComparison.OrdinalIgnoreCase) >= 0));
                    if (existing != null) { existing.SetPlacement(w); updated++; continue; }

                    string path = Native.GetProcessPath(w.Pid), name = w.ProcessName;
                    try { if (path != "") { var fd = FileVersionInfo.GetVersionInfo(path).FileDescription; if (!string.IsNullOrWhiteSpace(fd)) name = fd.Trim(); } } catch { }
                    var app = new AppEntry { Name = name, Path = path, Process = w.ProcessName };
                    app.SetPlacement(w);
                    Apps.Add(app);
                    added++;
                }
                Util.TidyEdges(Apps);
                Save();
                RefreshAll();
                Log("Captured windows into \"" + Cfg.Active.Name + "\": " + added + " added, " + updated + " updated.");
            }
        }

        /// <summary>Moves and resizes one open app to its saved position (Dashboard "Position" button).</summary>
        public void PositionApp(AppEntry a)
        {
            if (!a.HasPos)
            {
                Toast.Show("No saved position", a.Name + " has no position yet. Place its window and click \"Grab position\" on the Apps page.", null);
                ShowPage(AppsPg);
                return;
            }
            Live[a.Id] = "Positioning"; Dash.RefreshStatus();
            // all of this can take a few seconds (tray apps): keep the window responsive
            new System.Threading.Thread(() =>
            {
                var w = Engine.FindWindow(a);
                bool fromTray = false;
                if (w == null && Engine.IsRunning(a))
                {
                    if (Engine.RevealFromTray(a) == null)
                    {
                        var pids = Engine.Pids(a);
                        Action<string> dbg = m => Diagnostics.Write("  · " + m);
                        if (Native.PokeTrayIcon(pids, false, dbg) > 0) System.Threading.Thread.Sleep(1500);
                        if (Engine.FindWindow(a) == null && Native.PokeTrayIcon(pids, true, dbg) > 0) System.Threading.Thread.Sleep(1500);
                        if (Engine.FindWindow(a) == null && Tray.ClickIcon(a, dbg) != null) System.Threading.Thread.Sleep(1200);
                    }
                    System.Threading.Thread.Sleep(300);
                    w = Engine.FindWindow(a);
                    fromTray = w != null;
                }
                if (w == null)
                {
                    Ui(() =>
                    {
                        Live.Remove(a.Id); Dash.RefreshStatus();
                        Toast.Show(a.Name + " isn't open", "Open it first, then click Position again.", null);
                    });
                    return;
                }
                bool ok = Native.Place(w.Handle, a.X, a.Y, a.W, a.H, a.Maximized);
                if (ok)
                {
                    System.Threading.Thread.Sleep(1200);          // second pass for apps that nudge themselves
                    var w2 = Engine.FindWindow(a);
                    if (w2 != null) Native.Place(w2.Handle, a.X, a.Y, a.W, a.H, a.Maximized);
                }
                Ui(() =>
                {
                    Live.Remove(a.Id); Dash.RefreshStatus();
                    if (fromTray) Log(a.Name + " was waiting in the tray — opened its window.");
                    Log(ok ? a.Name + ": moved to its saved position." :
                        a.Name + ": Windows refused to move it — it probably runs as administrator. Turn on \"Run FlightDeck as administrator\" in Settings.");
                });
            }) { IsBackground = true }.Start();
        }

        /// <summary>Saves the position of the app's open window; if it isn't open, explains what to do.</summary>
        public void GrabPosition(AppEntry a)
        {
            if (string.IsNullOrWhiteSpace(a.Process))
            {
                MessageBox.Show(this, "FlightDeck doesn't know which window belongs to \"" + a.Name + "\" yet.\n\nSet its Process name (as shown in Task Manager) and try again.",
                    "Grab position", MessageBoxButtons.OK, MessageBoxIcon.Information);
                EditApp(a);
                return;
            }
            WinInfo w = Engine.FindWindow(a);
            if (w == null || w.Minimized)
            {
                using (var d = new GrabGuideDialog(a))
                    if (d.ShowDialog(this) == DialogResult.Yes)
                    {
                        try { Engine.Launch(a, Util.IsElevated()); Log("Opening " + a.Name + " — place its window, then click Grab position."); }
                        catch (Exception ex) { Log(a.Name + ": could not start (" + ex.Message + ")."); }
                    }
                return;
            }
            a.SetPlacement(w);
            Util.TidyEdges(Apps);
            Save();
            RefreshAll();
            Overlay.Flash(a);
            Log(a.Name + ": position saved (" + Util.Placement(a) + ").");
        }

        public int SaveCurrentPositions(out List<string> missing)
        {
            missing = new List<string>();
            var all = Native.ListWindows();
            int n = 0;
            foreach (var a in Apps)
            {
                var w = Engine.FindWindow(a, all);
                if (w != null && !w.Minimized) { a.SetPlacement(w); n++; } else missing.Add(a.Name);
            }
            Util.TidyEdges(Apps);
            Save();
            RefreshAll();
            return n;
        }

        // =================================================================
        //  Window behaviour
        // =================================================================
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0233 /*WM_DROPFILES*/)
            {
                AddFromFiles(Native.DroppedFiles(m.WParam));
                return;
            }
            if (CmdMessage != 0 && m.Msg == (int)CmdMessage)
            {
                int cmd = m.WParam.ToInt32();
                if (cmd == 1) StartFlight();
                else if (cmd == 2) EndFlight();
                else RestoreFromTray();
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // let a non-elevated FlightDeck/shortcut talk to an elevated one
            try { Native.ChangeWindowMessageFilterEx(Handle, CmdMessage, 1, IntPtr.Zero); } catch { }
            if (Util.IsElevated())
            {
                // Explorer can't drag into an elevated window the normal way; accept the classic file drop instead.
                try
                {
                    foreach (uint msg in new uint[] { 0x0233, 0x004A, 0x0049 }) Native.ChangeWindowMessageFilterEx(Handle, msg, 1, IntPtr.Zero);
                    Native.DragAcceptFiles(Handle, true);
                }
                catch { }
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (Cfg.AccentColor != null && Cfg.ShortcutIconsFor != Theme.ToHex(Theme.BaseAccent) + "|" + Application.ExecutablePath)
                UpdateShortcutIcons(false);   // e.g. pinned to Start/taskbar after the colour was chosen, or the exe moved
            string ver = Application.ProductVersion;
            if (Cfg.IconRefreshVersion != ver)
            {
                Cfg.IconRefreshVersion = ver; Save();
                // Windows keeps showing the old (yellow) icon from its cache; ask it to reload ours.
                new System.Threading.Thread(() =>
                {
                    try
                    {
                        var files = new List<string> { Application.ExecutablePath };
                        foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                                                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) })
                            if (Directory.Exists(dir)) files.AddRange(Directory.GetFiles(dir, "*.lnk"));
                        Native.RefreshShellIcons(files);
                        var ie = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ie4uinit.exe");
                        if (File.Exists(ie))
                            using (var p = Process.Start(new ProcessStartInfo(ie, "-show") { UseShellExecute = false, CreateNoWindow = true })) { }
                    }
                    catch { }
                }) { IsBackground = true }.Start();
            }
            if (startArgs.Contains("--start", StringComparer.OrdinalIgnoreCase)) StartFlight();
            else if (startArgs.Contains("--end", StringComparer.OrdinalIgnoreCase)) EndFlight();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // During a flight the X button sends FlightDeck to the tray instead of closing it.
            if (!exiting && InFlight && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                bool first = !Cfg.TrayHintShown;
                Cfg.TrayHintShown = true; Save();
                GoToTray("FlightDeck is still here", first
                    ? "You're in a flight, so FlightDeck moved to the tray. Right-click the icon to end the flight or exit."
                    : "Right-click the icon to end the flight.");
                return;
            }
            Runner.Cancel();
            if (!handedOver) Save();
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { tray.Dispose(); trayMenu.Dispose(); } catch { } }
            base.Dispose(disposing);
        }
    }

    /// <summary>Small dark text prompt.</summary>
    class PromptDialog : DarkForm
    {
        readonly TextField field;

        PromptDialog(string title, string label, string value)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            int pad = Theme.S(24), w = Theme.S(440);
            ClientSize = new Size(w, Theme.S(176));

            var l = Theme.MakeLabel(label, Theme.F10, Theme.SubText, Theme.Bg);
            l.Location = new Point(pad, Theme.S(22));
            field = new TextField(value) { Location = new Point(pad, Theme.S(52)), Width = w - 2 * pad };
            var ok = new FlatButton("OK", null, BtnStyle.Primary, 38) { Width = Theme.S(96) };
            var cancel = new FlatButton("Cancel", null, BtnStyle.Normal, 38);
            ok.Location = new Point(w - pad - ok.Width, Theme.S(112));
            cancel.Location = new Point(ok.Left - Theme.S(10) - cancel.Width, ok.Top);
            ok.Click += (s, e) => { if (field.Value.Trim() != "") { DialogResult = DialogResult.OK; Close(); } };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            field.Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (field.Value.Trim() != "") { DialogResult = DialogResult.OK; Close(); } }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; DialogResult = DialogResult.Cancel; Close(); }
            };
            Controls.AddRange(new Control[] { l, field, ok, cancel });
            Shown += (s, e) => { field.Box.Focus(); field.Box.SelectAll(); };
        }

        public static string Ask(IWin32Window owner, string title, string label, string value)
        {
            using (var d = new PromptDialog(title, label, value))
                return d.ShowDialog(owner) == DialogResult.OK ? d.field.Value.Trim() : null;
        }
    }
}
