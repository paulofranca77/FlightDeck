using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FlightDeck
{
    class Page : Panel
    {
        protected readonly MainForm Host;
        readonly Label title, subtitle;
        readonly List<Control> actions = new List<Control>();
        protected int HeaderHeight { get { return Theme.S(122); } }

        public Page(MainForm host, string t, string sub)
        {
            Host = host;
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            title = Theme.MakeLabel(t, Theme.Title, Theme.Text, Theme.Bg);
            subtitle = Theme.MakeLabel(sub, Theme.F10, Theme.Muted, Theme.Bg);
            title.Location = new Point(Theme.S(34), Theme.S(26));
            subtitle.Location = new Point(Theme.S(36), Theme.S(66));
            subtitle.AutoSize = false;
            subtitle.AutoEllipsis = true;
            subtitle.Height = Theme.S(22);
            title.Visible = t != "";
            subtitle.Visible = sub != "";
            Controls.Add(title);
            Controls.Add(subtitle);
        }

        protected void AddAction(Control c) { actions.Add(c); Controls.Add(c); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int x = ClientSize.Width - Theme.S(34);
            for (int i = actions.Count - 1; i >= 0; i--)
            {
                x -= actions[i].Width;
                actions[i].Location = new Point(x, Theme.S(34));
                x -= Theme.S(10);
            }
            subtitle.Width = Math.Max(0, x - subtitle.Left - Theme.S(12));
            if (ClientSize.Width < Theme.S(400) || ClientSize.Height < Theme.S(300)) return;
            LayoutBody(new Rectangle(Theme.S(34), HeaderHeight, ClientSize.Width - Theme.S(68), ClientSize.Height - HeaderHeight - Theme.S(30)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.HeaderLine)) e.Graphics.DrawLine(pen, 0, Theme.S(100), Width, Theme.S(100));
        }

        protected virtual void LayoutBody(Rectangle r) { }
        public virtual void OnShown() { PerformLayout(); }
    }

    // =====================================================================
    class DashboardPage : Page
    {
        readonly FlatButton btnStart, btnEnd, btnApply;
        readonly ProfileButton profileBtn;
        readonly Card appsCard, logCard;
        readonly Label appsTitle, appsSub, emptyLbl, logTitle, warnLbl;
        readonly ProgressStrip progress;
        readonly Timer hideProgress;
        readonly Panel tiles;
        readonly SetupGuide guide;
        readonly TextBox log;
        readonly List<AppTile> tileList = new List<AppTile>();
        readonly Timer poll;

        public DashboardPage(MainForm host) : base(host, "", "")
        {
            btnEnd = new FlatButton("End Flight", null, BtnStyle.Danger, 40) { Painter = (g, r, c) => Theme.DrawPlane(g, r, c, false) };
            btnStart = new FlatButton("Start Flight", null, BtnStyle.Primary, 40) { Painter = (g, r, c) => Theme.DrawPlane(g, r, c, true) };
            btnEnd.FitWidth(); btnStart.FitWidth();
            btnStart.Width += Theme.S(10); btnEnd.Width += Theme.S(6);
            btnStart.Click += (s, e) => Host.StartFlight();
            btnEnd.Click += (s, e) => Host.EndFlight();
            profileBtn = new ProfileButton(host);
            AddAction(profileBtn);
            AddAction(btnEnd);
            AddAction(btnStart);

            appsCard = new Card();
            appsTitle = Theme.MakeLabel("Companion apps", Theme.F11S, Theme.Text, Theme.Card);
            appsSub = Theme.MakeLabel("", Theme.F9, Theme.Muted, Theme.Card);
            emptyLbl = Theme.MakeLabel("No apps yet. Drag a program (.exe or shortcut) into this window, or open your apps and use Apps → Capture windows.", Theme.F10, Theme.Muted, Theme.Card);
            warnLbl = Theme.MakeLabel("", Theme.F9, Theme.Orange, Theme.Card);
            warnLbl.Cursor = Cursors.Hand;
            warnLbl.Click += (s, e) => Host.ShowPage(Host.AppsPg);
            btnApply = new FlatButton("Re-apply layout", Theme.Glyph.Refresh, BtnStyle.Ghost, 34);
            btnApply.Click += (s, e) => Host.ApplyLayout();
            tiles = new Panel { BackColor = Theme.Card };
            guide = new SetupGuide { Visible = false };
            guide.BtnCapture.Click += (s, e) => Host.CaptureWindows();
            guide.BtnAdd.Click += (s, e) => { Host.ShowPage(Host.AppsPg); Host.AppsPg.AddManual(); };
            emptyLbl.Visible = false;
            appsCard.Controls.AddRange(new Control[] { appsTitle, appsSub, btnApply, tiles, guide, warnLbl });

            logCard = new Card();
            logTitle = Theme.MakeLabel("Activity", Theme.F11S, Theme.Text, Theme.Card);
            log = new TextBox
            {
                Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.SubText,
                Font = new Font("Cascadia Mono", 9f), ScrollBars = ScrollBars.Vertical, WordWrap = true, TabStop = false
            };
            if (log.Font.Name != "Cascadia Mono") log.Font = new Font("Consolas", 9.5f);
            log.HandleCreated += (s, e) => Native.DarkScrollbars(log.Handle);
            progress = new ProgressStrip { Visible = false };
            logCard.Controls.AddRange(new Control[] { logTitle, progress, log });
            hideProgress = new Timer { Interval = 5000 };
            hideProgress.Tick += (s, e) => { hideProgress.Stop(); progress.Visible = false; PerformLayout(); };

            Controls.Add(appsCard);
            Controls.Add(logCard);

            poll = new Timer { Interval = 2000 };
            poll.Tick += (s, e) => { if (Visible) ScanProcesses(); };
            poll.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && poll != null) { poll.Stop(); poll.Dispose(); }
            if (disposing && hideProgress != null) { hideProgress.Stop(); hideProgress.Dispose(); }
            base.Dispose(disposing);
        }

        public void SetBusy(bool busy)
        {
            btnStart.Enabled = !busy;
            btnApply.Enabled = !busy;
        }

        /// <summary>Progress of Start Flight / End Flight / Apply layout, shown under "Activity".</summary>
        public void ShowProgress(int done, int total, string label, bool problem)
        {
            hideProgress.Stop();
            if (!progress.Visible) { progress.Reset(); progress.Visible = true; PerformLayout(); }
            progress.Set(done, total, label, problem);
            progress.Update();      // paint now, even while the window is busy with other messages
            if (total > 0 && done >= total) hideProgress.Start();   // keep the full bar a moment, then tidy up
        }

        // Which programs are running, read on a background thread so the window never waits on it.
        HashSet<string> running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        volatile bool scanning;
        void ScanProcesses()
        {
            if (scanning) return;
            scanning = true;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try { foreach (var p in System.Diagnostics.Process.GetProcesses()) { try { set.Add(p.ProcessName); } catch { } p.Dispose(); } }
                catch { }
                try { BeginInvoke((Action)(() => { running = set; scanning = false; RefreshStatus(); })); }
                catch { scanning = false; }
            });
        }

        public void AddLine(string line)
        {
            if (log.TextLength > 60000) log.Clear();
            log.AppendText((log.TextLength > 0 ? Environment.NewLine : "") + line);
        }

        public void Rebuild()
        {
            profileBtn.RefreshText();
            tiles.SuspendLayout();
            foreach (var t in tileList) t.Dispose();
            tiles.Controls.Clear();
            tileList.Clear();
            foreach (var a in Host.Apps)
            {
                var t = new AppTile(a);
                var app = a;
                t.Click += (s, e) => { if (t.DownOnPosition) Host.PositionApp(app); else Host.EditApp(app); };
                tileList.Add(t);
                tiles.Controls.Add(t);
            }
            tiles.ResumeLayout();
            int on = Host.Apps.Count(a => a.Enabled);
            appsSub.Text = Host.Apps.Count == 0 ? "" : on + " of " + Host.Apps.Count + " start with your flight";
            guide.Visible = Host.Apps.Count == 0;
            btnApply.Visible = Host.Apps.Count > 0;
            int noPos = Host.Apps.Count(a => a.Enabled && !a.HasPos);
            warnLbl.Text = noPos == 0 ? "" : "⚠  " + noPos + (noPos == 1 ? " app has" : " apps have") +
                " no position yet — click here, then \"Grab position\" on the Apps page.";
            warnLbl.Visible = noPos > 0;
            RefreshStatus();
            PerformLayout();
        }

        bool IsRunningCached(AppEntry a)
        {
            if (string.IsNullOrWhiteSpace(a.Process)) return false;
            string n = a.Process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? a.Process.Substring(0, a.Process.Length - 4) : a.Process;
            return running.Contains(n);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ScanProcesses(); }

        public void RefreshStatus()
        {
            foreach (var t in tileList)
            {
                string st;
                if (!Host.Live.TryGetValue(t.App.Id, out st))
                    st = !t.App.HasPos ? "No position yet" : IsRunningCached(t.App) ? "Running" : "Not running";
                t.SetStatus(st);
            }
        }

        protected override void LayoutBody(Rectangle r)
        {
            if (appsCard == null || logCard == null || log == null || tiles == null) return;
            int pad = Theme.S(20);
            int innerW = r.Width - 2 * pad;
            int warnH = warnLbl.Visible ? Theme.S(30) : 0;

            // Every app must stay visible: the apps card may take space from Activity (which keeps
            // at least a small strip), and tiles shrink step by step until they all fit.
            int n = tileList.Count;
            int maxTilesH = Math.Max(Theme.S(44), r.Height - Theme.S(60) - warnH - Theme.S(20) - Theme.S(16) - Theme.S(100));
            int gap = Theme.S(12), tw = Theme.S(252), th = Theme.S(70), cols = 1, rows = 1;
            for (float k = 1f; ; k -= 0.05f)
            {
                gap = Math.Max(Theme.S(6), (int)(Theme.S(12) * k));
                tw = Math.Max(Theme.S(190), (int)(Theme.S(300) * k));
                th = Math.Max(Theme.S(38), (int)(Theme.S(70) * k));
                cols = Math.Max(1, (innerW + gap) / (tw + gap));
                rows = Math.Max(1, (int)Math.Ceiling(n / (double)cols));
                if (rows * th + (rows - 1) * gap <= maxTilesH || k <= 0.45f) break;
            }
            // spread the tiles over the full width of the card
            if (n > 0)
            {
                int used = Math.Min(cols, n);
                int fill = (innerW - (cols - 1) * gap) / cols;
                tw = Math.Min(Theme.S(360), Math.Max(tw, fill));
                if (used < cols) tw = Math.Min(tw, Theme.S(320));
            }
            int tilesH = n == 0 ? 0 : rows * th + (rows - 1) * gap;
            int guideH = n == 0 ? guide.HeightFor(innerW) : 0;
            int cardH = n == 0 ? Theme.S(62) + guideH + Theme.S(18) : Theme.S(60) + warnH + tilesH + Theme.S(20);

            appsCard.SetBounds(r.X, r.Y, r.Width, cardH);
            appsTitle.Location = new Point(pad, Theme.S(18));
            appsSub.Location = new Point(pad + appsTitle.Width + Theme.S(10), Theme.S(22));
            btnApply.Location = new Point(r.Width - pad - btnApply.Width + Theme.S(8), Theme.S(12));
            warnLbl.Location = new Point(pad, Theme.S(54));
            tiles.SetBounds(pad, Theme.S(60) + warnH, innerW, Math.Max(1, tilesH));
            for (int i = 0; i < n; i++)
                tileList[i].SetBounds((i % cols) * (tw + gap), (i / cols) * (th + gap), tw, th);
            if (n == 0) guide.SetBounds(pad, Theme.S(62), innerW, guideH);

            int ly = r.Y + cardH + Theme.S(16);
            logCard.SetBounds(r.X, ly, r.Width, Math.Max(Theme.S(100), r.Bottom - ly));
            logTitle.Location = new Point(pad, Theme.S(18));
            int logTop = Theme.S(54);
            if (progress.Visible)
            {
                progress.SetBounds(pad, Theme.S(50), r.Width - 2 * pad, Theme.S(30));
                logTop = Theme.S(92);
            }
            log.SetBounds(pad, logTop, r.Width - 2 * pad, logCard.Height - logTop - Theme.S(14));
        }
    }

    // =====================================================================
    class AppsPage : Page
    {
        readonly Card card;
        readonly TableHeader header;
        readonly Panel rows;
        readonly SetupGuide empty;
        readonly List<AppRow> rowList = new List<AppRow>();
        readonly ProfileButton profileBtn;

        // drag & drop (stacking order)
        AppRow dragRow; int dragStartY, dropIndex = -1; bool dragging;
        Panel dropLine;

        public AppsPage(MainForm host) : base(host, "Apps", "Drag the rows to set the stacking order — the app at the top stays in front.")
        {
            var cap = new FlatButton("Capture windows", Theme.Glyph.Windows, BtnStyle.Normal, 40);
            var add = new FlatButton("Add app", Theme.Glyph.Add, BtnStyle.Primary, 40);
            cap.Click += (s, e) => Host.CaptureWindows();
            profileBtn = new ProfileButton(host);
            AddAction(profileBtn);
            add.Click += (s, e) => AddManual();
            AddAction(cap);
            AddAction(add);

            card = new Card();
            header = new TableHeader { BackColor = Theme.Card };
            rows = new Panel { BackColor = Theme.Card, AutoScroll = true };
            rows.HandleCreated += (s, e) => Native.DarkScrollbars(rows.Handle);
            rows.Resize += (s, e) => LayoutRows();
            empty = new SetupGuide();
            empty.BtnCapture.Click += (s, e) => Host.CaptureWindows();
            empty.BtnAdd.Click += (s, e) => AddManual();
            dropLine = new Panel { BackColor = Theme.Accent, Visible = false, Height = Theme.S(3) };
            rows.Controls.Add(dropLine);
            card.Controls.AddRange(new Control[] { header, rows, empty });
            Controls.Add(card);
        }

        public void AddManual()
        {
            using (var ofd = new OpenFileDialog { Filter = "Programs and shortcuts (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|All files|*.*", Title = "Choose a program" })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                string baseName = Path.GetFileNameWithoutExtension(ofd.FileName), name = baseName;
                try { var fd = FileVersionInfo.GetVersionInfo(ofd.FileName).FileDescription; if (!string.IsNullOrWhiteSpace(fd)) name = fd.Trim(); } catch { }
                var a = new AppEntry { Name = name, Path = ofd.FileName, Process = ofd.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? baseName : "" };
                Host.EditApp(a);
            }
        }

        public void Rebuild()
        {
            profileBtn.RefreshText();
            rows.SuspendLayout();
            foreach (var r in rowList) { rows.Controls.Remove(r); r.Dispose(); }
            rowList.Clear();
            dragRow = null; dragging = false; dropLine.Visible = false;
            for (int i = 0; i < Host.Apps.Count; i++)
            {
                var a = Host.Apps[i];
                var row = new AppRow(a, i + 1);
                row.Click += (s, e) =>
                {
                    if (dragging) return;
                    if (row.DownOnGrab) Host.GrabPosition(a); else Host.EditApp(a);
                };
                row.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { dragRow = row; dragStartY = Cursor.Position.Y; dragging = false; } };
                row.MouseMove += (s, e) => DragMove(row);
                row.MouseUp += (s, e) => DragEnd(row);
                row.BtnEdit.Click += (s, e) => Host.EditApp(a);
                row.BtnDelete.Click += (s, e) => Host.DeleteApp(a);
                row.Changed += () => { Host.Save(); Host.Dash.Rebuild(); Host.LayoutPg.Map.Invalidate(); };
                var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, Font = Theme.F10 };
                menu.Items.Add("Edit…", null, (s, e) => Host.EditApp(a));
                menu.Items.Add("Move up", null, (s, e) => MoveApp(a, -1));
                menu.Items.Add("Move down", null, (s, e) => MoveApp(a, +1));
                menu.Items.Add("Preview position", null, (s, e) => Overlay.Flash(a));
                menu.Items.Add("-");
                menu.Items.Add("Remove", null, (s, e) => Host.DeleteApp(a));
                row.ContextMenuStrip = menu;
                rowList.Add(row);
                rows.Controls.Add(row);
            }
            rows.ResumeLayout();
            empty.Visible = rowList.Count == 0;
            rows.Visible = rowList.Count > 0;
            if (empty.Visible) empty.BringToFront();
            header.Visible = rowList.Count > 0;
            // the card's height depends on the number of rows: re-run the page layout, not just the rows
            rows.AutoScrollPosition = Point.Empty;
            PerformLayout();
            LayoutRows();
            Invalidate(true);
        }

        void MoveApp(AppEntry a, int dir)
        {
            var list = Host.Apps;
            int i = list.IndexOf(a), j = i + dir;
            if (i < 0 || j < 0 || j >= list.Count) return;
            list[i] = list[j]; list[j] = a;
            Host.Save();
            Host.BeginInvoke(new Action(Host.RefreshAll));
        }

        void DragMove(AppRow row)
        {
            if (dragRow != row || (Control.MouseButtons & MouseButtons.Left) == 0) return;
            if (!dragging)
            {
                if (Math.Abs(Cursor.Position.Y - dragStartY) < Theme.S(6) || rowList.Count < 2) return;
                dragging = true;
                row.Dragging = true;
                row.Capture = true;
                row.Cursor = Cursors.SizeNS;
            }
            int h = Theme.S(60);
            var pt = rows.PointToClient(Cursor.Position);
            // auto-scroll near the edges
            int sy = -rows.AutoScrollPosition.Y;
            if (pt.Y < Theme.S(24) && sy > 0) { rows.AutoScrollPosition = new Point(0, Math.Max(0, sy - h / 3)); LayoutRows(); }
            else if (pt.Y > rows.ClientSize.Height - Theme.S(24)) { rows.AutoScrollPosition = new Point(0, sy + h / 3); LayoutRows(); }

            int y = pt.Y - rows.AutoScrollPosition.Y;
            dropIndex = Math.Max(0, Math.Min(rowList.Count, (y + h / 2) / h));
            dropLine.SetBounds(Theme.S(12), rows.AutoScrollPosition.Y + dropIndex * h - Theme.S(1), rows.ClientSize.Width - Theme.S(24), Theme.S(3));
            dropLine.Visible = true;
            dropLine.BringToFront();
        }

        void DragEnd(AppRow row)
        {
            if (dragRow != row) return;
            bool wasDragging = dragging;
            dragRow = null;
            dropLine.Visible = false;
            row.Capture = false;
            row.Dragging = false;
            if (!wasDragging) return;
            int from = rowList.IndexOf(row), to = dropIndex;
            // reset after the Click that WinForms raises on this same mouse-up
            BeginInvoke(new Action(() => dragging = false));
            if (from < 0 || to < 0) return;
            if (to > from) to--;
            if (to == from) return;
            var list = Host.Apps;
            var app = list[from];
            list.RemoveAt(from);
            list.Insert(to, app);
            Host.Save();
            Host.BeginInvoke(new Action(Host.RefreshAll));
            Host.Log("Stacking order: " + string.Join(" › ", list.Select(x => x.Name)));
        }

        void LayoutRows()
        {
            int h = Theme.S(60);
            int w = rows.ClientSize.Width;
            int y0 = rows.AutoScrollPosition.Y;
            for (int i = 0; i < rowList.Count; i++) rowList[i].SetBounds(0, y0 + i * h, w, h);
            header.RowWidth = w;
            header.Invalidate();
        }

        protected override void LayoutBody(Rectangle r)
        {
            if (card == null || header == null || rows == null || empty == null) return;
            int contentH = Theme.S(48) + Math.Max(1, rowList.Count) * Theme.S(60) + Theme.S(10);
            int guideW = r.Width - Theme.S(44);
            int ch = rowList.Count == 0 ? Theme.S(26) + empty.HeightFor(guideW) + Theme.S(22) : Math.Min(r.Height, contentH);
            card.SetBounds(r.X, r.Y, r.Width, ch);
            header.SetBounds(1, Theme.S(6), r.Width - 2, Theme.S(40));
            rows.SetBounds(1, Theme.S(46), r.Width - 2, ch - Theme.S(46) - Theme.S(8));
            empty.SetBounds(Theme.S(22), Theme.S(26), guideW, empty.HeightFor(guideW));
            LayoutRows();
        }
    }

    // =====================================================================
    class LayoutPage : Page
    {
        public readonly LayoutMap Map;
        readonly Card card;
        readonly Label hint;

        public LayoutPage(MainForm host) : base(host, "Layout", "Where each app goes on your monitors.")
        {
            var preview = new FlatButton("Preview", Theme.Glyph.View, BtnStyle.Normal, 40);
            var save = new FlatButton("Save current positions", Theme.Glyph.Save, BtnStyle.Normal, 40);
            var apply = new FlatButton("Apply layout", Theme.Glyph.Refresh, BtnStyle.Primary, 40);
            preview.Click += (s, e) => { foreach (var a in Host.Apps.Where(x => x.Enabled)) Overlay.Flash(a); };
            save.Click += (s, e) =>
            {
                List<string> miss;
                int n = Host.SaveCurrentPositions(out miss);
                string m = "Saved the position of " + n + " app" + (n == 1 ? "" : "s") + ".";
                if (miss.Count > 0) m += "\n\nNot open (kept the old position): " + string.Join(", ", miss);
                MessageBox.Show(this, m, "FlightDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            apply.Click += (s, e) => { Host.ApplyLayout(); Host.ShowPage(Host.Dash); };
            AddAction(preview);
            AddAction(save);
            AddAction(apply);

            card = new Card();
            Map = new LayoutMap { BackColor = Theme.Card, Apps = Host.Apps };
            Map.AppClicked += a => Host.EditApp(a);
            hint = Theme.MakeLabel("Tip: snap and resize your windows with Windows itself (drag to the top of the screen or Win + Z), then click Save current positions. Click an app to fine-tune it.",
                Theme.F9, Theme.Muted, Theme.Card);
            hint.AutoSize = false;
            card.Controls.AddRange(new Control[] { Map, hint });
            Controls.Add(card);
        }

        protected override void LayoutBody(Rectangle r)
        {
            if (card == null || Map == null || hint == null) return;
            card.SetBounds(r.X, r.Y, r.Width, r.Height);
            int pad = Theme.S(16);
            hint.SetBounds(pad + Theme.S(4), r.Height - Theme.S(52), r.Width - 2 * pad, Theme.S(40));
            Map.SetBounds(pad, pad, r.Width - 2 * pad, r.Height - pad - Theme.S(60));
        }

        public override void OnShown() { base.OnShown(); Map.Invalidate(); }
    }

    // =====================================================================
    class SettingsPage : Page
    {
        readonly Card card, card2;
        readonly List<Control[]> settingRows = new List<Control[]>();   // title, desc, control
        readonly Label filesTitle, filesDesc;
        readonly FlowLayoutPanel buttons;

        public SettingsPage(MainForm host) : base(host, "Settings", "How FlightDeck starts and ends your flights.")
        {
            AutoScroll = true;   // small windows / high DPI: the settings scroll instead of being cut off
            card = new Card();
            Controls.Add(card);
            var cfg = Host.Cfg;

            var look = new Segmented("Dark", "Light");
            look.SetSilently(cfg.LightMode ? 1 : 0);
            look.SelectedChanged += (s, e) => Host.SetTheme(look.Selected == 1);
            AddRow("Appearance", "Dark, or Light with soft off-white tones that are easy on the eyes.", look);

            var accent = new AccentPicker(Theme.BaseAccent);
            accent.Picked += c => Host.SetAccent(c);
            AddRow("Accent colour", "Buttons, highlights, the logo, the tray icon and your desktop shortcuts.", accent);

            var minTray = new Toggle { Checked = !cfg.KeepOnTaskbar };
            minTray.CheckedChanged += (s, e) => { cfg.KeepOnTaskbar = !minTray.Checked; Host.Save(); };
            AddRow("Minimize to tray", "Minimizing FlightDeck puts it next to the clock instead of on the taskbar.", minTray);

            var launch = new TextField(cfg.LaunchTimeout.ToString()) { Width = Theme.S(90) };
            launch.ValueChanged += (s, e) => { int v = launch.IntValue(0); if (v >= 10) { cfg.LaunchTimeout = v; Host.Save(); } };
            AddRow("Launch timeout (seconds)", "How long Start Flight waits for each app window to appear.", launch);

            var close = new TextField(cfg.CloseTimeout.ToString()) { Width = Theme.S(90) };
            close.ValueChanged += (s, e) => { int v = close.IntValue(0); if (v >= 1) { cfg.CloseTimeout = v; Host.Save(); } };
            AddRow("Close timeout (seconds)", "How long End Flight gives apps to close cleanly before the last resort below.", close);

            var force = new Toggle { Checked = cfg.ForceClose };
            force.CheckedChanged += (s, e) => { cfg.ForceClose = force.Checked; Host.Save(); };
            AddRow("Force close", "Last resort for apps still running after the close timeout. Apps are always asked to exit properly first.", force);

            var simSafe = new Toggle { Checked = !cfg.ForceWhileSimRunning };
            simSafe.CheckedChanged += (s, e) => { cfg.ForceWhileSimRunning = !simSafe.Checked; Host.Save(); };
            AddRow("Never force close while the sim is open", "Forcing an add-on closed while it's connected to the simulator can crash the sim. Recommended.", simSafe);

            var admin = new Toggle { Checked = cfg.RunAsAdmin };
            admin.CheckedChanged += (s, e) =>
            {
                if (admin.Checked && !Util.IsElevated())
                {
                    if (MessageBox.Show(this, "FlightDeck will restart as administrator now.\n\nWindows asks for approval this one time; after that FlightDeck starts as administrator without asking.",
                        "FlightDeck", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK)
                        Host.BeginInvoke(new Action(() => Host.RelaunchElevated("")));
                    else admin.Checked = false;
                    return;
                }
                cfg.RunAsAdmin = admin.Checked;
                if (!admin.Checked) Elevation.RemoveTask(cfg);
                Host.Save();
            };
            AddRow("Run FlightDeck as administrator",
                "Needed to move and close apps that run as administrator (BeyondATC, tebs…). Asks Windows once, then starts that way silently. Now: " +
                (Util.IsElevated() ? "administrator." : "standard user."), admin);

            card2 = new Card();
            filesTitle = Theme.MakeLabel("Shortcuts, files & troubleshooting", Theme.F10S, Theme.Text, Theme.Card);
            filesDesc = Theme.MakeLabel("Shortcuts go to your Desktop. If an app misbehaves, open View log & diagnostics, click Copy, and paste it in your message.", Theme.F9, Theme.Muted, Theme.Card);
            filesDesc.AutoSize = false;
            buttons = new FlowLayoutPanel { BackColor = Theme.Card, WrapContents = true };
            buttons.Controls.Add(ShortcutButton("FlightDeck shortcut", "FlightDeck.lnk", "", "Open FlightDeck"));
            buttons.Controls.Add(ShortcutButton("Start Flight shortcut", "Start Flight.lnk", "--start", "Open FlightDeck and start the flight"));
            buttons.Controls.Add(ShortcutButton("End Flight shortcut", "End Flight.lnk", "--end", "End the flight and close the add-ons"));
            var logs = new FlatButton("View log & diagnostics", "\uE9D9", BtnStyle.Primary) { Margin = new Padding(0, 0, Theme.S(10), Theme.S(10)) };
            logs.Click += (s, e) => { using (var d = new LogDialog(Host.Cfg)) d.ShowDialog(this); };
            buttons.Controls.Add(logs);
            var folder = new FlatButton("Open config folder", Theme.Glyph.Folder) { Margin = new Padding(0, 0, Theme.S(10), Theme.S(10)) };
            folder.Click += (s, e) => { Directory.CreateDirectory(ConfigStore.Folder); Process.Start("explorer.exe", "\"" + ConfigStore.Folder + "\""); };
            buttons.Controls.Add(folder);
            card2.Controls.AddRange(new Control[] { filesTitle, filesDesc, buttons });
            Controls.Add(card2);
        }

        FlatButton ShortcutButton(string text, string file, string args, string desc)
        {
            var b = new FlatButton(text, Theme.Glyph.Link) { Margin = new Padding(0, 0, Theme.S(10), Theme.S(10)) };
            b.Click += (s, e) =>
            {
                try
                {
                    string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), file);
                    Util.WriteShortcutIcons();
                    Util.CreateShortcut(p, args, desc, Util.IconPath(args));
                    Native.RefreshShellIcons(new[] { p });
                    MessageBox.Show(this, "Created \"" + Path.GetFileNameWithoutExtension(file) + "\" on your Desktop.", "FlightDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, "Could not create the shortcut: " + ex.Message, "FlightDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            return b;
        }

        void AddRow(string title, string desc, Control ctl)
        {
            var t = Theme.MakeLabel(title, Theme.F10S, Theme.Text, Theme.Card);
            var d = Theme.MakeLabel(desc, Theme.F9, Theme.Muted, Theme.Card);
            d.AutoSize = false;
            card.Controls.AddRange(new Control[] { t, d, ctl });
            settingRows.Add(new[] { t, d, ctl });
        }

        protected override void LayoutBody(Rectangle r)
        {
            if (card == null || card2 == null || filesTitle == null || filesDesc == null || buttons == null) return;
            int pad = Theme.S(22), rowH = Theme.S(64), oy = AutoScrollPosition.Y;
            card.SetBounds(r.X, r.Y + oy, r.Width, settingRows.Count * rowH + Theme.S(8));
            for (int i = 0; i < settingRows.Count; i++)
            {
                int y = Theme.S(4) + i * rowH;
                var c = settingRows[i];
                c[0].Location = new Point(pad, y + Theme.S(11));
                c[2].Location = new Point(r.Width - pad - c[2].Width, y + (rowH - c[2].Height) / 2);
                c[1].SetBounds(pad, y + Theme.S(35), c[2].Left - pad - Theme.S(30), Theme.S(24));
            }
            int y2 = card.Bottom + Theme.S(16);
            card2.SetBounds(r.X, y2, r.Width, Theme.S(180));
            filesTitle.Location = new Point(pad, Theme.S(18));
            filesDesc.SetBounds(pad, Theme.S(46), r.Width - 2 * pad, Theme.S(22));
            buttons.SetBounds(pad, Theme.S(76), r.Width - 2 * pad, Theme.S(100));
            var need = new Size(0, card2.Bottom - oy + Theme.S(24));
            if (AutoScrollMinSize != need) AutoScrollMinSize = need;
        }

        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); }
    }

    /// <summary>Header button showing the active profile; opens the profile menu.</summary>
    class ProfileButton : FlatButton
    {
        readonly MainForm host;
        readonly ContextMenuStrip menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = true, Font = Theme.F10 };

        public ProfileButton(MainForm h) : base("Profile", "\uE77B", BtnStyle.Normal, 40)
        {
            host = h;
            Click += (s, e) =>
            {
                host.FillProfileMenu(menu.Items);
                menu.Show(this, new Point(0, Height + Theme.S(4)));
            };
        }

        public void RefreshText()
        {
            string n = host.Cfg.Active.Name;
            if (n.Length > 28) n = n.Substring(0, 27) + "…";
            Text = n + "   ▾";
            FitWidth();
            if (Parent != null) Parent.PerformLayout();
        }
    }
}
