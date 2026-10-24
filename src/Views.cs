using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace FlightDeck
{
    /// <summary>Miniature of all monitors with app windows drawn on top.</summary>
    class LayoutMap : Owner
    {
        public List<AppEntry> Apps = new List<AppEntry>();
        public AppEntry Highlight;           // drawn highlighted
        public bool OnlyFocus;            // edit dialog: draw just this one
        public event Action<AppEntry> AppClicked;
        readonly List<KeyValuePair<RectangleF, AppEntry>> hits = new List<KeyValuePair<RectangleF, AppEntry>>();

        public LayoutMap() { Cursor = Cursors.Default; }

        Rectangle AppRect(AppEntry a)
        {
            if (a.Maximized)
                return Util.Screens()[Util.MonitorIndex(new Rectangle(a.X, a.Y, Math.Max(1, a.W), Math.Max(1, a.H)))].WorkingArea;
            return new Rectangle(a.X, a.Y, Math.Max(1, a.W), Math.Max(1, a.H));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            hits.Clear();

            var screens = Util.Screens();
            if (screens.Length == 0) return;
            Rectangle vb = screens[0].Bounds;
            foreach (var s in screens) vb = Rectangle.Union(vb, s.Bounds);

            float pad = Theme.S(14);
            float sc = Math.Min((Width - 2 * pad) / vb.Width, (Height - 2 * pad) / vb.Height);
            if (sc <= 0) return;
            float ox = (Width - vb.Width * sc) / 2f, oy = (Height - vb.Height * sc) / 2f;
            Func<Rectangle, RectangleF> map = r => new RectangleF(ox + (r.X - vb.X) * sc, oy + (r.Y - vb.Y) * sc, r.Width * sc, r.Height * sc);

            float gap = Theme.S(3);
            for (int i = 0; i < screens.Length; i++)
            {
                var r = map(screens[i].Bounds);
                r.Inflate(-gap, -gap);
                Theme.FillRound(g, Theme.MonitorFill, r, Theme.S(8), Theme.MonitorBorder);
                string lbl = (i + 1).ToString();
                using (var f = new Font("Segoe UI Semibold", Math.Max(9f, Math.Min(r.Height * 0.16f, 40f) / Theme.Scale * 0.75f)))
                    TextRenderer.DrawText(g, lbl, f, new Rectangle((int)r.X + Theme.S(8), (int)r.Y + Theme.S(4), (int)r.Width, (int)r.Height), Theme.Faint, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
                string res = screens[i].Bounds.Width + "×" + screens[i].Bounds.Height + (screens[i].Primary ? "  ·  main" : "");
                TextRenderer.DrawText(g, res, Theme.F8, new Rectangle((int)r.X, (int)r.Bottom - Theme.S(22), (int)r.Width - Theme.S(8), Theme.S(18)), Theme.Faint, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            var list = OnlyFocus ? (Highlight != null ? new List<AppEntry> { Highlight } : new List<AppEntry>()) : Apps.Where(a => a.HasPos && a.Enabled).ToList();
            // stacking order: last in the list is furthest back, so draw it first
            list.Reverse();
            if (!OnlyFocus && Highlight != null && list.Remove(Highlight)) list.Add(Highlight);
            var drawn = new List<KeyValuePair<RectangleF, AppEntry>>();
            foreach (var a in list)
            {
                if (!a.HasPos) continue;
                var r = map(AppRect(a));
                r.Inflate(-gap - 1, -gap - 1);
                bool hl = a == Highlight;
                Theme.FillRound(g, Theme.Alpha(Theme.Accent, hl ? 120 : 46), r, Theme.S(6), Theme.Alpha(Theme.Accent, hl ? 255 : 170));
                hits.Add(new KeyValuePair<RectangleF, AppEntry>(r, a));
                drawn.Add(new KeyValuePair<RectangleF, AppEntry>(r, a));
            }

            // name chips in the bottom-left corner of each window; chips that would collide are stacked upwards
            var placed = new List<RectangleF>();
            int chipH = Theme.S(22), icon = Theme.S(14);
            foreach (var kv in drawn)
            {
                var r = kv.Key; var a = kv.Value;
                bool hl = a == Highlight;
                int maxW = (int)Math.Max(Theme.S(40), r.Width - Theme.S(10));
                int textW = Math.Min(maxW - icon - Theme.S(14), TextRenderer.MeasureText(a.Name, Theme.F8).Width);
                float w = Math.Max(Theme.S(30), textW + icon + Theme.S(16));
                var chip = new RectangleF(r.X + Theme.S(5), r.Bottom - Theme.S(5) - chipH, w, chipH);
                for (int guard = 0; guard < 20 && placed.Any(p => p.IntersectsWith(chip)); guard++) chip.Y -= chipH + Theme.S(3);
                placed.Add(chip);
                Theme.FillRound(g, hl ? Theme.Accent : Theme.Card, chip, Theme.S(6), Theme.Alpha(Theme.Accent, 200));
                var bmp = AppIcons.Get(a, icon);
                var ir = new Rectangle((int)chip.X + Theme.S(5), (int)(chip.Y + (chipH - icon) / 2f), icon, icon);
                if (bmp != null) g.DrawImage(bmp, ir); else Theme.DrawTile(g, a.Name, ir);
                TextRenderer.DrawText(g, a.Name, Theme.F8, new Rectangle(ir.Right + Theme.S(5), (int)chip.Y, textW + Theme.S(4), chipH),
                    hl ? Theme.AccentText : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
            Cursor = hits.Count > 0 && AppClicked != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Key.Contains(e.Location)) { var h = AppClicked; if (h != null) h(hits[i].Value); return; }
        }
    }

    /// <summary>Shared column positions for the apps table.</summary>
    static class Cols
    {
        // 0 grip · 1 name · 2 path · 3 position · 4 place after · 5 autorun · 6 admin · 7 auto-close · 8 start · 9 actions
        public static int[] X(int w)
        {
            int grip = Theme.S(32), wait = Theme.S(90), tog = Theme.S(84), start = Theme.S(140), act = Theme.S(80);
            int flex = Math.Max(Theme.S(300), w - grip - wait - tog * 3 - start - act - Theme.S(20));
            int nameW = (int)(flex * 0.32), pathW = (int)(flex * 0.28);
            int x0 = Theme.S(10), x1 = x0 + grip;
            int x4 = x1 + flex;
            return new[] { x0, x1, x1 + nameW, x1 + nameW + pathW, x4, x4 + wait, x4 + wait + tog, x4 + wait + tog * 2, x4 + wait + tog * 3, x4 + wait + tog * 3 + start };
        }
        public static readonly string[] Headers = { "", "NAME", "PATH", "POSITION", "PLACE AFTER", "AUTORUN", "ADMIN", "AUTO-CLOSE", "START", "" };
    }

    class TableHeader : Owner
    {
        public int RowWidth;
        public TableHeader() { Height = Theme.S(40); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var x = Cols.X(RowWidth > 0 ? RowWidth : Width);
            for (int i = 0; i < Cols.Headers.Length; i++)
                TextRenderer.DrawText(g, Cols.Headers[i], Theme.F8, new Point(x[i], (Height - Theme.S(14)) / 2), Theme.Muted, TextFormatFlags.NoPadding);
        }
    }

    class AppRow : Owner
    {
        public readonly AppEntry App;
        public readonly int Rank;
        public readonly Toggle Autorun = new Toggle(), Admin = new Toggle(), AutoClose = new Toggle();
        public readonly StartPicker Start;
        public readonly TextField Wait;
        public readonly FlatButton BtnEdit = new FlatButton(null, Theme.Glyph.Edit, BtnStyle.Ghost, 32);
        public readonly FlatButton BtnDelete = new FlatButton(null, Theme.Glyph.Delete, BtnStyle.Ghost, 32);
        public event Action Changed;
        static readonly ToolTip simTip = new ToolTip();
        bool dragging, linkHot;
        static readonly Font LinkFont = new Font(Theme.F9S, FontStyle.Underline);
        public bool DownOnGrab { get; private set; }
        public bool Dragging { get { return dragging; } set { dragging = value; Invalidate(); } }

        public AppRow(AppEntry a, int rank)
        {
            App = a;
            Rank = rank;
            BackColor = Theme.Card;
            Height = Theme.S(60);
            Cursor = Cursors.Hand;
            Wait = new TextField(a.Delay.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)) { Width = Theme.S(48), Height = Theme.S(32) };
            Autorun.Checked = a.Enabled; Admin.Checked = a.Admin; AutoClose.Checked = a.CloseOnEnd;
            Start = new StartPicker(a) { Width = Theme.S(128) };
            Start.Changed += Fire;
            simTip.SetToolTip(Start, "When Start Flight opens this app: right away, a few seconds later, when the simulator is ready (SimConnect), or once your flight has loaded.");
            simTip.SetToolTip(Wait, "Seconds to wait after the app's window appears before moving it into place.");
            Autorun.CheckedChanged += (s, e) => { App.Enabled = Autorun.Checked; Fire(); };
            Admin.CheckedChanged += (s, e) => { App.Admin = Admin.Checked; Fire(); };
            AutoClose.CheckedChanged += (s, e) => { App.CloseOnEnd = AutoClose.Checked; Fire(); };
            Wait.ValueChanged += (s, e) => { App.Delay = Math.Max(0, Math.Min(600, Wait.DoubleValue(App.Delay))); Fire(); };
            Controls.AddRange(new Control[] { Wait, Autorun, Admin, AutoClose, Start, BtnEdit, BtnDelete });
            foreach (Control c in Controls) { c.MouseEnter += (s, e) => SetHover(true); c.MouseLeave += (s, e) => CheckHover(); }
            Wait.Box.MouseEnter += (s, e) => SetHover(true);
        }

        void Fire() { var h = Changed; if (h != null) h(); }
        void SetHover(bool v) { if (hover != v) { hover = v; BackColor = v ? Theme.RowHover : Theme.Card; foreach (Control c in Controls) c.BackColor = BackColor; Invalidate(true); } }
        void CheckHover() { SetHover(ClientRectangle.Contains(PointToClient(Cursor.Position))); }
        protected override void OnMouseEnter(EventArgs e) { SetHover(true); }
        protected override void OnMouseLeave(EventArgs e) { if (linkHot) { linkHot = false; Invalidate(); } CheckHover(); }
        protected override void OnParentChanged(EventArgs e) { }   // keep own BackColor

        Rectangle GrabRect()
        {
            if (App.HasPos) return Rectangle.Empty;
            var x = Cols.X(Width);
            int w = TextRenderer.MeasureText("Not set · Grab position", Theme.F9S).Width;
            return new Rectangle(x[3] - Theme.S(4), Theme.S(12), Math.Min(w + Theme.S(8), x[4] - x[3]), Height - Theme.S(24));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool lh = GrabRect().Contains(e.Location);
            if (lh != linkHot) { linkHot = lh; Invalidate(); }
            if (!dragging) Cursor = e.X < Cols.X(Width)[1] ? Cursors.SizeNS : Cursors.Hand;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            DownOnGrab = e.Button == MouseButtons.Left && GrabRect().Contains(e.Location);
            base.OnMouseDown(e);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Wait == null) return;
            var x = Cols.X(Width);
            Wait.Location = new Point(x[4], (Height - Wait.Height) / 2);
            int ty = (Height - Autorun.Height) / 2;
            Autorun.Location = new Point(x[5], ty);
            Admin.Location = new Point(x[6], ty);
            AutoClose.Location = new Point(x[7], ty);
            Start.Location = new Point(x[8], (Height - Start.Height) / 2);
            int by = (Height - BtnEdit.Height) / 2;
            BtnEdit.Location = new Point(x[9], by);
            BtnDelete.Location = new Point(x[9] + BtnEdit.Width + Theme.S(2), by);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(dragging ? Theme.Selected : BackColor);
            using (var pen = new Pen(Theme.CardBorder)) g.DrawLine(pen, Theme.S(14), 0, Width - Theme.S(14), 0);
            var x = Cols.X(Width);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

            // drag handle + stacking rank (1 = front)
            var badge = new Rectangle(x[0] + Theme.S(2), (Height - Theme.S(22)) / 2, Theme.S(22), Theme.S(22));
            if (hover || dragging)
                Theme.DrawGlyph(g, "\uE76F", 10f, Theme.SubText, badge);          // drag handle
            else
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Theme.FillRound(g, Theme.Raised, badge, Theme.S(11), Theme.CardBorder);
                TextRenderer.DrawText(g, Rank.ToString(), Theme.F8, badge, Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }

            int t = Theme.S(34);
            Theme.DrawAppIcon(g, App, new Rectangle(x[1], (Height - t) / 2, t, t));
            TextRenderer.DrawText(g, App.Name, Theme.F10S, new Rectangle(x[1] + t + Theme.S(12), 0, x[2] - x[1] - t - Theme.S(24), Height), Theme.Text, flags);
            string path = string.IsNullOrWhiteSpace(App.Path) ? "No program path" : App.Path;
            TextRenderer.DrawText(g, path, Theme.F9, new Rectangle(x[2], 0, x[3] - x[2] - Theme.S(16), Height), string.IsNullOrWhiteSpace(App.Path) ? Theme.Orange : Theme.Muted, flags | TextFormatFlags.PathEllipsis);
            if (App.HasPos)
                TextRenderer.DrawText(g, Util.Placement(App), Theme.F9, new Rectangle(x[3], 0, x[4] - x[3] - Theme.S(12), Height), Theme.SubText, flags);
            else
            {
                // call to action: grab the position straight from the open window
                var gr = GrabRect();
                if (linkHot) { g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; Theme.FillRound(g, Theme.Alpha(Theme.Accent, 40), gr, Theme.S(7)); }
                int nw = TextRenderer.MeasureText(g, "Not set · ", Theme.F9, Size.Empty, TextFormatFlags.NoPadding).Width;
                TextRenderer.DrawText(g, "Not set · ", Theme.F9, new Rectangle(x[3], 0, nw + 2, Height), Theme.Orange, flags);
                TextRenderer.DrawText(g, "Grab position", linkHot ? LinkFont : Theme.F9S,
                    new Rectangle(x[3] + nw, 0, x[4] - x[3] - nw - Theme.S(8), Height), Theme.AccentInk, flags);
            }
            TextRenderer.DrawText(g, "s", Theme.F9, new Rectangle(x[4] + Theme.S(54), 0, Theme.S(20), Height), Theme.Muted, flags);
        }
    }

    class AppTile : Owner
    {
        public readonly AppEntry App;
        string status = "";
        Color dot = Theme.Faint;

        public AppTile(AppEntry a) { App = a; Size = new Size(Theme.S(252), Theme.S(70)); Cursor = Cursors.Hand; }

        bool btnHot;
        public bool DownOnPosition { get; private set; }
        bool Compact { get { return Height < Theme.S(56) || Width < Theme.S(230); } }

        /// <summary>"Position" button on the right side of the tile.</summary>
        Rectangle PosRect()
        {
            int h = Math.Min(Theme.S(30), Height - Theme.S(16));
            int w = Compact ? h : Theme.S(86);
            return new Rectangle(Width - w - Theme.S(10), (Height - h) / 2, w, h);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hot = PosRect().Contains(e.Location);
            if (hot != btnHot) { btnHot = hot; Invalidate(); tip.SetToolTip(this, hot ? "Move " + App.Name + " to its saved position" : App.Name + " — " + status); }
        }
        protected override void OnMouseLeave(EventArgs e) { btnHot = false; base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            DownOnPosition = e.Button == MouseButtons.Left && PosRect().Contains(e.Location);
            base.OnMouseDown(e);
        }

        public void SetStatus(string s)
        {
            Color c;
            switch (s)
            {
                case "Running": case "Ready": c = Theme.Green; break;
                case "Starting": case "Positioning": case "Closing": c = Theme.Orange; break;
                case "Waiting for sim": case "Waiting for flight": c = Theme.Purple; break;
                case "Error": case "Not found": case "Needs admin": c = Theme.Red; break;
                case "No position yet": case "In tray": c = Theme.Orange; break;
                default: c = s.StartsWith("Starting in") ? Theme.Purple : Theme.Faint; break;   // opens later (START column)
            }
            if (!App.Enabled) { s = "Autorun off"; c = Theme.Faint; }
            if (s != status || c != dot) { status = s; dot = c; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, hover ? Theme.Hover : Theme.Raised, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(10), Theme.CardBorder);
            var flags = TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            int pad = Math.Max(Theme.S(8), Height / 5);
            int t = Math.Min(Theme.S(38), Height - 2 * Math.Max(Theme.S(6), Height / 6));
            Theme.DrawAppIcon(g, App, new Rectangle(pad, (Height - t) / 2, t, t));
            int tx = pad + t + Theme.S(10);
            float d = Theme.S(8);

            // Position button
            var pr = PosRect();
            Color bfg = btnHot ? Theme.AccentText : Theme.SubText;
            Theme.FillRound(g, btnHot ? Theme.Accent : Theme.Card, pr, Theme.S(8), btnHot ? (Color?)null : Theme.CardBorder);
            if (Compact) Theme.DrawGlyph(g, Theme.Glyph.Target, 10f, bfg, pr);
            else
            {
                Theme.DrawGlyph(g, Theme.Glyph.Target, 9f, bfg, new Rectangle(pr.X + Theme.S(8), pr.Y, Theme.S(16), pr.Height));
                TextRenderer.DrawText(g, "Position", Theme.F9S, new Rectangle(pr.X + Theme.S(27), pr.Y, pr.Width - Theme.S(29), pr.Height), bfg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
            int textR = pr.Left - Theme.S(8);

            using (var b = new SolidBrush(dot))
            {
                if (Height >= Theme.S(56))
                {
                    // name + status on two lines
                    int mid = Height / 2;
                    TextRenderer.DrawText(g, App.Name, Theme.F10S, new Rectangle(tx, mid - Theme.S(22), textR - tx, Theme.S(22)),
                        App.Enabled ? Theme.Text : Theme.Muted, flags);
                    g.FillEllipse(b, tx, mid + Theme.S(10) - d / 2, d, d);
                    TextRenderer.DrawText(g, status, Theme.F9, new Rectangle(tx + Theme.S(14), mid, textR - tx - Theme.S(14), Theme.S(20)), Theme.Muted, flags);
                }
                else
                {
                    // compact: status dot + name on one line (full status in the tooltip)
                    g.FillEllipse(b, tx, (Height - d) / 2, d, d);
                    TextRenderer.DrawText(g, App.Name, Theme.F9S, new Rectangle(tx + Theme.S(14), 0, textR - tx - Theme.S(14), Height),
                        App.Enabled ? Theme.Text : Theme.Muted, flags);
                }
            }
        }

        static readonly ToolTip tip = new ToolTip();
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); tip.SetToolTip(this, App.Name + " — " + status); }
    }
    /// <summary>
    /// First-run guide shown where the app list would be (Dashboard and Apps page) while the
    /// profile has no apps: what to do, in order, with the buttons that do it.
    /// </summary>
    class SetupGuide : Panel
    {
        readonly Label title, footer;
        readonly List<Control[]> steps = new List<Control[]>();   // badge, heading, text
        public readonly FlatButton BtnCapture, BtnAdd;

        static readonly string[,] Text3 =
        {
            { "Open your add-ons", "Start every app you use in a flight: charts, ATC, flight tracker, VA client, tools…" },
            { "Put each window in its place", "Move and resize them on your monitors exactly how you like them. Snapping (Win + Z) works too." },
            { "Save the layout", "Click Capture windows and tick your apps: FlightDeck saves each one with its position. Added an app another way? Use Grab position on the Apps page." },
        };

        public SetupGuide()
        {
            BackColor = Theme.Card;
            title = Theme.MakeLabel("Set up your first flight", Theme.F11S, Theme.Text, Theme.Card);
            Controls.Add(title);
            for (int i = 0; i < 3; i++)
            {
                var badge = new StepBadge(i + 1);
                var h = Theme.MakeLabel(Text3[i, 0], Theme.F10S, Theme.Text, Theme.Card);
                var d = Theme.MakeLabel(Text3[i, 1], Theme.F9, Theme.Muted, Theme.Card);
                d.AutoSize = false;
                Controls.AddRange(new Control[] { badge, h, d });
                steps.Add(new Control[] { badge, h, d });
            }
            BtnCapture = new FlatButton("Capture windows", Theme.Glyph.Windows, BtnStyle.Primary, 38);
            BtnAdd = new FlatButton("Add an app manually", Theme.Glyph.Add, BtnStyle.Normal, 38);
            footer = Theme.MakeLabel("Then click Start Flight: everything opens and goes right back into place. You can also drag a program or shortcut into this window.", Theme.F9, Theme.Muted, Theme.Card);
            footer.AutoSize = false;
            Controls.AddRange(new Control[] { BtnCapture, BtnAdd, footer });
        }

        /// <summary>Height the guide needs at a given width.</summary>
        public int HeightFor(int width)
        {
            int lineW = Math.Max(Theme.S(200), width - Theme.S(44));
            int y = Theme.S(40);
            for (int i = 0; i < 3; i++) y += Theme.S(24) + TextH(Text3[i, 1], lineW) + Theme.S(14);
            y += Theme.S(4) + Theme.S(38) + Theme.S(12) + TextH(footer.Text, width) + Theme.S(4);
            return y;
        }

        static int TextH(string text, int width)
        {
            return TextRenderer.MeasureText(text, Theme.F9, new Size(width, int.MaxValue), TextFormatFlags.WordBreak).Height + Theme.S(2);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (footer == null) return;
            int w = ClientSize.Width, indent = Theme.S(44), lineW = Math.Max(Theme.S(200), w - indent);
            title.Location = new Point(0, 0);
            int y = Theme.S(40);
            for (int i = 0; i < 3; i++)
            {
                var c = steps[i];
                c[0].Location = new Point(0, y - Theme.S(2));
                c[1].Location = new Point(indent, y);
                int dh = TextH(Text3[i, 1], lineW);
                c[2].SetBounds(indent, y + Theme.S(24), lineW, dh);
                y += Theme.S(24) + dh + Theme.S(14);
            }
            y += Theme.S(4);
            BtnCapture.Location = new Point(indent, y);
            BtnAdd.Location = new Point(BtnCapture.Right + Theme.S(10), y);
            y += Theme.S(38) + Theme.S(12);
            footer.SetBounds(0, y, w, TextH(footer.Text, w));
        }

        /// <summary>Round number badge in the accent colour.</summary>
        class StepBadge : Owner
        {
            readonly int n;
            public StepBadge(int number) { n = number; Size = new Size(Theme.S(28), Theme.S(28)); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(BackColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, 0, 0, Width - 1, Height - 1);
                TextRenderer.DrawText(g, n.ToString(), Theme.F10S, new Rectangle(0, 0, Width, Height), Theme.AccentText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
    /// <summary>
    /// "When does this app open?" — Right away / After N s / Sim ready / Flight loaded.
    /// Choosing "After…" asks for the number of seconds.
    /// </summary>
    class StartPicker : DarkCombo
    {
        static readonly string[] Modes = { "now", "delay", "sim", "flight" };
        readonly AppEntry app;
        bool loading;
        int last;
        public event Action Changed;

        public StartPicker(AppEntry a)
        {
            app = a;
            Font = Theme.F9;
            ItemHeight = Theme.S(24);
            loading = true;
            Items.AddRange(new object[] { "Right away", DelayText(), "Sim ready", "Flight loaded" });
            last = Math.Max(0, Array.IndexOf(Modes, a.StartMode));
            SelectedIndex = last;
            loading = false;
        }

        string DelayText() { return app.StartMode == "delay" ? "After " + app.StartDelay + " s" : "After a delay…"; }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            if (loading || SelectedIndex < 0) return;
            int i = SelectedIndex;
            if (i == 1)
            {
                // ask after the drop-down has closed
                BeginInvoke(new Action(AskDelay));
                return;
            }
            last = i;
            app.StartMode = Modes[i];
            Refill();
            var h = Changed; if (h != null) h();
        }

        void AskDelay()
        {
            string v = PromptDialog.Ask(FindForm(), "Open after a delay",
                "Seconds after you click Start Flight (e.g. 120 to open the sim 2 minutes later):",
                (app.StartDelay > 0 ? app.StartDelay : 60).ToString());
            int sec;
            if (v != null && int.TryParse(v.Trim().TrimEnd('s', 'S').Trim(), out sec) && sec > 0 && sec <= 3600)
            {
                app.StartMode = "delay";
                app.StartDelay = sec;
                last = 1;
                Refill();
                var h = Changed; if (h != null) h();
            }
            else { loading = true; SelectedIndex = last; loading = false; }
        }

        void Refill()
        {
            loading = true;
            Items[1] = DelayText();
            SelectedIndex = last;
            loading = false;
            Invalidate();
        }
    }
}

