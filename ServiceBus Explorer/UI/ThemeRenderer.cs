using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ServiceBusExplorer.UI
{
    /// <summary>
    /// ToolStrip renderer: gradient header banner (ToolStrip tagged "header") and light, rounded menus.
    /// </summary>
    internal class ThemeRenderer : ToolStripProfessionalRenderer
    {
        public const string HeaderTag = "header";

        public ThemeRenderer()
        {
            RoundedEdges = false;
        }

        private static bool IsHeader(ToolStrip strip) => !(strip is ToolStripDropDown) && strip?.Tag as string == HeaderTag;

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;

            if (e.ToolStrip is ToolStripDropDown)
            {
                g.Clear(Theme.Card);
                return;
            }

            if (!IsHeader(e.ToolStrip))
            {
                base.OnRenderToolStripBackground(e);
                return;
            }

            var bounds = e.ToolStrip.ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            using (var gradient = new LinearGradientBrush(bounds, Theme.HeaderStart, Theme.HeaderEnd, LinearGradientMode.Horizontal))
            {
                g.FillRectangle(gradient, bounds);
            }

            // Decorative heartbeat line on the right-hand side
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.DrawPulseDecoration(g, bounds);

            // Accent line at the bottom
            var line = new Rectangle(bounds.Left, bounds.Bottom - 3, bounds.Width, 3);
            using (var accent = new LinearGradientBrush(line, Theme.Teal, Theme.Accent, LinearGradientMode.Horizontal))
            {
                g.FillRectangle(accent, line);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
            {
                using (var pen = new Pen(Theme.Border))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                }
            }
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (!IsHeader(e.ToolStrip))
            {
                base.OnRenderButtonBackground(e);
                return;
            }
            PaintHeaderItem(e.Graphics, e.Item);
        }

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (!IsHeader(e.ToolStrip))
            {
                base.OnRenderDropDownButtonBackground(e);
                return;
            }
            PaintHeaderItem(e.Graphics, e.Item);
        }

        private static void PaintHeaderItem(Graphics g, ToolStripItem item)
        {
            if (!item.Selected && !item.Pressed) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(1, 10, item.Width - 2, item.Height - 20);
            using (var path = Theme.RoundedRect(bounds, 6))
            using (var brush = new SolidBrush(Color.FromArgb(item.Pressed ? 60 : 36, Color.White)))
            {
                g.FillPath(brush, path);
            }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            using (var path = Theme.RoundedRect(bounds, 5))
            using (var brush = new SolidBrush(Theme.AccentSoft))
            {
                g.FillPath(brush, path);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (IsHeader(e.ToolStrip))
            {
                // Labels keep their own color (title, subtitle, connection); buttons are white.
                if (!(e.Item is ToolStripLabel)) e.TextColor = Color.White;
            }
            else
            {
                e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Subtle;
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = IsHeader(e.Item?.Owner) ? Color.White : Theme.Muted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var g = e.Graphics;
            if (IsHeader(e.ToolStrip))
            {
                using (var pen = new Pen(Color.FromArgb(60, Color.White)))
                {
                    var x = e.Item.Width / 2;
                    g.DrawLine(pen, x, 16, x, e.Item.Height - 16);
                }
                return;
            }

            using (var pen = new Pen(Theme.Divider))
            {
                var y = e.Item.Height / 2;
                g.DrawLine(pen, 8, y, e.Item.Width - 8, y);
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // Plain white menus: no grey image margin.
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            LineIcons.Draw(e.Graphics, IconKind.Check, new RectangleF(e.ImageRectangle.X, e.ImageRectangle.Y, 16, 16), Theme.Accent);
        }
    }
}
