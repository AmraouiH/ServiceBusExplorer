using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ServiceBusExplorer.UI.Controls
{
    /// <summary>
    /// Rounded white card with a soft shadow and an optional header (step badge, title, subtitle).
    /// Docked children are laid out below the header thanks to the top padding.
    /// </summary>
    [DesignerCategory("Code")]
    public class CardPanel : Panel
    {
        /// <summary>Compact header: title and subtitle on one line (saves vertical space on small screens).</summary>
        public const int HeaderHeight = 40;
        private const int Radius = 10;

        private string _title;
        private string _subtitle;
        private int _step;

        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            UpdatePadding();
        }

        [DefaultValue(null)]
        public string Title
        {
            get => _title;
            set { _title = value; UpdatePadding(); Invalidate(); }
        }

        [DefaultValue(null)]
        public string Subtitle
        {
            get => _subtitle;
            set { _subtitle = value; Invalidate(); }
        }

        /// <summary>Step number shown in the badge (0 = no badge).</summary>
        [DefaultValue(0)]
        public int Step
        {
            get => _step;
            set { _step = value; Invalidate(); }
        }

        private bool HasHeader => !string.IsNullOrEmpty(_title);

        private void UpdatePadding()
        {
            Padding = new Padding(14, HasHeader ? HeaderHeight + 8 : 12, 14, 14);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Canvas);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Soft shadow + card
            using (var shadow = Theme.RoundedRect(new RectangleF(1f, 2.5f, Width - 2.5f, Height - 3.5f), Radius))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(18, 20, 24, 70)))
            {
                g.FillPath(shadowBrush, shadow);
            }
            using (var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 2f, Height - 3f), Radius))
            using (var fill = new SolidBrush(Theme.Card))
            using (var border = new Pen(Theme.Border))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }

            if (!HasHeader) return;

            var x = 18;
            if (_step > 0)
            {
                var badge = new Rectangle(18, 16, 26, 26);
                using (var brush = new LinearGradientBrush(badge, Theme.Accent, Theme.HeaderEnd, LinearGradientMode.ForwardDiagonal))
                {
                    g.FillEllipse(brush, badge);
                }
                TextRenderer.DrawText(g, _step.ToString(), Theme.Semibold, badge, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x = 54;
            }

            TextRenderer.DrawText(g, _title, Theme.CardTitle, new Point(x, 10), Theme.Text, TextFormatFlags.NoPadding);
            if (!string.IsNullOrEmpty(_subtitle))
            {
                var titleWidth = TextRenderer.MeasureText(g, _title, Theme.CardTitle, Size.Empty, TextFormatFlags.NoPadding).Width;
                var subtitleX = x + titleWidth + 12;
                TextRenderer.DrawText(g, _subtitle, Theme.Small, new Rectangle(subtitleX, 14, Math.Max(0, Width - subtitleX - 18), 18), Theme.Muted,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }

            using (var pen = new Pen(Theme.Divider))
            {
                g.DrawLine(pen, 14, HeaderHeight, Width - 16, HeaderHeight);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Everything is drawn in OnPaintBackground.
        }
    }
}
