using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ServiceBusExplorer.UI.Controls
{
    /// <summary>
    /// Small metric tile: a big tinted number and a caption.
    /// </summary>
    [DesignerCategory("Code")]
    public class StatTile : Control
    {
        private string _value = "–";
        private string _caption;
        private Color _tint = Theme.Accent;

        public StatTile()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(150, 58);
        }

        [DefaultValue("–")]
        public string Value
        {
            get => _value;
            set { _value = value; Invalidate(); }
        }

        [DefaultValue(null)]
        public string Caption
        {
            get => _caption;
            set { _caption = value; Invalidate(); }
        }

        public Color Tint
        {
            get => _tint;
            set { _tint = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.RoundedRect(bounds, 8))
            using (var brush = new SolidBrush(Theme.Blend(Color.White, _tint, 0.08f)))
            {
                g.FillPath(brush, path);
            }
            using (var bar = new SolidBrush(_tint))
            {
                g.FillRectangle(bar, 0, 12, 3, Height - 24);
            }

            TextRenderer.DrawText(g, _value, Theme.Stat, new Point(12, 6), _tint, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, _caption, Theme.Small, new Rectangle(12, Height - 21, Width - 16, 16), Theme.Muted,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }
}
