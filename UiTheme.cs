using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace AutoClickUI
{
    /// <summary>Clean light professional theme.</summary>
    internal static class AppTheme
    {
        public static readonly Color Bg = Color.FromArgb(245, 247, 250);
        public static readonly Color Surface = Color.White;
        public static readonly Color SurfaceAlt = Color.FromArgb(248, 250, 252);
        public static readonly Color Border = Color.FromArgb(226, 232, 240);
        public static readonly Color TextPrimary = Color.FromArgb(30, 41, 59);
        public static readonly Color TextMuted = Color.FromArgb(100, 116, 139);
        public static readonly Color Accent = Color.FromArgb(15, 118, 110);
        public static readonly Color AccentSoft = Color.FromArgb(204, 251, 241);
        public static readonly Color AccentDim = Color.FromArgb(13, 148, 136);
        public static readonly Color Success = Color.FromArgb(22, 163, 74);
        public static readonly Color Warning = Color.FromArgb(217, 119, 6);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
        public static readonly Color Start = Color.FromArgb(22, 163, 74);
        public static readonly Color StartHover = Color.FromArgb(21, 128, 61);
        public static readonly Color StopEnabled = Color.FromArgb(220, 38, 38);
        public static readonly Color LogInfo = Color.FromArgb(37, 99, 235);
        public static readonly Color LogOk = Color.FromArgb(22, 163, 74);
        public static readonly Color LogWarn = Color.FromArgb(180, 83, 9);
        public static readonly Color LogErr = Color.FromArgb(185, 28, 28);

        public static Font BrandFont { get { return new Font("Segoe UI Semibold", 12.5F, FontStyle.Bold); } }
        public static Font HeroFont { get { return new Font("Consolas", 36F, FontStyle.Bold); } }
        public static Font MonoFont { get { return new Font("Consolas", 10F, FontStyle.Regular); } }
        public static Font UiFont { get { return new Font("Segoe UI", 9.25F, FontStyle.Regular); } }
        public static Font UiFontBold { get { return new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold); } }
        public static Font CaptionFont { get { return new Font("Segoe UI", 8.25F, FontStyle.Regular); } }
        public static Font SectionFont { get { return new Font("Segoe UI Semibold", 9F, FontStyle.Bold); } }

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
            tb.BackColor = Color.White;
            tb.ForeColor = TextPrimary;
            tb.Font = UiFont;
        }

        public static void StyleNumeric(NumericUpDown nud)
        {
            nud.BorderStyle = BorderStyle.FixedSingle;
            nud.BackColor = Color.White;
            nud.ForeColor = TextPrimary;
            nud.Font = UiFont;
        }

        public static void StyleCombo(ComboBox cmb)
        {
            cmb.FlatStyle = FlatStyle.Standard;
            cmb.BackColor = Color.White;
            cmb.ForeColor = TextPrimary;
            cmb.Font = UiFont;
        }

        public static void StyleDateTimePicker(DateTimePicker dtp)
        {
            dtp.Font = UiFont;
            dtp.CalendarFont = UiFont;
        }

        public static void StyleRichText(RichTextBox rtb)
        {
            rtb.BackColor = Color.White;
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
            if (c.ToArgb() == Color.Green.ToArgb() || c.ToArgb() == Color.DarkGreen.ToArgb()) return LogOk;
            if (c.ToArgb() == Color.Red.ToArgb() || c.ToArgb() == Color.DarkRed.ToArgb()) return LogErr;
            if (c.ToArgb() == Color.Orange.ToArgb() || c.ToArgb() == Color.DarkOrange.ToArgb()) return LogWarn;
            if (c.ToArgb() == Color.Blue.ToArgb() || c.ToArgb() == Color.DarkBlue.ToArgb()) return LogInfo;
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

    /// <summary>White card with a real title label (layout-safe).</summary>
    internal sealed class CardPanel : Panel
    {
        private readonly Label _title;
        private readonly Panel _body;

        public Panel Body { get { return _body; } }

        public CardPanel(string title)
        {
            DoubleBuffered = true;
            BackColor = AppTheme.Surface;
            Padding = new Padding(1);
            Margin = new Padding(0, 0, 0, 12);

            var shell = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface, Padding = new Padding(14, 10, 14, 12) };

            _title = new Label
            {
                Text = title ?? string.Empty,
                Dock = DockStyle.Top,
                Height = 22,
                Font = AppTheme.SectionFont,
                ForeColor = AppTheme.TextMuted,
                BackColor = AppTheme.Surface,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppTheme.Surface
            };

            shell.Controls.Add(_body);
            shell.Controls.Add(_title);
            Controls.Add(shell);
            Paint += (s, e) =>
            {
                using (var pen = new Pen(AppTheme.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            };
        }

        public void SetTitle(string title)
        {
            _title.Text = title ?? string.Empty;
        }
    }

    internal sealed class HeroClockLabel : Control
    {
        private string _value = "--:--:--";

        public HeroClockLabel()
        {
            DoubleBuffered = true;
            BackColor = AppTheme.Surface;
            ForeColor = AppTheme.Accent;
            Font = AppTheme.HeroFont;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        public override string Text
        {
            get { return _value; }
            set
            {
                string v = value ?? string.Empty;
                if (_value == v) return;
                _value = v;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var brush = new SolidBrush(ForeColor))
            using (var format = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                e.Graphics.DrawString(_value, Font, brush, new RectangleF(2, 2, Width - 4, Height - 4), format);
            }
        }
    }

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
            Height = 38;
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
            SetAccent(AppTheme.SurfaceAlt, Color.FromArgb(241, 245, 249));
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
            BackColor = Enabled ? _baseColor : Color.FromArgb(241, 245, 249);
            base.OnMouseLeave(e);
        }

        protected override void OnEnabledChanged(System.EventArgs e)
        {
            BackColor = Enabled ? (_hover ? _hoverColor : _baseColor) : Color.FromArgb(241, 245, 249);
            ForeColor = Enabled ? _foreNormal : AppTheme.TextMuted;
            base.OnEnabledChanged(e);
        }
    }

    internal sealed class ThinProgressBar : Control
    {
        private double _value;

        public ThinProgressBar()
        {
            Height = 6;
            DoubleBuffered = true;
            BackColor = AppTheme.Surface;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
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
            using (var bg = new SolidBrush(Color.FromArgb(226, 232, 240)))
                e.Graphics.FillRectangle(bg, 0, 0, Width, Height);
            int w = (int)System.Math.Round(Width * _value);
            if (w > 0)
            {
                using (var brush = new SolidBrush(AppTheme.Accent))
                    e.Graphics.FillRectangle(brush, 0, 0, w, Height);
            }
        }
    }

    internal sealed class NavButton : Button
    {
        private bool _active;

        public NavButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = AppTheme.UiFontBold;
            Size = new Size(88, 30);
            ForeColor = AppTheme.TextMuted;
            BackColor = AppTheme.Surface;
            FlatAppearance.MouseOverBackColor = AppTheme.SurfaceAlt;
            FlatAppearance.MouseDownBackColor = AppTheme.SurfaceAlt;
            Margin = new Padding(4, 0, 0, 0);
        }

        public bool Active
        {
            get { return _active; }
            set
            {
                _active = value;
                if (_active)
                {
                    BackColor = AppTheme.AccentSoft;
                    ForeColor = AppTheme.Accent;
                    FlatAppearance.BorderSize = 1;
                    FlatAppearance.BorderColor = AppTheme.AccentDim;
                }
                else
                {
                    BackColor = AppTheme.Surface;
                    ForeColor = AppTheme.TextMuted;
                    FlatAppearance.BorderSize = 0;
                }
            }
        }
    }
}
