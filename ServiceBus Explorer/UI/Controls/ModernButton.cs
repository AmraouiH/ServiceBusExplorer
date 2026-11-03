using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ServiceBusExplorer.UI.Controls
{
    public enum ButtonVariant
    {
        Primary,
        Secondary,
        Ghost,
        Danger,
        Success
    }

    /// <summary>
    /// Flat rounded button with hover / pressed states and an optional line icon.
    /// </summary>
    [DesignerCategory("Code")]
    public class ModernButton : Button
    {
        private ButtonVariant _variant = ButtonVariant.Secondary;
        private IconKind _glyph = IconKind.None;
        private int _radius = 6;
        private bool _hover;
        private bool _pressed;

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            Font = Theme.Semibold;
            Size = new Size(110, 32);
        }

        [DefaultValue(ButtonVariant.Secondary)]
        public ButtonVariant Variant
        {
            get => _variant;
            set { _variant = value; Invalidate(); }
        }

        [DefaultValue(IconKind.None)]
        public IconKind Glyph
        {
            get => _glyph;
            set { _glyph = value; Invalidate(); }
        }

        [DefaultValue(6)]
        public int Radius
        {
            get => _radius;
            set { _radius = value; Invalidate(); }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var text = TextRenderer.MeasureText(Text ?? string.Empty, Font);
            var width = text.Width + 28 + (_glyph != IconKind.None ? 22 : 0);
            return new Size(System.Math.Max(width, MinimumSize.Width), Height > 0 ? Height : 32);
        }

        protected override void OnMouseEnter(System.EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(System.EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(System.EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            GetColors(out var fill, out var border, out var fore);

            var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.RoundedRect(bounds, _radius))
            {
                if (fill != Color.Empty)
                {
                    using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                }
                if (border != Color.Empty)
                {
                    using (var pen = new Pen(border)) g.DrawPath(pen, path);
                }
            }

            var textSize = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            var glyphWidth = _glyph != IconKind.None ? (textSize.Width == 0 ? 16 : 22) : 0;
            var x = (Width - (glyphWidth + textSize.Width)) / 2;

            if (_glyph != IconKind.None)
            {
                LineIcons.Draw(g, _glyph, new RectangleF(x, (Height - 16) / 2f, 16, 16), fore);
            }

            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + glyphWidth, 0, textSize.Width + 4, Height), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            if (Focused && ShowFocusCues)
            {
                using (var focus = Theme.RoundedRect(new RectangleF(2, 2, Width - 5, Height - 5), _radius - 1))
                using (var pen = new Pen(Color.FromArgb(140, _variant == ButtonVariant.Primary ? Color.White : Theme.Accent)) { DashStyle = DashStyle.Dot })
                {
                    g.DrawPath(pen, focus);
                }
            }
        }

        private void GetColors(out Color fill, out Color border, out Color fore)
        {
            fill = Color.Empty;
            border = Color.Empty;

            if (!Enabled)
            {
                fore = Theme.Subtle;
                switch (_variant)
                {
                    case ButtonVariant.Primary:
                        fill = Theme.DisabledFill;
                        fore = Color.White;
                        break;
                    case ButtonVariant.Ghost:
                        break;
                    default:
                        fill = Color.FromArgb(248, 249, 251);
                        border = Theme.Divider;
                        break;
                }
                return;
            }

            switch (_variant)
            {
                case ButtonVariant.Primary:
                    fill = _pressed ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent;
                    fore = Color.White;
                    break;
                case ButtonVariant.Ghost:
                    fill = _pressed ? Theme.Blend(Theme.AccentSoft, Theme.Accent, 0.12f) : _hover ? Theme.AccentSoft : Color.Empty;
                    fore = Theme.Accent;
                    break;
                case ButtonVariant.Danger:
                    fill = _pressed || _hover ? Theme.DangerPressed : Theme.DangerSoft;
                    fore = Theme.Danger;
                    break;
                case ButtonVariant.Success:
                    fill = _pressed || _hover ? Theme.Blend(Theme.SuccessSoft, Theme.Success, 0.15f) : Theme.SuccessSoft;
                    fore = Theme.SuccessText;
                    break;
                default:
                    fill = _pressed ? Color.FromArgb(236, 239, 244) : _hover ? Color.FromArgb(245, 247, 250) : Theme.Card;
                    border = _hover ? Color.FromArgb(201, 209, 221) : Theme.Border;
                    fore = Theme.Text;
                    break;
            }
        }
    }
}
