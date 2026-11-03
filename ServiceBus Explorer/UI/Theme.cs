using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ServiceBusExplorer.UI
{
    /// <summary>
    /// Visual identity of ServiceBus Explorer: palette, fonts and shared drawing helpers.
    /// No third-party UI library: everything is drawn with GDI+ so the plugin ships as a single DLL.
    /// </summary>
    internal static class Theme
    {
        #region Palette

        public static readonly Color Canvas = Color.FromArgb(243, 245, 249);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(226, 231, 239);
        public static readonly Color Divider = Color.FromArgb(238, 241, 246);
        public static readonly Color Text = Color.FromArgb(30, 36, 51);
        public static readonly Color Muted = Color.FromArgb(107, 115, 133);
        public static readonly Color Subtle = Color.FromArgb(160, 167, 180);

        public static readonly Color Accent = Color.FromArgb(0, 120, 212);
        public static readonly Color AccentHover = Color.FromArgb(16, 110, 190);
        public static readonly Color AccentPressed = Color.FromArgb(0, 90, 158);
        public static readonly Color AccentSoft = Color.FromArgb(230, 242, 252);
        public static readonly Color Teal = Color.FromArgb(45, 212, 191);
        public static readonly Color TealDark = Color.FromArgb(13, 148, 136);
        public static readonly Color Pulse = Color.FromArgb(52, 232, 158);

        public static readonly Color HeaderStart = Color.FromArgb(10, 25, 47);
        public static readonly Color HeaderEnd = Color.FromArgb(12, 74, 110);

        // Service Bus entities
        public static readonly Color QueueColor = Color.FromArgb(0, 120, 212);
        public static readonly Color TopicColor = Color.FromArgb(124, 58, 237);
        public static readonly Color SubscriptionColor = Color.FromArgb(13, 148, 136);
        public static readonly Color RuleColor = Color.FromArgb(234, 88, 12);

        // Message dialogs (shared with Dataverse Cartographer and Dataverse Record Recovery)
        public static readonly Color Diff = Color.FromArgb(245, 158, 11);
        public static readonly Color Slave = Color.FromArgb(234, 88, 12);
        public static readonly Color SameBack = Color.FromArgb(250, 251, 253);

        public static readonly Color Success = Color.FromArgb(22, 163, 74);
        public static readonly Color SuccessSoft = Color.FromArgb(225, 245, 233);
        public static readonly Color SuccessText = Color.FromArgb(15, 122, 61);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
        public static readonly Color DangerSoft = Color.FromArgb(254, 231, 231);
        public static readonly Color DangerPressed = Color.FromArgb(252, 205, 205);
        public static readonly Color WarningSoft = Color.FromArgb(255, 244, 229);
        public static readonly Color WarningText = Color.FromArgb(138, 75, 0);
        public static readonly Color DisabledFill = Color.FromArgb(203, 208, 219);

        #endregion

        #region Fonts

        public static readonly Font Base = new Font("Segoe UI", 9F);
        public static readonly Font Semibold = new Font("Segoe UI Semibold", 9F);
        public static readonly Font Small = new Font("Segoe UI", 8F);
        public static readonly Font SmallBold = new Font("Segoe UI Semibold", 7.5F);
        public static readonly Font CardTitle = new Font("Segoe UI Semibold", 11F);
        public static readonly Font HeaderTitle = new Font("Segoe UI Semibold", 13F);
        public static readonly Font Stat = new Font("Segoe UI Semibold", 15F);
        public static readonly Font Mono = new Font("Consolas", 8.5F);
        public static readonly Font Strike = new Font("Segoe UI", 9F, FontStyle.Strikeout);

        #endregion

        /// <summary>
        /// Paints a grid cell as a colored pill (status of a step or of a match).
        /// </summary>
        public static void PaintPillCell(DataGridViewCellPaintingEventArgs e, DataGridView grid, string text, Color fore, Color back)
        {
            PaintFlatCell(e, grid, false);
            if (string.IsNullOrEmpty(text)) return;

            var g = e.Graphics;
            var size = TextRenderer.MeasureText(g, text, SmallBold, Size.Empty, TextFormatFlags.NoPadding);
            var bounds = new Rectangle(e.CellBounds.X + 8, e.CellBounds.Y + (e.CellBounds.Height - 20) / 2, Math.Min(size.Width + 16, e.CellBounds.Width - 12), 20);
            var previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundedRect(bounds, 10))
            using (var brush = new SolidBrush(back))
            {
                g.FillPath(brush, path);
            }
            g.SmoothingMode = previous;
            TextRenderer.DrawText(g, text, SmallBold, bounds, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        /// <summary>Soft background matching a pill color.</summary>
        public static Color Soft(Color color) => Blend(Color.White, color, 0.13f);

        public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0.5f)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color Blend(Color from, Color to, float amount)
        {
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }

        /// <summary>
        /// Flat, airy grid style shared by every DataGridView of the tool.
        /// </summary>
        public static void StyleGrid(DataGridView grid, bool alternateRows)
        {
            EnableDoubleBuffer(grid);
            grid.BorderStyle = BorderStyle.None;
            grid.BackgroundColor = Card;
            grid.GridColor = Divider;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.EnableHeadersVisualStyles = false;
            grid.RowHeadersVisible = false;
            grid.AllowUserToResizeRows = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = 34;
            grid.RowTemplate.Height = 30;

            var header = grid.ColumnHeadersDefaultCellStyle;
            header.BackColor = Color.FromArgb(247, 248, 251);
            header.ForeColor = Muted;
            header.SelectionBackColor = header.BackColor;
            header.SelectionForeColor = Muted;
            header.Font = SmallBold;
            header.Padding = new Padding(6, 0, 6, 0);
            header.Alignment = DataGridViewContentAlignment.MiddleLeft;

            var cell = grid.DefaultCellStyle;
            cell.BackColor = Card;
            cell.ForeColor = Text;
            cell.Font = Base;
            cell.SelectionBackColor = AccentSoft;
            cell.SelectionForeColor = Text;
            cell.Padding = new Padding(6, 0, 6, 0);

            if (alternateRows)
            {
                grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 253);
            }
        }

        /// <summary>
        /// Paints a data cell without the native borders (background + content), then a single light divider.
        /// Keeps the grid perfectly flat whatever the system theme.
        /// </summary>
        public static void PaintFlatCell(DataGridViewCellPaintingEventArgs e, DataGridView grid, bool paintContent = true)
        {
            // The Graphics object is shared by all cells: anti-aliasing left on by a custom drawing
            // would blur the rectangle edges and draw 1px seams between cells.
            e.Graphics.SmoothingMode = SmoothingMode.None;

            var parts = DataGridViewPaintParts.Background | DataGridViewPaintParts.SelectionBackground;
            if (paintContent) parts |= DataGridViewPaintParts.ContentForeground | DataGridViewPaintParts.ContentBackground;
            e.Paint(e.CellBounds, parts);
            PaintDivider(e.Graphics, e.CellBounds, grid.GridColor);
            e.Handled = true;
        }

        public static void PaintDivider(Graphics g, Rectangle cellBounds, Color color)
        {
            var previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            using (var pen = new Pen(color))
            {
                g.DrawLine(pen, cellBounds.Left, cellBounds.Bottom - 1, cellBounds.Right, cellBounds.Bottom - 1);
            }
            g.SmoothingMode = previous;
        }

        public static void EnableDoubleBuffer(Control control)
        {
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(control, true);
        }

        /// <summary>
        /// Native placeholder text (cue banner) for a single-line TextBox.
        /// </summary>
        public static void SetCue(TextBox box, string text)
        {
            SendMessage(box.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, text);
        }

        /// <summary>
        /// Native placeholder text of an editable ComboBox (CB_SETCUEBANNER).
        /// </summary>
        public static void SetCue(ComboBox box, string text)
        {
            SendMessage(box.Handle, 0x1703 /* CB_SETCUEBANNER */, IntPtr.Zero, text);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static Bitmap _logo;

        /// <summary>The « AH » icon of the author's tools (same picture as the XrmToolBox tile), at any size.</summary>
        public static Bitmap CreateLogo(int size)
        {
            if (_logo == null)
            {
                using (var stream = new System.IO.MemoryStream(Convert.FromBase64String(PluginIcons.Big)))
                using (var source = Image.FromStream(stream))
                {
                    _logo = new Bitmap(source);
                }
            }
            var bitmap = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawImage(_logo, new Rectangle(0, 0, size, size));
            }
            return bitmap;
        }

        private static Bitmap _ahLogo;

        /// <summary>The "AH" icon of the author's tools (same as the XrmToolBox tools list), resized with a high quality filter.</summary>
        public static Bitmap AhLogo(int size)
        {
            if (_ahLogo == null)
            {
                using (var stream = new System.IO.MemoryStream(Convert.FromBase64String(PluginIcons.Big)))
                using (var image = Image.FromStream(stream))
                {
                    _ahLogo = new Bitmap(image);
                }
            }

            var bitmap = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawImage(_ahLogo, new Rectangle(0, 0, size, size));
            }
            return bitmap;
        }

        /// <summary>
        /// Faint ECG line drawn on the right part of the gradient headers.
        /// </summary>
        public static void DrawPulseDecoration(Graphics g, Rectangle bounds)
        {
            var width = Math.Min(560, bounds.Width / 2);
            if (width < 120) return;
            var left = bounds.Right - width;
            var mid = bounds.Top + bounds.Height * 0.55f;
            var h = bounds.Height * 0.32f;
            var points = new[]
            {
                new PointF(left, mid), new PointF(left + width * 0.35f, mid), new PointF(left + width * 0.40f, mid - h * 0.45f),
                new PointF(left + width * 0.45f, mid + h), new PointF(left + width * 0.50f, mid - h * 1.25f), new PointF(left + width * 0.55f, mid + h * 0.35f),
                new PointF(left + width * 0.58f, mid), new PointF(bounds.Right, mid)
            };
            using (var glow = new Pen(Color.FromArgb(22, Pulse), 7f) { LineJoin = LineJoin.Round })
            using (var pen = new Pen(Color.FromArgb(70, Pulse), 1.6f) { LineJoin = LineJoin.Round })
            {
                g.DrawLines(glow, points);
                g.DrawLines(pen, points);
            }
        }

        /// <summary>
        /// Small filled dot used as a menu or legend image.
        /// </summary>
        public static Bitmap CreateDot(Color color, int size = 16)
        {
            var bitmap = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(color))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(brush, size * 0.25f, size * 0.25f, size * 0.5f, size * 0.5f);
            }
            return bitmap;
        }
    }
}
