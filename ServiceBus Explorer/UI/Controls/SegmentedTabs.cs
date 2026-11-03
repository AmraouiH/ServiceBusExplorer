using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ServiceBusExplorer.UI.Controls
{
    /// <summary>
    /// Flat pill tabs (icon, text, optional counter badge). The host shows the page matching <see cref="SelectedIndex"/>.
    /// </summary>
    [DesignerCategory("Code")]
    public class SegmentedTabs : Control
    {
        public class Tab
        {
            public string Text { get; set; }
            public IconKind Glyph { get; set; }
            public string Badge { get; set; }
            public Color BadgeColor { get; set; } = Theme.Danger;
            public bool Hidden { get; set; }
            internal Rectangle Bounds;
        }

        private const int RowHeight = 38;

        private int _selected;
        private int _hover = -1;

        public event EventHandler SelectedIndexChanged;

        public SegmentedTabs()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 44;
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public List<Tab> Tabs { get; } = new List<Tab>();

        [DefaultValue(0)]
        public int SelectedIndex
        {
            get => _selected;
            set
            {
                if (value < 0 || value >= Tabs.Count || value == _selected) return;
                _selected = value;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Puts the tabs that do not fit on extra rows (the control height follows, see <see cref="UpdateHeight"/>).</summary>
        [DefaultValue(false)]
        public bool Wrap { get; set; }

        /// <returns>Number of rows used.</returns>
        private int ArrangeTabs(Graphics g)
        {
            var x = 0;
            var row = 0;
            var height = Wrap ? RowHeight : Height;
            foreach (var tab in Tabs)
            {
                if (tab.Hidden)
                {
                    tab.Bounds = Rectangle.Empty;
                    continue;
                }

                var text = TextRenderer.MeasureText(g, tab.Text ?? string.Empty, Theme.Semibold, Size.Empty, TextFormatFlags.NoPadding).Width;
                var badge = string.IsNullOrEmpty(tab.Badge) ? 0 : TextRenderer.MeasureText(g, tab.Badge, Theme.SmallBold, Size.Empty, TextFormatFlags.NoPadding).Width + 16;
                var width = 18 + 22 + text + badge + 16;
                if (Wrap && x > 0 && x + width > Width)
                {
                    x = 0;
                    row++;
                }
                tab.Bounds = new Rectangle(x, row * height + 4, width, height - 8);
                x += width + 6;
            }
            return row + 1;
        }

        /// <summary>With <see cref="Wrap"/>, adapts the height to the number of rows (call after changing texts or badges).</summary>
        public void UpdateHeight()
        {
            if (!Wrap || !IsHandleCreated || Width <= 0) return;
            using (var g = CreateGraphics())
            {
                var height = ArrangeTabs(g) * RowHeight;
                if (Height != height) Height = height;
            }
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Wrap && IsHandleCreated) BeginInvoke(new Action(UpdateHeight));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateHeight();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var index = Tabs.FindIndex(t => t.Bounds.Contains(e.Location));
            if (index == _hover) return;
            _hover = index;
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            var index = Tabs.FindIndex(t => t.Bounds.Contains(e.Location));
            if (index >= 0) SelectedIndex = index;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Canvas);
            ArrangeTabs(g);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            for (var i = 0; i < Tabs.Count; i++)
            {
                var tab = Tabs[i];
                if (tab.Hidden) continue;
                var selected = i == _selected;
                var bounds = tab.Bounds;

                if (selected || i == _hover)
                {
                    using (var path = Theme.RoundedRect(new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1, bounds.Height - 1), 8))
                    using (var fill = new SolidBrush(selected ? Theme.Card : Color.FromArgb(234, 238, 244)))
                    {
                        g.FillPath(fill, path);
                        if (selected)
                        {
                            using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
                        }
                    }
                }

                var fore = selected ? Theme.Accent : Theme.Muted;
                LineIcons.Draw(g, tab.Glyph, new RectangleF(bounds.X + 14, bounds.Y + (bounds.Height - 16) / 2f, 16, 16), fore);
                var textX = bounds.X + 14 + 22;
                TextRenderer.DrawText(g, tab.Text, Theme.Semibold, new Rectangle(textX, bounds.Y, bounds.Width, bounds.Height), selected ? Theme.Text : Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

                if (!string.IsNullOrEmpty(tab.Badge))
                {
                    var textWidth = TextRenderer.MeasureText(g, tab.Text ?? string.Empty, Theme.Semibold, Size.Empty, TextFormatFlags.NoPadding).Width;
                    var badgeWidth = TextRenderer.MeasureText(g, tab.Badge, Theme.SmallBold, Size.Empty, TextFormatFlags.NoPadding).Width + 12;
                    var badge = new RectangleF(textX + textWidth + 8, bounds.Y + (bounds.Height - 18) / 2f, badgeWidth, 18);
                    using (var path = Theme.RoundedRect(badge, 9))
                    using (var fill = new SolidBrush(Theme.Blend(Color.White, tab.BadgeColor, 0.14f)))
                    {
                        g.FillPath(fill, path);
                    }
                    TextRenderer.DrawText(g, tab.Badge, Theme.SmallBold, Rectangle.Round(badge), tab.BadgeColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }

            // Selected underline accent
            if (_selected >= 0 && _selected < Tabs.Count)
            {
                var b = Tabs[_selected].Bounds;
                using (var brush = new SolidBrush(Theme.Accent)) g.FillRectangle(brush, b.X + 14, b.Bottom - 3, b.Width - 28, 2);
            }
        }
    }
}
