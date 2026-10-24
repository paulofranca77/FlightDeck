using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FlightDeck
{
    // =====================================================================
    //  Add / edit an app
    // =====================================================================
    class EditAppDialog : DarkForm
    {
        public readonly AppEntry App;
        readonly Panel body;
        readonly TextField fName, fPath, fArgs, fDelay, fProc, fTitle, fX, fY, fW, fH;
        readonly Toggle tEnabled, tAdmin, tClose, tMax;
        readonly StartPicker startPick;
        readonly AppEntry startModel;
        readonly DarkCombo monitor;
        readonly LayoutMap map;
        readonly Label posState;
        bool loading;

        public EditAppDialog(AppEntry app, bool isNew)
        {
            App = app;
            Text = isNew ? "Add app" : "Edit " + app.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            int W = Theme.S(780), pad = Theme.S(28), full = W - 2 * pad - Theme.S(18);
            int half = (full - Theme.S(16)) / 2;
            int footerH = Theme.S(70);

            body = new Panel { BackColor = Theme.Bg, AutoScroll = true, Location = Point.Empty };
            body.HandleCreated += (s, e) => Native.DarkScrollbars(body.Handle);
            Controls.Add(body);

            int y = Theme.S(22);
            Section("App", ref y, pad);
            fName = Field("Name", app.Name, pad, y, full); y += Theme.S(66);

            fPath = Field("Program  (.exe, shortcut, or a https:// link)", app.Path, pad, y, full - Theme.S(112));
            var browse = new FlatButton("Browse…", Theme.Glyph.Folder, BtnStyle.Normal, 36) { Width = Theme.S(104) };
            browse.Location = new Point(pad + full - browse.Width, y + Theme.S(22));
            browse.Click += (s, e) => Browse();
            body.Controls.Add(browse);
            y += Theme.S(66);

            fArgs = Field("Arguments  (optional)", app.Args, pad, y, half);
            fDelay = Field("Place after (seconds once the window appears)", app.Delay.ToString(System.Globalization.CultureInfo.InvariantCulture), pad + half + Theme.S(16), y, half);
            y += Theme.S(66);
            fProc = Field("Process name  (as in Task Manager, without .exe)", app.Process, pad, y, half);
            fTitle = Field("Window title contains  (optional)", app.Title, pad + half + Theme.S(16), y, half);
            y += Theme.S(70);

            tEnabled = ToggleWithLabel("Autorun with Start Flight", app.Enabled, pad, y);
            tAdmin = ToggleWithLabel("Run as administrator", app.Admin, pad + Theme.S(250), y);
            tClose = ToggleWithLabel("Close on End Flight", app.CloseOnEnd, pad + Theme.S(480), y);
            y += Theme.S(40);
            var startLbl = Theme.MakeLabel("Open", Theme.F10, Theme.SubText, Theme.Bg);
            startLbl.Location = new Point(pad, y + Theme.S(6));
            startModel = new AppEntry { StartMode = app.StartMode, StartDelay = app.StartDelay };   // edited here, saved on Save
            startPick = new StartPicker(startModel) { Width = Theme.S(170), Location = new Point(pad + Theme.S(60), y) };
            var simHint = Theme.MakeLabel("Right away, a few seconds after Start Flight, when the simulator is ready for add-ons (SimConnect), or once your flight has loaded.", Theme.F9, Theme.Muted, Theme.Bg);
            simHint.AutoSize = false;
            simHint.SetBounds(pad + Theme.S(250), y - Theme.S(2), full - Theme.S(250), Theme.S(38));
            body.Controls.Add(startLbl); body.Controls.Add(startPick); body.Controls.Add(simHint);
            y += Theme.S(48);

            Divider(pad, y, full); y += Theme.S(18);
            Section("Window position", ref y, pad);
            posState = Theme.MakeLabel("", Theme.F9, Theme.Muted, Theme.Bg);
            posState.Location = new Point(pad + Theme.S(170), y - Theme.S(28));
            body.Controls.Add(posState);

            map = new LayoutMap { BackColor = Theme.Card, OnlyFocus = true, Highlight = app };
            map.SetBounds(pad, y, full, Theme.S(170));
            body.Controls.Add(map);
            y += Theme.S(182);

            var grab = new FlatButton("Grab from open window", Theme.Glyph.Target, BtnStyle.Normal, 36);
            grab.Location = new Point(pad, y);
            grab.Click += (s, e) => Grab();
            var preview = new FlatButton("Preview", Theme.Glyph.View, BtnStyle.Normal, 36);
            preview.Location = new Point(grab.Right + Theme.S(10), y);
            preview.Click += (s, e) => { ReadFields(); Overlay.Flash(App); };
            body.Controls.Add(grab); body.Controls.Add(preview);
            y += Theme.S(50);

            var snapLbl = Theme.MakeLabel("Snap to monitor zone", Theme.F9, Theme.Muted, Theme.Bg);
            snapLbl.Location = new Point(pad, y);
            body.Controls.Add(snapLbl);
            y += Theme.S(22);
            monitor = new DarkCombo { Width = Theme.S(200), Location = new Point(pad, y + Theme.S(4)) };
            var screens = Util.Screens();
            for (int i = 0; i < screens.Length; i++)
                monitor.Items.Add("Monitor " + (i + 1) + "  (" + screens[i].Bounds.Width + "×" + screens[i].Bounds.Height + ")");
            monitor.SelectedIndex = app.HasPos ? Util.MonitorIndex(new Rectangle(app.X, app.Y, Math.Max(1, app.W), Math.Max(1, app.H))) : 0;
            body.Controls.Add(monitor);

            var zones = new[]
            {
                new { T = "Left ½", X = 0.0, Wd = 0.5 }, new { T = "Right ½", X = 0.5, Wd = 0.5 },
                new { T = "Left ⅓", X = 0.0, Wd = 1 / 3.0 }, new { T = "Middle ⅓", X = 1 / 3.0, Wd = 1 / 3.0 }, new { T = "Right ⅓", X = 2 / 3.0, Wd = 1 / 3.0 },
                new { T = "Left ⅔", X = 0.0, Wd = 2 / 3.0 }, new { T = "Right ⅔", X = 1 / 3.0, Wd = 2 / 3.0 }, new { T = "Full", X = 0.0, Wd = 1.0 }
            };
            var flow = new FlowLayoutPanel { BackColor = Theme.Bg, Location = new Point(monitor.Right + Theme.S(12), y), Width = full - monitor.Width - Theme.S(12), Height = Theme.S(82), WrapContents = true };
            foreach (var z in zones)
            {
                var zz = z;
                var b = new FlatButton(z.T, null, BtnStyle.Normal, 34) { Margin = new Padding(0, 0, Theme.S(6), Theme.S(6)) };
                b.Click += (s, e) => Snap(zz.X, zz.Wd);
                flow.Controls.Add(b);
            }
            body.Controls.Add(flow);
            y += Theme.S(92);

            int q = (full - 3 * Theme.S(12)) / 4;
            fX = Field("X", app.X.ToString(), pad, y, q);
            fY = Field("Y", app.Y.ToString(), pad + (q + Theme.S(12)), y, q);
            fW = Field("Width", app.W.ToString(), pad + 2 * (q + Theme.S(12)), y, q);
            fH = Field("Height", app.H.ToString(), pad + 3 * (q + Theme.S(12)), y, q);
            y += Theme.S(70);
            tMax = ToggleWithLabel("Maximize on this monitor", app.Maximized, pad, y);
            y += Theme.S(40);
            body.AutoScrollMinSize = new Size(0, y + Theme.S(10));

            foreach (var f in new[] { fX, fY, fW, fH }) f.ValueChanged += (s, e) => { if (!loading) { ReadFields(); App.HasPos = App.W > 0 && App.H > 0; UpdatePosState(); map.Invalidate(); } };
            tMax.CheckedChanged += (s, e) => { if (!loading) { ReadFields(); map.Invalidate(); } };

            // footer
            var footer = new Panel { BackColor = Theme.Sidebar, Dock = DockStyle.Bottom, Height = footerH };
            var save = new FlatButton("Save", Theme.Glyph.Save, BtnStyle.Primary, 40);
            var cancel = new FlatButton("Cancel", null, BtnStyle.Normal, 40);
            save.Width = Math.Max(save.Width, Theme.S(110));
            save.Click += (s, e) => DoSave();
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            footer.Controls.Add(save); footer.Controls.Add(cancel);
            footer.Resize += (s, e) =>
            {
                save.Location = new Point(footer.Width - pad - save.Width, (footerH - save.Height) / 2);
                cancel.Location = new Point(save.Left - Theme.S(10) - cancel.Width, save.Top);
            };
            Controls.Add(footer);
            CancelButton = null;

            int wantH = y + Theme.S(16) + footerH;
            int maxH = Screen.FromPoint(Cursor.Position).WorkingArea.Height - Theme.S(80);
            ClientSize = new Size(W, Math.Min(wantH, maxH));
            body.Size = new Size(W, ClientSize.Height - footerH);
            UpdatePosState();
        }

        // ---------- builders ----------
        void Section(string text, ref int y, int x)
        {
            var l = Theme.MakeLabel(text, Theme.F11S, Theme.Text, Theme.Bg);
            l.Location = new Point(x, y);
            body.Controls.Add(l);
            y += Theme.S(38);
        }

        void Divider(int x, int y, int w)
        {
            var p = new Panel { BackColor = Theme.CardBorder, Bounds = new Rectangle(x, y, w, 1) };
            body.Controls.Add(p);
        }

        TextField Field(string label, string value, int x, int y, int w)
        {
            var l = Theme.MakeLabel(label, Theme.F9, Theme.Muted, Theme.Bg);
            l.Location = new Point(x, y);
            var f = new TextField(value) { Width = w, Location = new Point(x, y + Theme.S(22)) };
            body.Controls.Add(l);
            body.Controls.Add(f);
            return f;
        }

        Toggle ToggleWithLabel(string text, bool value, int x, int y)
        {
            var t = new Toggle { Checked = value, Location = new Point(x, y) };
            var l = Theme.MakeLabel(text, Theme.F10, Theme.SubText, Theme.Bg);
            l.Location = new Point(x + t.Width + Theme.S(10), y + (t.Height - Theme.S(19)) / 2);
            l.Cursor = Cursors.Hand;
            l.Click += (s, e) => t.Checked = !t.Checked;
            body.Controls.Add(t); body.Controls.Add(l);
            return t;
        }

        // ---------- actions ----------
        void ReadFields()
        {
            App.X = fX.IntValue(App.X); App.Y = fY.IntValue(App.Y);
            App.W = fW.IntValue(App.W); App.H = fH.IntValue(App.H);
            App.Maximized = tMax.Checked;
            App.Name = fName.Value.Trim();
        }

        void WriteFields()
        {
            loading = true;
            fX.Value = App.X.ToString(); fY.Value = App.Y.ToString(); fW.Value = App.W.ToString(); fH.Value = App.H.ToString();
            tMax.Checked = App.Maximized;
            loading = false;
            UpdatePosState();
            map.Invalidate();
        }

        void UpdatePosState()
        {
            posState.Text = App.HasPos ? Util.Placement(App) : "Not set yet — grab it from the open window or pick a zone.";
            posState.ForeColor = App.HasPos ? Theme.Muted : Theme.Orange;
        }

        void Browse()
        {
            using (var ofd = new OpenFileDialog { Filter = "Programs and shortcuts (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|All files|*.*" })
            {
                try { if (File.Exists(fPath.Value)) ofd.InitialDirectory = Path.GetDirectoryName(fPath.Value); } catch { }
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                fPath.Value = ofd.FileName;
                if (string.IsNullOrWhiteSpace(fProc.Value) && ofd.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    fProc.Value = Path.GetFileNameWithoutExtension(ofd.FileName);
                if (string.IsNullOrWhiteSpace(fName.Value)) fName.Value = Path.GetFileNameWithoutExtension(ofd.FileName);
            }
        }

        void Grab()
        {
            var probe = new AppEntry { Process = fProc.Value.Trim().Replace(".exe", ""), Title = fTitle.Value.Trim() };
            var w = Engine.FindWindow(probe);
            if (w == null || w.Minimized)
            {
                MessageBox.Show(this, "Couldn't find an open window for process \"" + probe.Process + "\".\n\nOpen the app, put the window where you want it (snap it, resize it), and try again.",
                    "FlightDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            App.SetPlacement(w);
            WriteFields();
            monitor.SelectedIndex = Math.Max(0, Math.Min(monitor.Items.Count - 1, Util.MonitorIndex(new Rectangle(App.X, App.Y, App.W, App.H))));
        }

        void Snap(double fx, double fw)
        {
            var screens = Util.Screens();
            int i = Math.Max(0, Math.Min(screens.Length - 1, monitor.SelectedIndex));
            var wa = screens[i].WorkingArea;
            int x0 = wa.X + (int)Math.Round(wa.Width * fx);
            int x1 = wa.X + (int)Math.Round(wa.Width * (fx + fw));
            App.X = x0; App.Y = wa.Y; App.W = x1 - x0; App.H = wa.Height;
            App.Maximized = false; App.HasPos = true;
            WriteFields();
        }

        void DoSave()
        {
            ReadFields();
            if (App.Name == "") { MessageBox.Show(this, "Give the app a name.", "FlightDeck"); return; }
            if (fPath.Value.Trim() == "" && fProc.Value.Trim() == "")
            {
                MessageBox.Show(this, "Set at least the program path or the process name.", "FlightDeck"); return;
            }
            App.Path = fPath.Value.Trim().Trim('"');
            App.Args = fArgs.Value.Trim();
            App.Process = fProc.Value.Trim();
            if (App.Process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) App.Process = App.Process.Substring(0, App.Process.Length - 4);
            App.Title = fTitle.Value.Trim();
            App.Delay = Math.Max(0, fDelay.DoubleValue(2));
            App.Enabled = tEnabled.Checked; App.Admin = tAdmin.Checked; App.CloseOnEnd = tClose.Checked; App.StartMode = startModel.StartMode; App.StartDelay = startModel.StartDelay;
            App.HasPos = App.W > 0 && App.H > 0;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // =====================================================================
    //  Capture open windows
    // =====================================================================
    class CaptureDialog : DarkForm
    {
        public readonly List<WinInfo> Selected = new List<WinInfo>();
        readonly List<KeyValuePair<Toggle, WinInfo>> items = new List<KeyValuePair<Toggle, WinInfo>>();

        static readonly HashSet<string> skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "explorer", "ApplicationFrameHost", "TextInputHost", "SystemSettings", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost", "LockApp" };

        public CaptureDialog(List<AppEntry> apps)
        {
            Text = "Capture windows";
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Theme.S(820), Theme.S(600));
            MinimumSize = new Size(Theme.S(600), Theme.S(400));
            int pad = Theme.S(24);

            var head = new Panel { Dock = DockStyle.Top, Height = Theme.S(92), BackColor = Theme.Bg };
            var t = Theme.MakeLabel("Pick the windows FlightDeck should manage", Theme.F11S, Theme.Text, Theme.Bg);
            t.Location = new Point(pad, Theme.S(20));
            var d = Theme.MakeLabel("Their current position and size are saved exactly as they are now — snapped windows included.\nApps already on your list are just updated.", Theme.F9, Theme.Muted, Theme.Bg);
            d.Location = new Point(pad, Theme.S(48));
            head.Controls.Add(t); head.Controls.Add(d);

            var list = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(pad, 0, pad, Theme.S(8)) };
            list.HandleCreated += (s, e) => Native.DarkScrollbars(list.Handle);

            int own = System.Diagnostics.Process.GetCurrentProcess().Id;
            var wins = Native.ListWindows().Where(w => w.Pid != own && !w.Minimized && w.ProcessName != "" && !skip.Contains(w.ProcessName)).ToList();
            var rowsCtl = new List<Control>();
            foreach (var w in wins)
            {
                var known = apps.FirstOrDefault(a => string.Equals(a.Process, w.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                                                      (string.IsNullOrWhiteSpace(a.Title) || w.Title.IndexOf(a.Title, StringComparison.OrdinalIgnoreCase) >= 0));
                var row = new CaptureRow(w, known != null ? known.Name : null) { Dock = DockStyle.Top };
                items.Add(new KeyValuePair<Toggle, WinInfo>(row.Pick, w));
                rowsCtl.Add(row);
            }
            if (rowsCtl.Count == 0)
            {
                var none = Theme.MakeLabel("No open windows found. Open your apps first.", Theme.F10, Theme.Muted, Theme.Bg);
                none.Location = new Point(pad, Theme.S(10));
                list.Controls.Add(none);
            }
            // Dock=Top stacks in reverse order of adding
            for (int i = rowsCtl.Count - 1; i >= 0; i--) list.Controls.Add(rowsCtl[i]);

            var footer = new Panel { BackColor = Theme.Sidebar, Dock = DockStyle.Bottom, Height = Theme.S(70) };
            var ok = new FlatButton("Add selected", Theme.Glyph.Add, BtnStyle.Primary, 40);
            var cancel = new FlatButton("Cancel", null, BtnStyle.Normal, 40);
            ok.Click += (s, e) =>
            {
                foreach (var kv in items) if (kv.Key.Checked) Selected.Add(kv.Value);
                DialogResult = DialogResult.OK; Close();
            };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            footer.Controls.Add(ok); footer.Controls.Add(cancel);
            footer.Resize += (s, e) =>
            {
                ok.Location = new Point(footer.Width - pad - ok.Width, (footer.Height - ok.Height) / 2);
                cancel.Location = new Point(ok.Left - Theme.S(10) - cancel.Width, ok.Top);
            };

            Controls.Add(list);
            Controls.Add(footer);
            Controls.Add(head);
        }
    }

    class CaptureRow : Owner
    {
        public readonly Toggle Pick = new Toggle();
        readonly WinInfo win;
        readonly string known;
        readonly string exePath;

        public CaptureRow(WinInfo w, string knownName)
        {
            win = w; known = knownName;
            exePath = Native.GetProcessPath(w.Pid);
            BackColor = Theme.Bg;
            Height = Theme.S(64);
            Pick.BackColor = Theme.Card;
            Controls.Add(Pick);
            Cursor = Cursors.Hand;
            Click += (s, e) => Pick.Checked = !Pick.Checked;
            Pick.CheckedChanged += (s, e) => Invalidate();
        }
        protected override void OnParentChanged(EventArgs e) { }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            Pick.Location = new Point(Theme.S(16), (Height - Theme.S(4) - Pick.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - Theme.S(6));
            Theme.FillRound(g, hover ? Theme.Hover : Theme.Card, r, Theme.S(10), Pick.Checked ? Theme.Alpha(Theme.Accent, 180) : Theme.CardBorder);
            Pick.BackColor = hover ? Theme.Hover : Theme.Card;

            int x = Theme.S(16) + Pick.Width + Theme.S(16);
            int ic = Theme.S(32);
            var icon = AppIcons.Load(exePath, ic);
            var icr = new Rectangle(x, (Height - Theme.S(6) - ic) / 2, ic, ic);
            if (icon != null) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(icon, icr); }
            else Theme.DrawTile(g, win.ProcessName, icr);
            x += ic + Theme.S(14);
            int rightW = Theme.S(230);
            var flags = TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            string name = win.ProcessName + (known != null ? "" : "");
            TextRenderer.DrawText(g, name, Theme.F10S, new Rectangle(x, Theme.S(11), Width - x - rightW, Theme.S(22)), Theme.Text, flags);
            TextRenderer.DrawText(g, win.Title, Theme.F9, new Rectangle(x, Theme.S(33), Width - x - rightW, Theme.S(20)), Theme.Muted, flags);

            var rect = new Rectangle(win.Frame.Left, win.Frame.Top, win.Frame.Width, win.Frame.Height);
            string pos = "Monitor " + (Util.MonitorIndex(rect) + 1) + " · " + (win.Maximized ? "maximized" : win.Frame.Width + "×" + win.Frame.Height);
            TextRenderer.DrawText(g, pos, Theme.F9, new Rectangle(Width - rightW, Theme.S(11), rightW - Theme.S(16), Theme.S(22)), Theme.SubText, flags | TextFormatFlags.Right);
            if (known != null)
                TextRenderer.DrawText(g, "updates " + known, Theme.F9, new Rectangle(Width - rightW, Theme.S(33), rightW - Theme.S(16), Theme.S(20)), Theme.AccentInk, flags | TextFormatFlags.Right);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Pick.Invalidate(); }
        protected override void OnMouseLeave(EventArgs e)
        {
            if (ClientRectangle.Contains(PointToClient(Cursor.Position))) return;
            base.OnMouseLeave(e); Pick.Invalidate();
        }
    }

    /// <summary>Short how-to shown when "Grab position" is clicked but the app isn't open.</summary>
    class GrabGuideDialog : DarkForm
    {
        public GrabGuideDialog(AppEntry a)
        {
            Text = "Grab position";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            int pad = Theme.S(26), w = Theme.S(520);
            ClientSize = new Size(w, Theme.S(330));

            var head = Theme.MakeLabel(a.Name + " isn't open yet", Theme.F11S, Theme.Text, Theme.Bg);
            head.Location = new Point(pad, Theme.S(22));
            Controls.Add(head);
            var sub = Theme.MakeLabel("FlightDeck copies the position from the real window. Three quick steps:", Theme.F9, Theme.Muted, Theme.Bg);
            sub.Location = new Point(pad, Theme.S(52));
            Controls.Add(sub);

            string[] steps =
            {
                "Open " + a.Name + ".",
                "Put its window where you want it — snap it, resize it.",
                "Come back here and click Grab position again.",
            };
            int y = Theme.S(92);
            for (int i = 0; i < steps.Length; i++)
            {
                var n = new StepBadge(i + 1) { Location = new Point(pad, y) };
                var l = Theme.MakeLabel(steps[i], Theme.F10, Theme.SubText, Theme.Bg);
                l.AutoSize = false;
                l.SetBounds(pad + Theme.S(42), y + Theme.S(4), w - pad * 2 - Theme.S(42), Theme.S(24));
                Controls.Add(n); Controls.Add(l);
                y += Theme.S(48);
            }

            var ok = new FlatButton("Got it", null, BtnStyle.Normal, 40);
            ok.Location = new Point(w - pad - ok.Width, Theme.S(330) - Theme.S(64));
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(ok);
            if (!string.IsNullOrWhiteSpace(a.Path))
            {
                var open = new FlatButton("Open " + (a.Name.Length > 22 ? "it now" : a.Name + " now"), null, BtnStyle.Primary, 40);
                open.Location = new Point(ok.Left - Theme.S(10) - open.Width, ok.Top);
                open.Click += (s, e) => { DialogResult = DialogResult.Yes; Close(); };
                Controls.Add(open);
            }
        }
    }

    class StepBadge : Owner
    {
        readonly int n;
        public StepBadge(int number) { n = number; Size = new Size(Theme.S(30), Theme.S(30)); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(g, n.ToString(), Theme.F10S, ClientRectangle, Theme.AccentText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Shows diagnostics + recent activity + crash log, with a one-click copy.</summary>
    class LogDialog : DarkForm
    {
        readonly TextBox box;
        readonly Config cfg;

        public LogDialog(Config c)
        {
            cfg = c;
            Text = "Log & diagnostics";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            ClientSize = new Size(Theme.S(900), Theme.S(620));
            MinimumSize = new Size(Theme.S(600), Theme.S(400));
            int pad = Theme.S(20);

            var head = new Panel { Dock = DockStyle.Top, Height = Theme.S(70), BackColor = Theme.Bg };
            var t = Theme.MakeLabel("Log & diagnostics", Theme.F11S, Theme.Text, Theme.Bg);
            t.Location = new Point(pad, Theme.S(14));
            var d = Theme.MakeLabel("Copy everything and paste it in the chat — it shows each app's windows and what FlightDeck did with them.", Theme.F9, Theme.Muted, Theme.Bg);
            d.Location = new Point(pad, Theme.S(42));
            head.Controls.Add(t); head.Controls.Add(d);

            var wrap = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(pad, 0, pad, 0) };
            box = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.SubText, Font = new Font("Consolas", 9f)
            };
            box.HandleCreated += (s, e) => Native.DarkScrollbars(box.Handle);
            wrap.Controls.Add(box);

            var footer = new Panel { BackColor = Theme.Sidebar, Dock = DockStyle.Bottom, Height = Theme.S(70) };
            var copy = new FlatButton("Copy all", "\uE8C8", BtnStyle.Primary, 40);
            var refresh = new FlatButton("Refresh", Theme.Glyph.Refresh, BtnStyle.Normal, 40);
            var folder = new FlatButton("Open folder", Theme.Glyph.Folder, BtnStyle.Normal, 40);
            var close = new FlatButton("Close", null, BtnStyle.Normal, 40);
            copy.Click += (s, e) =>
            {
                try { Clipboard.SetText(box.Text); copy.Text = "Copied ✓"; copy.FitWidth(); footer.PerformLayout(); }
                catch (Exception ex) { MessageBox.Show(this, "Could not copy: " + ex.Message, "FlightDeck"); }
            };
            refresh.Click += (s, e) => Fill();
            folder.Click += (s, e) => { try { System.Diagnostics.Process.Start("explorer.exe", "\"" + ConfigStore.Folder + "\""); } catch { } };
            close.Click += (s, e) => Close();
            footer.Controls.AddRange(new Control[] { copy, refresh, folder, close });
            footer.Layout += (s, e) =>
            {
                int y = (footer.Height - copy.Height) / 2;
                copy.Location = new Point(footer.Width - pad - copy.Width, y);
                close.Location = new Point(pad, y);
                refresh.Location = new Point(copy.Left - Theme.S(10) - refresh.Width, y);
                folder.Location = new Point(refresh.Left - Theme.S(10) - folder.Width, y);
            };

            Controls.Add(wrap);
            Controls.Add(footer);
            Controls.Add(head);
            Fill();
        }

        void Fill()
        {
            var sb = new System.Text.StringBuilder();
            try { sb.AppendLine(Diagnostics.Report(cfg)); } catch (Exception ex) { sb.AppendLine("(diagnostics failed: " + ex.Message + ")"); }
            sb.AppendLine("==== Recent activity ====");
            string act = Diagnostics.Tail(Diagnostics.LogPath, 400);
            sb.AppendLine(act == "" ? "(empty)" : act);
            sb.AppendLine();
            sb.AppendLine("==== Crash log ====");
            string crash = Diagnostics.Tail(Diagnostics.CrashPath, 150);
            sb.AppendLine(crash == "" ? "(no crashes)" : crash);
            box.Text = sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
            box.SelectionStart = 0; box.ScrollToCaret();
        }
    }

    /// <summary>Asks to restart elevated when some apps run as administrator.</summary>
    class AdminNeededDialog : DarkForm
    {
        readonly Toggle dontAsk;
        public bool DontAskAgain { get { return dontAsk.Checked; } }

        public AdminNeededDialog(List<string> apps)
        {
            Text = "Administrator needed";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            int pad = Theme.S(26), w = Theme.S(560), h = Theme.S(330);
            ClientSize = new Size(w, h);

            var head = Theme.MakeLabel("Some apps run as administrator", Theme.F11S, Theme.Text, Theme.Bg);
            head.Location = new Point(pad, Theme.S(22));
            string list = apps.Count <= 3 ? string.Join(", ", apps) : string.Join(", ", apps.Take(3)) + " and " + (apps.Count - 3) + " more";
            var body = Theme.MakeLabel(list + (apps.Count == 1 ? " runs" : " run") + " as administrator, so Windows won't let FlightDeck move " +
                (apps.Count == 1 ? "it" : "them") + " unless FlightDeck runs as administrator too.\n\n" +
                "Restart as administrator once and FlightDeck remembers it — after a one-time Windows approval it starts that way without asking again.",
                Theme.F10, Theme.SubText, Theme.Bg);
            body.AutoSize = false;
            body.SetBounds(pad, Theme.S(58), w - 2 * pad, Theme.S(150));

            dontAsk = new Toggle { Location = new Point(pad, Theme.S(214)) };
            var dl = Theme.MakeLabel("Don't ask again", Theme.F9, Theme.Muted, Theme.Bg);
            dl.Location = new Point(pad + dontAsk.Width + Theme.S(10), Theme.S(216));
            dl.Click += (s, e) => dontAsk.Checked = !dontAsk.Checked;

            var restart = new FlatButton("Restart as administrator", Theme.Glyph.Shield, BtnStyle.Primary, 40);
            var anyway = new FlatButton("Start anyway", null, BtnStyle.Normal, 40);
            restart.Location = new Point(w - pad - restart.Width, h - Theme.S(66));
            anyway.Location = new Point(restart.Left - Theme.S(10) - anyway.Width, restart.Top);
            restart.Click += (s, e) => { DialogResult = DialogResult.Yes; Close(); };
            anyway.Click += (s, e) => { DialogResult = DialogResult.No; Close(); };
            Controls.AddRange(new Control[] { head, body, dontAsk, dl, restart, anyway });
        }
    }
}
