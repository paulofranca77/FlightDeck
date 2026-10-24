using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FlightDeck
{
    static class Theme
    {
        public static float Scale = 1f;
        public static int S(float v) { return (int)Math.Round(v * Scale); }

        public static bool IsDark = true;

        // Palette (filled by Apply). Accent is the same in both modes.
        public static Color Sidebar, Bg, Card, CardBorder, Raised, Hover, Selected, Input, InputBorder,
            Text, SubText, Muted, Faint,
            Accent, AccentHover, AccentPressed, AccentDisabled, AccentText, AccentInk,
            Green, Red, Orange, Purple,
            ButtonHover, ButtonPressed, DangerHoverFill, DangerHoverText,
            ToggleOff, ToggleOffHover, KnobOff,
            NavHover, RowHover, SidebarLine, HeaderLine, MonitorFill, MonitorBorder;

        static Theme() { Apply(true); }

        public static readonly Color DefaultAccent = Color.FromArgb(0x9F, 0xCC, 0x2E);
        public static Color BaseAccent = DefaultAccent;

        // ---- color math ----
        static double Lum(Color c)
        {
            Func<int, double> f = v => { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); };
            return 0.2126 * f(c.R) + 0.7152 * f(c.G) + 0.0722 * f(c.B);
        }
        public static double Contrast(Color a, Color b)
        {
            double x = Lum(a), y = Lum(b);
            return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
        }
        public static Color Mix(Color a, Color b, double t)   // t = share of b
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));
        }
        /// <summary>Moves c toward white or black until it reaches the contrast ratio against bg.</summary>
        static Color Readable(Color c, Color bg, double ratio)
        {
            if (Contrast(c, bg) >= ratio) return c;
            Color target = Lum(bg) < 0.4 ? Color.White : Color.Black;
            for (double t = 0.05; t <= 1.0; t += 0.05)
            {
                var m = Mix(c, target, t);
                if (Contrast(m, bg) >= ratio) return m;
            }
            return target;
        }
        public static string ToHex(Color c) { return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
        public static bool TryHex(string s, out Color c)
        {
            c = Color.Empty;
            s = (s ?? "").Trim().TrimStart('#');
            if (s.Length == 3) s = "" + s[0] + s[0] + s[1] + s[1] + s[2] + s[2];
            int v;
            if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out v)) return false;
            c = Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
            return true;
        }

        public static void Apply(bool dark)
        {
            IsDark = dark;
            var a = BaseAccent;
            Accent = a;
            AccentHover = Mix(a, Color.White, 0.18);
            AccentPressed = Mix(a, Color.Black, 0.12);
            // text drawn ON the accent: a very dark tint of it, or white — whichever reads better
            var darkInk = Mix(a, Color.Black, 0.88);
            AccentText = Contrast(darkInk, a) >= Contrast(Color.White, a) ? darkInk : Color.White;
            if (dark)
            {
                Sidebar = Hex("0D0E10"); Bg = Hex("131417"); Card = Hex("18191D"); CardBorder = Hex("26272D");
                Raised = Hex("1E1F24"); Hover = Hex("212228"); Selected = Hex("23242A");
                Input = Hex("0F1013"); InputBorder = Hex("2D2E35");
                Text = Hex("F1F1F3"); SubText = Hex("C6C8CE"); Muted = Hex("8A8C94"); Faint = Hex("6E7079");
                Green = Hex("4CC38A"); Red = Hex("E5484D"); Orange = Hex("F2A23A"); Purple = Hex("A98BFF");
                ButtonHover = Hex("26272D"); ButtonPressed = Hex("2A2B31");
                DangerHoverFill = Hex("2A1A1C"); DangerHoverText = Hex("FF8A8D");
                ToggleOff = Hex("2C2D33"); ToggleOffHover = Hex("34353C"); KnobOff = Hex("9A9CA4");
                NavHover = Hex("17181B"); RowHover = Hex("1C1D22");
                SidebarLine = Hex("1C1D21"); HeaderLine = Hex("1F2024");
                MonitorFill = Hex("0F1012"); MonitorBorder = Hex("3A3B42");
            }
            else
            {
                Sidebar = Hex("EEEBE4"); Bg = Hex("F5F3EE"); Card = Hex("FEFDFA"); CardBorder = Hex("E1DDD3");
                Raised = Hex("F1EFE9"); Hover = Hex("ECE9E2"); Selected = Hex("E3E0D6");
                Input = Hex("FFFFFF"); InputBorder = Hex("D3CEC3");
                Text = Hex("1D1F1A"); SubText = Hex("3B3E37"); Muted = Hex("62655D"); Faint = Hex("7A7D74");
                Green = Hex("2F8A57"); Red = Hex("C2343A"); Orange = Hex("A85D00"); Purple = Hex("6E46D0");
                ButtonHover = Hex("E9E6DE"); ButtonPressed = Hex("E1DDD3");
                DangerHoverFill = Hex("FBE8E7"); DangerHoverText = Hex("B4232A");
                ToggleOff = Hex("CFCAC0"); ToggleOffHover = Hex("C2BDB2"); KnobOff = Hex("FFFFFF");
                NavHover = Hex("E6E2DA"); RowHover = Hex("F6F4EF");
                SidebarLine = Hex("DDD9CF"); HeaderLine = Hex("E3DFD6");
                MonitorFill = Hex("F0EDE6"); MonitorBorder = Hex("BEB9AD");
            }
            AccentDisabled = Mix(a, Bg, 0.6);
            AccentInk = Readable(a, Card, 4.5);      // accent-coloured text/icons on cards stay legible
        }

        static readonly Color[] tilePalette = { Hex("2F6FE0"), Hex("12808F"), Hex("1E8A57"), Hex("6B4BD6"), Hex("CF7426"), Hex("BC3F78"), Hex("3E4350"), Hex("8A6D1C") };
        public static Color TileColor(string key)
        {
            int h = 0; foreach (char c in key ?? "") h = h * 31 + c;
            return tilePalette[Math.Abs(h % tilePalette.Length)];
        }

        public static Color Hex(string h)
        {
            return Color.FromArgb(Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16));
        }
        public static Color Alpha(Color c, int a) { return Color.FromArgb(a, c); }

        public static readonly Font F8 = new Font("Segoe UI Semibold", 7.75f);
        public static readonly Font F9 = new Font("Segoe UI", 9f);
        public static readonly Font F9S = new Font("Segoe UI Semibold", 9f);
        public static readonly Font F10 = new Font("Segoe UI", 10f);
        public static readonly Font F10S = new Font("Segoe UI Semibold", 10f);
        public static readonly Font F11S = new Font("Segoe UI Semibold", 11f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 17f);

        /// <summary>The FlightDeck wordmark in the brand font; when fitWidth &gt; 0 it is scaled to exactly that width.</summary>
        public static void DrawBrand(Graphics g, string text, RectangleF r, Color c, float fitWidth)
        {
            if (Fonts.Cal == null)
            {
                // brand font couldn't be loaded: plain heavy system font instead of nothing
                using (var f = new Font("Segoe UI Black", 15f))
                    TextRenderer.DrawText(g, text, f, Rectangle.Round(r), c, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
                return;
            }
            float em = 16f * g.DpiY / 72f;
            using (var path = new GraphicsPath(FillMode.Winding))
            {
                path.AddString(text, Fonts.Cal, (int)FontStyle.Regular, em, PointF.Empty, StringFormat.GenericTypographic);
                var b = path.GetBounds();
                using (var m = new Matrix())
                {
                    float k = fitWidth > 0 && b.Width > 0 ? fitWidth / b.Width : 1f;
                    m.Translate(r.X, r.Y + (r.Height - b.Height * k) / 2f);
                    m.Scale(k, k);
                    m.Translate(-b.X, -b.Y);
                    path.Transform(m);
                }
                var sm = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new SolidBrush(c)) g.FillPath(br, path);
                g.SmoothingMode = sm;
            }
        }

        static string iconFamily;
        public static Font Icons(float size)
        {
            if (iconFamily == null)
            {
                iconFamily = "Segoe MDL2 Assets";
                try { using (var f = new Font("Segoe Fluent Icons", 10)) if (f.Name == "Segoe Fluent Icons") iconFamily = f.Name; } catch { }
            }
            return new Font(iconFamily, size);
        }

        public static class Glyph
        {
            public const string Home = "", Apps = "", Monitor = "", Settings = "",
                Add = "", Delete = "", Edit = "",
                View = "", Refresh = "", Folder = "", Windows = "", Save = "",
                Link = "", Target = "", Shield = "";
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color fill, RectangleF r, float rad, Color? border = null)
        {
            using (var path = Round(r, rad))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border.HasValue) using (var pen = new Pen(border.Value, 1f)) g.DrawPath(pen, path);
            }
        }

        public static void DrawGlyph(Graphics g, string glyph, float size, Color color, Rectangle r)
        {
            using (var f = Icons(size))
                TextRenderer.DrawText(g, glyph, f, r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        /// <summary>App tile with initials, like a small app icon.</summary>
        public static void DrawTile(Graphics g, string name, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            FillRound(g, TileColor(name), r, r.Width * 0.22f);
            TextRenderer.DrawText(g, Util.Initials(name), F9S, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        /// <summary>FlightDeck logo: a yellow tile with a snap layout inside.</summary>
        public static void DrawLogo(Graphics g, RectangleF r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            FillRound(g, Accent, r, r.Width * 0.24f);
            float m = r.Width * 0.2f, gap = r.Width * 0.07f, rad = r.Width * 0.06f;
            var inner = new RectangleF(r.X + m, r.Y + m, r.Width - 2 * m, r.Height - 2 * m);
            float lw = inner.Width * 0.56f;
            FillRound(g, AccentText, new RectangleF(inner.X, inner.Y, lw - gap / 2, inner.Height), rad);
            float rx = inner.X + lw + gap / 2, rw = inner.Right - rx, hh = (inner.Height - gap) / 2;
            FillRound(g, AccentText, new RectangleF(rx, inner.Y, rw, hh), rad);
            FillRound(g, AccentText, new RectangleF(rx, inner.Y + hh + gap, rw, hh), rad);
        }

        /// <summary>Crisp, accent-colored logo icon at an exact pixel size (tray).</summary>
        public static Icon MakeLogoIcon(int size)
        {
            using (var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    float m = size <= 16 ? 0.5f : 1f;
                    DrawLogo(g, new RectangleF(m, m, size - 2 * m, size - 2 * m));
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        static readonly int[] IcoSizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

        /// <summary>Builds a multi-size .ico (PNG entries) from a painter.</summary>
        static byte[] BuildIco(Action<Graphics, int> paint)
        {
            var images = new System.Collections.Generic.List<byte[]>();
            float oldScale = Scale; Scale = 1f;
            try
            {
                foreach (int s in IcoSizes)
                    using (var bmp = new Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.Transparent);
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            paint(g, s);
                        }
                        using (var ms = new System.IO.MemoryStream()) { bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png); images.Add(ms.ToArray()); }
                    }
            }
            finally { Scale = oldScale; }
            using (var ms = new System.IO.MemoryStream())
            using (var w = new System.IO.BinaryWriter(ms))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)IcoSizes.Length);
                int offset = 6 + 16 * IcoSizes.Length;
                for (int i = 0; i < IcoSizes.Length; i++)
                {
                    w.Write((byte)(IcoSizes[i] >= 256 ? 0 : IcoSizes[i])); w.Write((byte)(IcoSizes[i] >= 256 ? 0 : IcoSizes[i]));
                    w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(images[i].Length); w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) w.Write(img);
                w.Flush();
                return ms.ToArray();
            }
        }

        static byte[] LogoIco()
        {
            return BuildIco((g, s) => { float m = s <= 20 ? 0.5f : s * 0.04f; DrawLogo(g, new RectangleF(m, m, s - 2 * m, s - 2 * m)); });
        }

        static byte[] FlightIco(bool takeoff)
        {
            return BuildIco((g, s) =>
            {
                float m = s <= 20 ? 0.5f : s * 0.04f;
                var tile = new RectangleF(m, m, s - 2 * m, s - 2 * m);
                FillRound(g, Accent, tile, tile.Width * 0.24f);
                float pad = tile.Width * (s <= 20 ? 0.10f : 0.16f);
                var inner = Rectangle.Round(new RectangleF(tile.X + pad, tile.Y + pad, tile.Width - 2 * pad, tile.Height - 2 * pad));
                DrawPlane(g, inner, AccentText, takeoff, 10000, true);
            });
        }

        static void WriteFile(string path, byte[] data)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, data);
        }

        /// <summary>Shortcut icon: accent tile with a plane taking off (or landing).</summary>
        public static void SaveFlightIcon(string path, bool takeoff) { WriteFile(path, FlightIco(takeoff)); }

        /// <summary>The FlightDeck logo as an .ico file, in the current accent colour.</summary>
        public static void SaveLogoIcon(string path) { WriteFile(path, LogoIco()); }

        /// <summary>Window/taskbar icon in the current accent colour.</summary>
        public static Icon AppIcon()
        {
            try { return new Icon(new System.IO.MemoryStream(LogoIco())); } catch { return null; }
        }

        /// <summary>App's real icon when available, otherwise a colored tile with initials.</summary>
        public static void DrawAppIcon(Graphics g, AppEntry a, Rectangle r)
        {
            var bmp = AppIcons.Get(a, r.Width);
            if (bmp == null) { DrawTile(g, a.Name, r); return; }
            var im = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(bmp, r);
            g.InterpolationMode = im;
        }

        /// <summary>Little airplane over a runway line: nose up (takeoff) or nose down (landing).</summary>
        public static void DrawPlane(Graphics g, Rectangle area, Color c, bool takeoff) { DrawPlane(g, area, c, takeoff, 21, true); }

        public static void DrawPlane(Graphics g, Rectangle area, Color c, bool takeoff, float maxSize, bool runway)
        {
            float sz = Math.Min(area.Height, S(maxSize));
            var box = new RectangleF(area.X + (area.Width - sz) / 2f, area.Y + (area.Height - sz) / 2f, sz, sz);
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // side view of an airliner, nose to +X, unit size (fuselage, fin, wing, stabilizer)
            var shapes = new[]
            {
                new[] { -0.96f,-0.02f, -0.86f,-0.11f, 0.70f,-0.11f, 0.86f,-0.09f, 0.97f,-0.03f, 1.00f,0.01f, 0.95f,0.06f, 0.82f,0.09f, -0.62f,0.09f },
                new[] { -0.60f,-0.10f, -0.86f,-0.56f, -0.99f,-0.56f, -0.90f,-0.10f },
                new[] { 0.22f,0.04f, -0.24f,0.58f, -0.42f,0.58f, -0.10f,0.04f },
                new[] { -0.68f,0.02f, -0.90f,0.22f, -1.00f,0.22f, -0.86f,0.00f },
            };
            using (var path = new GraphicsPath(FillMode.Winding))
            {
                foreach (var sh in shapes)
                {
                    var pts = new PointF[sh.Length / 2];
                    for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(sh[i * 2], sh[i * 2 + 1]);
                    path.AddPolygon(pts);
                }
                using (var m = new Matrix())
                {
                    m.Translate(box.X + sz * 0.50f, box.Y + sz * 0.46f);
                    m.Rotate(takeoff ? -24f : 22f);
                    m.Scale(sz * 0.46f, sz * 0.46f);
                    path.Transform(m);
                }
                using (var b = new SolidBrush(c)) g.FillPath(b, path);
            }
            if (runway) using (var pen = new Pen(c, Math.Max(1.5f, sz * 0.09f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, box.X + sz * 0.04f, box.Bottom - sz * 0.06f, box.Right - sz * 0.04f, box.Bottom - sz * 0.06f);
            g.SmoothingMode = sm;
        }

        public static Label MakeLabel(string text, Font font, Color color, Color back)
        {
            return new Label { Text = text, Font = font, ForeColor = color, BackColor = back, AutoSize = true, UseMnemonic = false };
        }
    }

    /// <summary>Base for owner-drawn controls.</summary>
    class Owner : Control
    {
        public Owner()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
        }
        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (Parent != null) BackColor = Parent.BackColor;
        }
        protected bool hover, down;
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    }

    enum BtnStyle { Normal, Primary, Ghost, Danger }

    class FlatButton : Owner
    {
        public string Glyph;
        public Action<Graphics, Rectangle, Color> Painter;   // custom icon instead of a glyph
        public BtnStyle Style;

        public FlatButton(string text, string glyph = null, BtnStyle style = BtnStyle.Normal, float height = 38)
        {
            Text = text; Glyph = glyph; Style = style;
            Font = Theme.F9S;
            Cursor = Cursors.Hand;
            Height = Theme.S(height);
            FitWidth();
        }

        public void FitWidth()
        {
            bool hasText = !string.IsNullOrEmpty(Text);
            if (!hasText) { Width = Height; return; }
            int w = Theme.S(18) * 2 + TextRenderer.MeasureText(Text, Font).Width;
            if (Painter != null) w += Theme.S(29); else if (Glyph != null) w += Theme.S(24);
            Width = w;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fill, fg; Color? border = null;
            switch (Style)
            {
                case BtnStyle.Primary:
                    fill = !Enabled ? Theme.AccentDisabled : (down ? Theme.AccentPressed : hover ? Theme.AccentHover : Theme.Accent);
                    fg = Theme.AccentText; break;
                case BtnStyle.Ghost:
                    fill = hover ? Theme.Hover : BackColor;
                    fg = !Enabled ? Theme.Faint : (hover ? Theme.Text : Theme.Muted); break;
                case BtnStyle.Danger:
                    fill = hover ? Theme.DangerHoverFill : Theme.Raised; border = hover ? Theme.Red : Theme.CardBorder;
                    fg = !Enabled ? Theme.Faint : (hover ? Theme.DangerHoverText : Theme.Text); break;
                default:
                    fill = down ? Theme.ButtonPressed : hover ? Theme.ButtonHover : Theme.Raised; border = Theme.CardBorder;
                    fg = Enabled ? Theme.Text : Theme.Faint; break;
            }
            Theme.FillRound(g, fill, r, Theme.S(Style == BtnStyle.Ghost ? 8 : 10), border);

            bool hasText = !string.IsNullOrEmpty(Text);
            bool hasIcon = Glyph != null || Painter != null;
            int gw = Painter != null ? Theme.S(22) : Glyph != null ? Theme.S(16) : 0;
            int gap = hasIcon && hasText ? Theme.S(7) : 0;
            int tw = hasText ? TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width : 0;
            int x = (Width - gw - gap - tw) / 2;
            if (Painter != null) Painter(g, new Rectangle(x, 0, gw, Height), fg);
            else if (Glyph != null) Theme.DrawGlyph(g, Glyph, hasText ? 10f : 11f, fg, new Rectangle(x, 0, gw, Height));
            if (hasText)
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x + gw + gap, 0, tw + 4, Height), fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.Left);
        }
    }

    class Toggle : Owner
    {
        bool on;
        public event EventHandler CheckedChanged;
        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; Invalidate(); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); }
        }
        public Toggle()
        {
            Size = new Size(Theme.S(38), Theme.S(22));
            Cursor = Cursors.Hand;
        }
        protected override void OnClick(EventArgs e) { base.OnClick(e); if (Enabled) Checked = !Checked; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color track = on ? (hover ? Theme.AccentHover : Theme.Accent) : (hover ? Theme.ToggleOffHover : Theme.ToggleOff);
            if (!Enabled) track = Theme.Alpha(track, 90);
            Theme.FillRound(g, track, r, r.Height / 2);
            float d = Height - Theme.S(7);
            float y = (Height - d) / 2f - 0.5f;
            float x = on ? Width - d - Theme.S(4) : Theme.S(3.5f);
            using (var b = new SolidBrush(on ? Color.White : Theme.KnobOff)) g.FillEllipse(b, x, y, d, d);
        }
    }

    class NavButton : Owner
    {
        public string Glyph;
        bool sel;
        public bool Selected { get { return sel; } set { sel = value; Invalidate(); } }

        public NavButton(string text, string glyph)
        {
            Text = text; Glyph = glyph; Font = Theme.F10; Cursor = Cursors.Hand; Height = Theme.S(42);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (sel || hover)
                Theme.FillRound(g, sel ? Theme.Selected : Theme.NavHover, new RectangleF(Theme.S(12), 1, Width - Theme.S(24), Height - 2), Theme.S(9));
            Color fg = sel ? Theme.Text : (hover ? Theme.Text : Theme.SubText);
            Theme.DrawGlyph(g, Glyph, 11.5f, sel ? Theme.AccentInk : fg, new Rectangle(Theme.S(24), 0, Theme.S(22), Height));
            TextRenderer.DrawText(g, Text, sel ? Theme.F10S : Theme.F10, new Rectangle(Theme.S(58), 0, Width - Theme.S(62), Height), fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Rounded card container.</summary>
    class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, Theme.Card, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(12), Theme.CardBorder);
        }
    }

    /// <summary>Dark rounded text input.</summary>
    class TextField : Panel
    {
        public readonly TextBox Box;
        public event EventHandler ValueChanged;

        public TextField(string value = "")
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Input;
            Height = Theme.S(36);
            Box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, Font = Theme.F10, Text = value };
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Box.TextChanged += (s, e) => { var h = ValueChanged; if (h != null) h(this, EventArgs.Empty); };
            Cursor = Cursors.IBeam;
            Click += (s, e) => Box.Focus();
        }

        public string Value { get { return Box.Text; } set { Box.Text = value ?? ""; } }

        public int IntValue(int fallback = 0)
        {
            int v; return int.TryParse(Box.Text.Trim(), out v) ? v : fallback;
        }

        public double DoubleValue(double fallback)
        {
            double v;
            return double.TryParse(Box.Text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Box == null) return;
            Box.Location = new Point(Theme.S(11), (Height - Box.Height) / 2);
            Box.Width = Math.Max(10, Width - Theme.S(22));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, Theme.Input, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(9),
                Box != null && Box.Focused ? Theme.Alpha(Theme.Accent, 200) : Theme.InputBorder);
        }
    }

    /// <summary>Dark drop-down list.</summary>
    class DarkCombo : ComboBox
    {
        public DarkCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Input;
            ForeColor = Theme.Text;
            Font = Theme.F10;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = Theme.S(26);
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.Selected : Theme.Input)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, new Rectangle(e.Bounds.X + Theme.S(6), e.Bounds.Y, e.Bounds.Width - Theme.S(6), e.Bounds.Height),
                Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Dark form base: dark title bar, no auto scaling.</summary>
    class DarkForm : Form
    {
        public DarkForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.F10;
            try { Icon = Theme.AppIcon() ?? Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitleBar(Handle);
        }
    }

    /// <summary>Translucent yellow rectangle that shows where a window will go.</summary>
    class Overlay : Form
    {
        readonly string label;
        public Overlay(Rectangle bounds, string text)
        {
            label = text;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Accent;
            Opacity = 0.42;
            Bounds = bounds;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            var t = new Timer { Interval = 2600 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); Close(); };
            Shown += (s, e) => t.Start();
            Click += (s, e) => Close();
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/; return cp; }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var pen = new Pen(Theme.AccentText, Theme.S(6))) g.DrawRectangle(pen, Theme.S(3), Theme.S(3), Width - Theme.S(6), Height - Theme.S(6));
            using (var f = new Font("Segoe UI Semibold", 28f))
                TextRenderer.DrawText(g, label, f, ClientRectangle, Theme.AccentText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        public static void Flash(AppEntry a)
        {
            if (!a.HasPos) return;
            Rectangle r;
            if (a.Maximized)
                r = Util.Screens()[Util.MonitorIndex(new Rectangle(a.X, a.Y, Math.Max(1, a.W), Math.Max(1, a.H)))].WorkingArea;
            else r = new Rectangle(a.X, a.Y, Math.Max(40, a.W), Math.Max(40, a.H));
            new Overlay(r, a.Name).Show();
        }
    }

    class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Raised; } }
        public override Color MenuBorder { get { return Theme.CardBorder; } }
        public override Color MenuItemBorder { get { return Theme.Selected; } }
        public override Color MenuItemSelected { get { return Theme.Selected; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Raised; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Raised; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Raised; } }
        public override Color SeparatorDark { get { return Theme.CardBorder; } }
        public override Color SeparatorLight { get { return Theme.CardBorder; } }
    }

    class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = Theme.Text; base.OnRenderItemText(e); }
    }

    /// <summary>Two-or-more option switch (e.g. Dark | Light).</summary>
    class Segmented : Owner
    {
        readonly string[] options;
        int sel, hot = -1;
        public event EventHandler SelectedChanged;

        public Segmented(params string[] opts)
        {
            options = opts;
            Font = Theme.F9S;
            Size = new Size(Theme.S(92) * opts.Length + Theme.S(8), Theme.S(36));
            Cursor = Cursors.Hand;
        }

        public int Selected
        {
            get { return sel; }
            set { if (sel == value) return; sel = value; Invalidate(); var h = SelectedChanged; if (h != null) h(this, EventArgs.Empty); }
        }
        public void SetSilently(int v) { sel = v; Invalidate(); }

        int SegAt(int x) { return Math.Max(0, Math.Min(options.Length - 1, (x - Theme.S(4)) * options.Length / Math.Max(1, Width - Theme.S(8)))); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = SegAt(e.X); if (h != hot) { hot = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); Selected = SegAt(e.X); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, Theme.Raised, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(10), Theme.CardBorder);
            float segW = (Width - Theme.S(8)) / (float)options.Length;
            for (int i = 0; i < options.Length; i++)
            {
                var r = new RectangleF(Theme.S(4) + i * segW, Theme.S(4), segW, Height - Theme.S(8));
                if (i == sel) Theme.FillRound(g, Theme.Accent, r, Theme.S(7));
                else if (i == hot) Theme.FillRound(g, Theme.ButtonHover, r, Theme.S(7));
                TextRenderer.DrawText(g, options[i], Font, Rectangle.Round(r), i == sel ? Theme.AccentText : Theme.SubText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    /// <summary>Full-window "drop to add" target shown while dragging programs over FlightDeck.</summary>
    class DropOverlay : Owner
    {
        readonly MainForm host;
        public int Count = 1;

        public DropOverlay(MainForm h)
        {
            host = h;
            AllowDrop = true;
            DragEnter += (s, e) => { e.Effect = MainForm.AddableFiles(e.Data).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None; };
            DragOver += (s, e) => { e.Effect = MainForm.AddableFiles(e.Data).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None; };
            DragLeave += (s, e) => host.HideDropOverlay();
            DragDrop += (s, e) =>
            {
                var files = MainForm.AddableFiles(e.Data);
                host.HideDropOverlay();
                if (files.Count > 0) host.BeginInvoke(new Action(() => host.AddFromFiles(files)));
            };
        }
        protected override void OnParentChanged(EventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(Theme.S(28), Theme.S(28), Width - Theme.S(56), Height - Theme.S(56));
            Theme.FillRound(g, Theme.Alpha(Theme.Accent, Theme.IsDark ? 22 : 36), r, Theme.S(18));
            using (var pen = new Pen(Theme.Accent, Theme.S(2.5f)) { DashStyle = DashStyle.Dash, DashPattern = new float[] { 4f, 3f } })
            using (var path = Round(r, Theme.S(18))) g.DrawPath(pen, path);

            int cy = Height / 2;
            float d = Theme.S(72);
            using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, Width / 2f - d / 2, cy - Theme.S(96), d, d);
            DrawGlyph(g, Theme.Glyph.Add, 24f, Theme.AccentText, new Rectangle((int)(Width / 2f - d / 2), cy - Theme.S(96), (int)d, (int)d));
            using (var f = new Font("Segoe UI Semibold", 18f))
                TextRenderer.DrawText(g, Count == 1 ? "Drop to add app" : "Drop to add " + Count + " apps", f,
                    new Rectangle(0, cy - Theme.S(8), Width, Theme.S(40)), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "It joins the profile \"" + host.Cfg.Active.Name + "\". Afterwards, give it a position in Apps or Layout.", F10,
                new Rectangle(0, cy + Theme.S(34), Width, Theme.S(26)), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        static Font F10 { get { return Theme.F10; } }
        static GraphicsPath Round(RectangleF r, float rad) { return Theme.Round(r, rad); }
        static void DrawGlyph(Graphics g, string glyph, float size, Color c, Rectangle r) { Theme.DrawGlyph(g, glyph, size, c, r); }
    }

    /// <summary>FlightDeck's own notification, so it always shows the green branding.</summary>
    class Toast : Form
    {
        static Toast current;
        readonly string title, text;
        readonly Action onClick;
        readonly Timer life;
        bool hot;

        public static void Show(string title, string text, Action onClick)
        {
            try { if (current != null && !current.IsDisposed) current.Close(); } catch { }
            current = new Toast(title, text, onClick);
            current.Show();
        }

        Toast(string title, string text, Action onClick)
        {
            this.title = title; this.text = text; this.onClick = onClick;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            BackColor = Theme.Card;
            Size = new Size(Theme.S(384), Theme.S(108));
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - Theme.S(16), wa.Bottom - Height - Theme.S(16));
            Cursor = Cursors.Hand;
            life = new Timer { Interval = 6500 };
            life.Tick += (s, e) => { if (!hot) { life.Stop(); Close(); } };
            Shown += (s, e) => life.Start();
            MouseEnter += (s, e) => hot = true;
            MouseLeave += (s, e) => hot = false;
            MouseClick += (s, e) =>
            {
                bool closeBtn = e.X > Width - Theme.S(40) && e.Y < Theme.S(40);
                Close();
                if (!closeBtn && onClick != null) onClick();
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/ | 0x8 /*TOPMOST*/; return cp; }
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.RoundCorners(Handle); Native.DarkTitleBar(Handle); }
        protected override void OnFormClosed(FormClosedEventArgs e) { life.Dispose(); base.OnFormClosed(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            using (var pen = new Pen(Theme.CardBorder)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            int pad = Theme.S(16);
            Theme.DrawLogo(g, new RectangleF(pad, pad, Theme.S(40), Theme.S(40)));
            int x = pad + Theme.S(40) + Theme.S(14);
            TextRenderer.DrawText(g, "FlightDeck", Theme.F8, new Point(x, pad - Theme.S(1)), Theme.Muted, TextFormatFlags.NoPadding);
            Theme.DrawGlyph(g, "\uE711", 9f, Theme.Muted, new Rectangle(Width - Theme.S(36), Theme.S(8), Theme.S(28), Theme.S(28)));
            TextRenderer.DrawText(g, title, Theme.F11S, new Point(x, pad + Theme.S(15)), Theme.Text, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, text, Theme.F9, new Rectangle(x, pad + Theme.S(42), Width - x - pad, Height - pad - Theme.S(42) - Theme.S(4)), Theme.SubText,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>"All set" moment shown over the window before FlightDeck goes to the tray.</summary>
    class CelebrationOverlay : Owner
    {
        readonly int apps;
        readonly bool landing;
        readonly Timer timer = new Timer { Interval = 16 };
        DateTime start;
        const double Duration = 2.6;
        public event Action Finished;
        bool done;

        public CelebrationOverlay(int appCount, bool landed = false)
        {
            apps = appCount;
            landing = landed;
            BackColor = Theme.Bg;
            timer.Tick += (s, e) =>
            {
                double t = (DateTime.Now - start).TotalSeconds;
                Invalidate();
                if (t >= Duration && !done) { done = true; timer.Stop(); var f = Finished; if (f != null) f(); }
            };
        }
        protected override void OnParentChanged(EventArgs e) { }
        public void Start() { start = DateTime.Now; timer.Start(); }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }

        static float Ease(float x) { x = Math.Max(0, Math.Min(1, x)); return 1 - (float)Math.Pow(1 - x, 3); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float t = (float)(DateTime.Now - start).TotalSeconds;
            float appear = Ease(t / 0.35f);

            int cx = Width / 2, cy = Height / 2 - Theme.S(40);
            float d = Theme.S(150) * (0.85f + 0.15f * appear);
            var circle = new RectangleF(cx - d / 2, cy - d / 2, d, d);
            using (var b = new SolidBrush(Theme.Alpha(Theme.Accent, (int)(255 * appear)))) g.FillEllipse(b, circle);

            g.SetClip(circle);
            float p = Ease((t - 0.2f) / 1.9f);
            float runwayY = circle.Bottom - d * 0.22f;
            using (var pen = new Pen(Theme.Alpha(Theme.AccentText, 110), Theme.S(4)))
                g.DrawLine(pen, circle.X, runwayY, circle.Right, runwayY);
            float px, py;
            if (!landing)
            {
                // a plane taking off across the circle
                px = circle.X - d * 0.35f + p * d * 1.25f; py = circle.Bottom - d * 0.30f - p * d * 0.55f;
            }
            else
            {
                // a plane coming down from the top left and settling on the runway
                px = circle.X - d * 0.20f + p * d * 0.72f; py = circle.Y + d * 0.10f + p * (runwayY - d * 0.13f - (circle.Y + d * 0.10f));
            }
            Theme.DrawPlane(g, new Rectangle((int)(px - d * 0.25f), (int)(py - d * 0.25f), (int)(d * 0.5f), (int)(d * 0.5f)), Theme.AccentText, !landing, 80, false);
            g.ResetClip();

            int alpha = (int)(255 * Ease((t - 0.15f) / 0.4f));
            string title = landing ? "Welcome back!" : "All set!";
            string sub = landing ? "Landed — your add-ons are closed" : "Have a great flight ✈";
            string small = landing ? apps + " app" + (apps == 1 ? "" : "s") + " closed · see you on the next flight"
                                   : apps + " app" + (apps == 1 ? "" : "s") + " in position · FlightDeck is moving to the tray";
            using (var f = new Font("Segoe UI Semibold", 22f))
                TextRenderer.DrawText(g, title, f, new Rectangle(0, (int)circle.Bottom + Theme.S(22), Width, Theme.S(44)),
                    Blend(Theme.Text, alpha), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, sub, Theme.F11S, new Rectangle(0, (int)circle.Bottom + Theme.S(66), Width, Theme.S(28)),
                Blend(Theme.SubText, alpha), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, small, Theme.F9,
                new Rectangle(0, (int)circle.Bottom + Theme.S(98), Width, Theme.S(22)), Blend(Theme.Muted, alpha), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        static Color Blend(Color c, int a)
        {
            a = Math.Max(0, Math.Min(255, a));
            var bg = Theme.Bg;
            return Color.FromArgb((c.R * a + bg.R * (255 - a)) / 255, (c.G * a + bg.G * (255 - a)) / 255, (c.B * a + bg.B * (255 - a)) / 255);
        }
    }

    /// <summary>Thin animated progress bar with a status label (Activity card).</summary>
    class ProgressStrip : Owner
    {
        float shown, target, step;
        string label = "";
        bool problem;
        readonly Timer anim = new Timer { Interval = 16 };

        public ProgressStrip()
        {
            Height = Theme.S(30);
            anim.Tick += (s, e) =>
            {
                float d = target - shown;
                if (d > 0.002f) shown += d * 0.18f;                         // catch up with real progress
                else if (target < 1f && shown < target + step * 0.85f)
                    shown += step * 0.006f;                                 // drift forward while waiting on the next step
                else if (target >= 1f) { shown = 1f; anim.Stop(); }
                Invalidate();
            };
        }
        protected override void OnParentChanged(EventArgs e) { base.OnParentChanged(e); if (Parent != null) BackColor = Parent.BackColor; }
        protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }

        public void Set(int done, int total, string text, bool hasProblem)
        {
            float v = total <= 0 ? 0 : Math.Max(0, Math.Min(1, done / (float)total));
            if (v < target - 0.001f) shown = v;      // a new run: start from its own value
            target = v; step = total > 0 ? 1f / total : 0; label = text ?? ""; problem = hasProblem;
            anim.Start();
            Invalidate();
        }

        public void Reset() { shown = target = step = 0; label = ""; problem = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int barH = Theme.S(6);
            var track = new RectangleF(0, Height - barH - Theme.S(2), Width - 1, barH);
            FillRound(track, Theme.ToggleOff);
            if (shown > 0.001f)
            {
                var fill = new RectangleF(track.X, track.Y, Math.Max(barH, track.Width * shown), barH);
                FillRound(fill, problem ? Theme.Orange : Theme.Accent);
            }
            string pct = ((int)Math.Floor(Math.Max(target, Math.Min(shown, 0.99f)) * 100)) + "%";
            TextRenderer.DrawText(g, label, Theme.F9, new Rectangle(0, 0, Width - Theme.S(50), Theme.S(18)), Theme.SubText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, pct, Theme.F9S, new Rectangle(Width - Theme.S(50), 0, Theme.S(50), Theme.S(18)), Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            void FillRound(RectangleF r, Color c) { Theme.FillRound(g, c, r, r.Height / 2); }
        }
    }

    /// <summary>Row of preset accent colours plus a "custom" slot that opens the colour picker.</summary>
    class AccentPicker : Owner
    {
        public static readonly Color[] Presets =
        {
            Theme.DefaultAccent, Theme.Hex("2EC4B6"), Theme.Hex("3BA3F5"), Theme.Hex("6C7CFF"),
            Theme.Hex("A178FF"), Theme.Hex("F06292"), Theme.Hex("FF7A59"), Theme.Hex("F5B83D")
        };
        static readonly string[] names = { "Lime (default)", "Teal", "Sky", "Indigo", "Violet", "Pink", "Coral", "Amber" };

        Color current;
        int hot = -1;
        public event Action<Color> Picked;
        readonly ToolTip tip = new ToolTip();

        public AccentPicker(Color c)
        {
            current = c;
            Size = new Size(Slot() * (Presets.Length + 1) + Theme.S(4), Theme.S(34));
            Cursor = Cursors.Hand;
        }
        static int Slot() { return Theme.S(34); }
        int CustomIndex { get { return Presets.Length; } }
        bool IsPreset { get { return Array.Exists(Presets, p => p.ToArgb() == current.ToArgb()); } }

        int At(Point p) { int i = p.X / Slot(); return i >= 0 && i <= CustomIndex ? i : -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = At(e.Location);
            if (h != hot) { hot = h; Invalidate(); tip.SetToolTip(this, h < 0 ? "" : h == CustomIndex ? "Custom colour…" : names[h]); }
        }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = At(e.Location);
            if (i < 0) return;
            Color c;
            if (i == CustomIndex)
            {
                using (var d = new ColorPickerDialog(current))
                {
                    if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                    c = d.Value;
                }
            }
            else c = Presets[i];
            if (c.ToArgb() == current.ToArgb()) return;
            current = c;
            Invalidate();
            var h = Picked; if (h != null) h(c);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int s = Slot(), d = Theme.S(24);
            for (int i = 0; i <= CustomIndex; i++)
            {
                var r = new RectangleF(i * s + (s - d) / 2f, (Height - d) / 2f, d, d);
                bool custom = i == CustomIndex;
                bool selected = custom ? !IsPreset : Presets[i].ToArgb() == current.ToArgb();
                if (selected || i == hot)
                {
                    var ring = RectangleF.Inflate(r, Theme.S(4), Theme.S(4));
                    using (var pen = new Pen(selected ? Theme.Text : Theme.Faint, Theme.S(2))) g.DrawEllipse(pen, ring);
                }
                if (custom)
                {
                    if (!IsPreset) using (var b = new SolidBrush(current)) g.FillEllipse(b, r);
                    else
                    {
                        // little colour wheel
                        using (var path = new GraphicsPath())
                        {
                            path.AddEllipse(r);
                            using (var pgb = new PathGradientBrush(path))
                            {
                                pgb.CenterColor = Color.White;
                                pgb.SurroundColors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta };
                                g.FillEllipse(pgb, r);
                            }
                        }
                    }
                    Theme.DrawGlyph(g, Theme.Glyph.Add, 8f, !IsPreset ? Theme.AccentText : Color.Black, Rectangle.Round(r));
                }
                else using (var b = new SolidBrush(Presets[i])) g.FillEllipse(b, r);
            }
        }
    }

    /// <summary>Colour picker: saturation/brightness square, hue strip, hex and RGB fields, live preview.</summary>
    class ColorPickerDialog : DarkForm
    {
        public Color Value { get; private set; }
        double hue, sat, val;
        readonly SVSquare square;
        readonly HueStrip strip;
        readonly TextField hex, fr, fg, fb;
        readonly Panel preview;
        bool syncing;

        public ColorPickerDialog(Color start)
        {
            Text = "Accent colour";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            int pad = Theme.S(24);
            ClientSize = new Size(Theme.S(560), Theme.S(380));
            Value = start;
            ToHsv(start, out hue, out sat, out val);

            square = new SVSquare(this) { Bounds = new Rectangle(pad, pad, Theme.S(256), Theme.S(256)) };
            strip = new HueStrip(this) { Bounds = new Rectangle(square.Right + Theme.S(14), pad, Theme.S(24), Theme.S(256)) };
            int x = strip.Right + Theme.S(24), w = ClientSize.Width - x - pad;

            var pl = Theme.MakeLabel("Preview", Theme.F9, Theme.Muted, Theme.Bg); pl.Location = new Point(x, pad - Theme.S(2));
            preview = new Panel { Bounds = new Rectangle(x, pad + Theme.S(20), w, Theme.S(64)) };
            preview.Paint += PaintPreview;
            typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(preview, true, null);

            int y = preview.Bottom + Theme.S(16);
            var hl = Theme.MakeLabel("Hex", Theme.F9, Theme.Muted, Theme.Bg); hl.Location = new Point(x, y);
            hex = new TextField("#" + Theme.ToHex(start)) { Location = new Point(x, y + Theme.S(20)), Width = w };
            y += Theme.S(66);
            int q = (w - 2 * Theme.S(8)) / 3;
            var lr = Theme.MakeLabel("R", Theme.F9, Theme.Muted, Theme.Bg); lr.Location = new Point(x, y);
            var lg = Theme.MakeLabel("G", Theme.F9, Theme.Muted, Theme.Bg); lg.Location = new Point(x + q + Theme.S(8), y);
            var lb = Theme.MakeLabel("B", Theme.F9, Theme.Muted, Theme.Bg); lb.Location = new Point(x + 2 * (q + Theme.S(8)), y);
            fr = new TextField(start.R.ToString()) { Location = new Point(x, y + Theme.S(20)), Width = q };
            fg = new TextField(start.G.ToString()) { Location = new Point(lg.Left, y + Theme.S(20)), Width = q };
            fb = new TextField(start.B.ToString()) { Location = new Point(lb.Left, y + Theme.S(20)), Width = q };

            hex.ValueChanged += (s, e) => { Color c; if (!syncing && Theme.TryHex(hex.Value, out c)) SetColor(c, hex); };
            EventHandler rgb = (s, e) =>
            {
                if (syncing) return;
                int r, g2, b;
                if (int.TryParse(fr.Value, out r) && int.TryParse(fg.Value, out g2) && int.TryParse(fb.Value, out b) &&
                    r >= 0 && r <= 255 && g2 >= 0 && g2 <= 255 && b >= 0 && b <= 255)
                    SetColor(Color.FromArgb(r, g2, b), (Control)s);
            };
            fr.ValueChanged += rgb; fg.ValueChanged += rgb; fb.ValueChanged += rgb;

            var ok = new FlatButton("Use colour", null, BtnStyle.Primary, 40);
            var cancel = new FlatButton("Cancel", null, BtnStyle.Normal, 40);
            var reset = new FlatButton("Default", Theme.Glyph.Refresh, BtnStyle.Ghost, 40);
            ok.Location = new Point(ClientSize.Width - pad - ok.Width, ClientSize.Height - pad - ok.Height);
            cancel.Location = new Point(ok.Left - Theme.S(10) - cancel.Width, ok.Top);
            reset.Location = new Point(pad - Theme.S(8), ok.Top);
            ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            reset.Click += (s, e) => SetColor(Theme.DefaultAccent, null);

            Controls.AddRange(new Control[] { square, strip, pl, preview, hl, hex, lr, lg, lb, fr, fg, fb, ok, cancel, reset });
            AcceptButton = null;
        }

        // ---- HSV ----
        public static void ToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            h = 0;
            if (d > 0)
            {
                if (max == r) h = 60 * (((g - b) / d) % 6);
                else if (max == g) h = 60 * ((b - r) / d + 2);
                else h = 60 * ((r - g) / d + 4);
            }
            if (h < 0) h += 360;
            s = max == 0 ? 0 : d / max;
            v = max;
        }

        public static Color FromHsv(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c, r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
        }

        public double Hue { get { return hue; } set { hue = value; FromParts(); } }
        public double Sat { get { return sat; } }
        public double Val { get { return val; } }
        public void SetSV(double s, double v) { sat = Math.Max(0, Math.Min(1, s)); val = Math.Max(0, Math.Min(1, v)); FromParts(); }

        void FromParts() { Value = FromHsv(hue, sat, val); Sync(null); }

        void SetColor(Color c, Control source)
        {
            Value = c;
            double h, s, v; ToHsv(c, out h, out s, out v);
            if (s > 0.001 && v > 0.001) hue = h;   // keep the hue for greys/black
            sat = s; val = v;
            Sync(source);
        }

        void Sync(Control source)
        {
            syncing = true;
            if (source != hex) hex.Value = "#" + Theme.ToHex(Value);
            if (source != fr && source != fg && source != fb) { fr.Value = Value.R.ToString(); fg.Value = Value.G.ToString(); fb.Value = Value.B.ToString(); }
            syncing = false;
            square.Invalidate(); strip.Invalidate(); preview.Invalidate();
        }

        void PaintPreview(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var c = Value;
            var dark = Theme.Mix(c, Color.Black, 0.88);
            var ink = Theme.Contrast(dark, c) >= Theme.Contrast(Color.White, c) ? dark : Color.White;
            int h = preview.Height;
            // logo tile + a Start Flight button, as they will look
            var tile = new RectangleF(0, (h - Theme.S(44)) / 2f, Theme.S(44), Theme.S(44));
            Theme.FillRound(g, c, tile, Theme.S(10));
            float m = tile.Width * 0.2f, gap = tile.Width * 0.07f, inner = tile.Width - 2 * m, lw = inner * 0.56f, hh = (inner - gap) / 2;
            Theme.FillRound(g, ink, new RectangleF(tile.X + m, tile.Y + m, lw - gap / 2, inner), Theme.S(2));
            Theme.FillRound(g, ink, new RectangleF(tile.X + m + lw + gap / 2, tile.Y + m, inner - lw - gap / 2, hh), Theme.S(2));
            Theme.FillRound(g, ink, new RectangleF(tile.X + m + lw + gap / 2, tile.Y + m + hh + gap, inner - lw - gap / 2, hh), Theme.S(2));
            var btn = new RectangleF(tile.Right + Theme.S(14), (h - Theme.S(40)) / 2f, preview.Width - tile.Right - Theme.S(16), Theme.S(40));
            Theme.FillRound(g, c, btn, Theme.S(10));
            var br = Rectangle.Round(btn);
            Theme.DrawPlane(g, new Rectangle(br.X + Theme.S(14), br.Y, Theme.S(22), br.Height), ink, true);
            TextRenderer.DrawText(g, "Start Flight", Theme.F9S, new Rectangle(br.X + Theme.S(42), br.Y, br.Width - Theme.S(46), br.Height), ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }

        /// <summary>Saturation (x) × brightness (y) square for the current hue.</summary>
        class SVSquare : Owner
        {
            readonly ColorPickerDialog dlg;
            Bitmap cache; double cacheHue = -1;
            public SVSquare(ColorPickerDialog d) { dlg = d; Cursor = Cursors.Cross; }
            protected override void OnParentChanged(EventArgs e) { }

            void Pick(Point p) { dlg.SetSV(p.X / (double)(Width - 1), 1 - p.Y / (double)(Height - 1)); }
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Capture = true; Pick(e.Location); }
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) Pick(e.Location); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                if (cache == null || cacheHue != dlg.Hue || cache.Width != Width)
                {
                    if (cache != null) cache.Dispose();
                    cache = new Bitmap(Width, Height);
                    using (var cg = Graphics.FromImage(cache))
                    {
                        var r = new Rectangle(0, 0, Width, Height);
                        using (var h = new LinearGradientBrush(r, Color.White, FromHsv(dlg.Hue, 1, 1), 0f)) cg.FillRectangle(h, r);
                        using (var v = new LinearGradientBrush(r, Color.Transparent, Color.Black, 90f)) cg.FillRectangle(v, r);
                    }
                    cacheHue = dlg.Hue;
                }
                g.DrawImage(cache, 0, 0);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float x = (float)(dlg.Sat * (Width - 1)), y = (float)((1 - dlg.Val) * (Height - 1)), d = Theme.S(14);
                using (var pen = new Pen(Color.White, Theme.S(2.5f))) g.DrawEllipse(pen, x - d / 2, y - d / 2, d, d);
                using (var pen = new Pen(Color.FromArgb(120, 0, 0, 0), 1f)) g.DrawEllipse(pen, x - d / 2 - 1.5f, y - d / 2 - 1.5f, d + 3, d + 3);
            }
            protected override void Dispose(bool disposing) { if (disposing && cache != null) cache.Dispose(); base.Dispose(disposing); }
        }

        /// <summary>Vertical hue strip.</summary>
        class HueStrip : Owner
        {
            readonly ColorPickerDialog dlg;
            public HueStrip(ColorPickerDialog d) { dlg = d; Cursor = Cursors.Hand; }
            protected override void OnParentChanged(EventArgs e) { }
            void Pick(int y) { dlg.Hue = Math.Max(0, Math.Min(359.9, y / (double)(Height - 1) * 360)); }
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Capture = true; Pick(e.Y); }
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) Pick(e.Y); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Bg);
                for (int y = 0; y < Height; y++)
                    using (var p = new Pen(FromHsv(y / (double)Height * 360, 1, 1))) g.DrawLine(p, Theme.S(4), y, Width - Theme.S(4), y);
                int yy = (int)(dlg.Hue / 360 * (Height - 1));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Theme.Text, Theme.S(2))) g.DrawRectangle(pen, 1, yy - Theme.S(3), Width - 3, Theme.S(6));
            }
        }
    }
}
