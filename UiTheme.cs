using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClickUI
{
    /// <summary>Precision Console dark theme tokens and WinForms styling helpers.</summary>
    internal static class AppTheme
    {
        public static readonly Color Bg = Color.FromArgb(15, 20, 25);
        public static readonly Color Surface = Color.FromArgb(26, 35, 50);
        public static readonly Color SurfaceAlt = Color.FromArgb(32, 44, 62);
        public static readonly Color Border = Color.FromArgb(48, 64, 88);
        public static readonly Color TextPrimary = Color.FromArgb(232, 240, 248);
        public static readonly Color TextMuted = Color.FromArgb(148, 163, 184);
        public static readonly Color Accent = Color.FromArgb(45, 212, 191);
        public static readonly Color AccentDim = Color.FromArgb(20, 120, 110);
        public static readonly Color Success = Color.FromArgb(52, 211, 153);
        public static readonly Color Warning = Color.FromArgb(251, 191, 36);
        public static readonly Color Danger = Color.FromArgb(248, 113, 113);
        public static readonly Color Start = Color.FromArgb(16, 185, 129);
        public static readonly Color StartHover = Color.FromArgb(5, 150, 105);
        public static readonly Color Stop = Color.FromArgb(127, 29, 29);
        public static readonly Color StopEnabled = Color.FromArgb(185, 28, 28);
        public static readonly Color LogInfo = Color.FromArgb(125, 211, 252);
        public static readonly Color LogOk = Color.FromArgb(110, 231, 183);
        public static readonly Color LogWarn = Color.FromArgb(253, 224, 71);
        public static readonly Color LogErr = Color.FromArgb(252, 165, 165);

        public static Font BrandFont
        {
            get { return new Font("Segoe UI Semibold", 14F, FontStyle.Bold); }
        }

        public static Font HeroFont
        {
            get { return new Font("Consolas", 36F, FontStyle.Bold); }
        }

        public static Font MonoFont
        {
            get { return new Font("Consolas", 11F, FontStyle.Regular); }
        }

        public static Font UiFont
        {
            get { return new Font("Segoe UI", 9.25F, FontStyle.Regular); }
        }

        public static Font UiFontBold
        {
            get { return new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold); }
        }

        public static Font CaptionFont
        {
            get { return new Font("Segoe UI", 8.25F, FontStyle.Regular); }
        }

        public static void StyleForm(Form form)
        {
            form.BackColor = Bg;
            form.ForeColor = TextPrimary;
            form.Font = UiFont;
        }

        public static void StyleLabel(Label lbl, bool muted = false, bool mono = false)
        {
            lbl.BackColor = Color.Transparent;
            lbl.ForeColor = muted ? TextMuted : TextPrimary;
            lbl.Font = mono ? MonoFont : UiFont;
        }

        public static void StyleTextBox(TextBox tb)
        {
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.BackColor = SurfaceAlt;
            tb.ForeColor = TextPrimary;
            tb.Font = UiFont;
        }

        public static void StyleNumeric(NumericUpDown nud)
        {
            nud.BorderStyle = BorderStyle.FixedSingle;
            nud.BackColor = SurfaceAlt;
            nud.ForeColor = TextPrimary;
            nud.Font = UiFont;
        }

        public static void StyleCombo(ComboBox cmb)
        {
            cmb.FlatStyle = FlatStyle.Flat;
            cmb.BackColor = SurfaceAlt;
            cmb.ForeColor = TextPrimary;
            cmb.Font = UiFont;
        }

        public static void StyleDateTimePicker(DateTimePicker dtp)
        {
            dtp.CalendarMonthBackground = Surface;
            dtp.CalendarForeColor = TextPrimary;
            dtp.CalendarTitleBackColor = SurfaceAlt;
            dtp.CalendarTitleForeColor = TextPrimary;
            dtp.CalendarTrailingForeColor = TextMuted;
            dtp.Font = UiFont;
        }

        public static void StyleRichText(RichTextBox rtb)
        {
            rtb.BackColor = Surface;
            rtb.ForeColor = TextPrimary;
            rtb.BorderStyle = BorderStyle.None;
            rtb.Font = new Font("Consolas", 9.25F);
        }

        public static void StyleStatusStrip(StatusStrip strip, ToolStripStatusLabel label)
        {
            strip.BackColor = Surface;
            strip.ForeColor = TextMuted;
            strip.SizingGrip = false;
            strip.Renderer = new SolidStripRenderer(Surface);
            label.ForeColor = TextMuted;
            label.Font = CaptionFont;
        }

        public static Color MapLogColor(Color? requested)
        {
            if (!requested.HasValue) return TextPrimary;
            Color c = requested.Value;
            if (c.ToArgb() == Color.Green.ToArgb() || c.ToArgb() == Color.DarkGreen.ToArgb())
                return LogOk;
            if (c.ToArgb() == Color.Red.ToArgb() || c.ToArgb() == Color.DarkRed.ToArgb())
                return LogErr;
            if (c.ToArgb() == Color.Orange.ToArgb() || c.ToArgb() == Color.DarkOrange.ToArgb())
                return LogWarn;
            if (c.ToArgb() == Color.Blue.ToArgb() || c.ToArgb() == Color.DarkBlue.ToArgb())
                return LogInfo;
            return c;
        }

        private sealed class SolidStripRenderer : ToolStripProfessionalRenderer
        {
            private readonly Color _bg;
            public SolidStripRenderer(Color bg) : base(new SolidStripColors(bg)) { _bg = bg; }
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var b = new SolidBrush(_bg))
                    e.Graphics.FillRectangle(b, e.AffectedBounds);
            }
        }

        private sealed class SolidStripColors : ProfessionalColorTable
        {
            private readonly Color _bg;
            public SolidStripColors(Color bg) { _bg = bg; }
            public override Color ToolStripGradientBegin { get { return _bg; } }
            public override Color ToolStripGradientMiddle { get { return _bg; } }
            public override Color ToolStripGradientEnd { get { return _bg; } }
            public override Color StatusStripGradientBegin { get { return _bg; } }
            public override Color StatusStripGradientEnd { get { return _bg; } }
        }
    }

    /// <summary>Rounded dark surface panel with subtle border.</summary>
    internal sealed class SurfacePanel : Panel
    {
        public int CornerRadius { get; set; }
        public Color SurfaceColor { get; set; }
        public Color BorderColor { get; set; }
        public string Title { get; set; }

        public SurfacePanel()
        {
            CornerRadius = 12;
            SurfaceColor = AppTheme.Surface;
            BorderColor = AppTheme.Border;
            Title = null;
            DoubleBuffered = true;
            BackColor = AppTheme.Bg;
            Padding = new Padding(14, 18, 14, 14);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundRect(rect, CornerRadius))
            using (var fill = new SolidBrush(SurfaceColor))
            using (var pen = new Pen(BorderColor))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }

            if (!string.IsNullOrEmpty(Title))
            {
                using (var font = AppTheme.CaptionFont)
                using (var brush = new SolidBrush(AppTheme.TextMuted))
                    e.Graphics.DrawString(Title.ToUpperInvariant(), font, brush, 16, 8);
            }
        }

        private static GraphicsPath RoundRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>Flat accent / action button with hover.</summary>
    internal sealed class AccentButton : Button
    {
        private Color _baseColor;
        private Color _hoverColor;
        private Color _foreNormal = Color.White;
        private bool _hover;

        public AccentButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = AppTheme.UiFontBold;
            ForeColor = Color.White;
            Height = 40;
            SetAccent(AppTheme.Accent, AppTheme.AccentDim);
        }

        public void SetAccent(Color baseColor, Color hoverColor)
        {
            _baseColor = baseColor;
            _hoverColor = hoverColor;
            BackColor = baseColor;
            FlatAppearance.MouseOverBackColor = hoverColor;
            FlatAppearance.MouseDownBackColor = hoverColor;
            _foreNormal = Color.White;
            ForeColor = _foreNormal;
            FlatAppearance.BorderSize = 0;
        }

        public void SetSecondary()
        {
            SetAccent(AppTheme.SurfaceAlt, AppTheme.Border);
            _foreNormal = AppTheme.TextPrimary;
            ForeColor = _foreNormal;
            FlatAppearance.BorderSize = 1;
            FlatAppearance.BorderColor = AppTheme.Border;
        }

        protected override void OnMouseEnter(System.EventArgs e)
        {
            _hover = true;
            if (Enabled) BackColor = _hoverColor;
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(System.EventArgs e)
        {
            _hover = false;
            BackColor = Enabled ? _baseColor : AppTheme.Border;
            base.OnMouseLeave(e);
        }

        protected override void OnEnabledChanged(System.EventArgs e)
        {
            BackColor = Enabled ? (_hover ? _hoverColor : _baseColor) : AppTheme.Border;
            ForeColor = Enabled ? _foreNormal : AppTheme.TextMuted;
            base.OnEnabledChanged(e);
        }
    }

    /// <summary>Thin horizontal progress bar for countdown.</summary>
    internal sealed class ThinProgressBar : Control
    {
        private double _value;

        public ThinProgressBar()
        {
            Height = 6;
            DoubleBuffered = true;
            _value = 0;
        }

        public double Progress
        {
            get { return _value; }
            set
            {
                double v = value;
                if (v < 0) v = 0;
                if (v > 1) v = 1;
                if (System.Math.Abs(_value - v) < 0.0005) return;
                _value = v;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle track = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var bg = new SolidBrush(AppTheme.SurfaceAlt))
            using (var path = RoundRect(track, Height / 2))
                e.Graphics.FillPath(bg, path);

            int w = (int)System.Math.Round((Width - 1) * _value);
            if (w > 1)
            {
                Rectangle fill = new Rectangle(0, 0, w, Height - 1);
                using (var brush = new LinearGradientBrush(fill, AppTheme.Accent, AppTheme.Success, LinearGradientMode.Horizontal))
                using (var path = RoundRect(fill, Height / 2))
                    e.Graphics.FillPath(brush, path);
            }
        }

        private static GraphicsPath RoundRect(Rectangle bounds, int radius)
        {
            int d = System.Math.Max(1, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>Top navigation pill button.</summary>
    internal sealed class NavButton : Button
    {
        private bool _active;

        public NavButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = AppTheme.UiFontBold;
            Height = 32;
            ForeColor = AppTheme.TextMuted;
            BackColor = Color.Transparent;
            FlatAppearance.MouseOverBackColor = AppTheme.SurfaceAlt;
            FlatAppearance.MouseDownBackColor = AppTheme.SurfaceAlt;
        }

        public bool Active
        {
            get { return _active; }
            set
            {
                _active = value;
                if (_active)
                {
                    BackColor = AppTheme.SurfaceAlt;
                    ForeColor = AppTheme.Accent;
                }
                else
                {
                    BackColor = Color.Transparent;
                    ForeColor = AppTheme.TextMuted;
                }
            }
        }
    }
}
