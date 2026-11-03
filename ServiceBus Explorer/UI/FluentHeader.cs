using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ServiceBusExplorer.UI
{
    /// <summary>
    /// Header tokens and icons shared with Entity Fields Analyser (Fluent 2 look of Dynamics 365 / Power Platform).
    /// </summary>
    internal static class Fluent
    {
        public static readonly Color Surface = Color.White;
        public static readonly Color SurfacePressed = Color.FromArgb(224, 224, 224);
        public static readonly Color Stroke = Color.FromArgb(224, 224, 224);
        public static readonly Color StrokeSubtle = Color.FromArgb(240, 240, 240);
        public static readonly Color TextPrimary = Color.FromArgb(36, 36, 36);
        public static readonly Color TextSecondary = Color.FromArgb(97, 97, 97);
        public static readonly Color TextDisabled = Color.FromArgb(189, 189, 189);
        public static readonly Color TextOnBrand = Color.White;
        public static readonly Color Brand = Color.FromArgb(15, 108, 189);
        public static readonly Color BrandHover = Color.FromArgb(17, 94, 163);
        public static readonly Color BrandPressed = Color.FromArgb(12, 59, 94);
        public static readonly Color ExcelGreen = Color.FromArgb(16, 124, 65);
        public static readonly Color HeartRed = Color.FromArgb(196, 49, 75);

        /// <summary>Glyphs of the Windows icon fonts (Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10).</summary>
        public static class Glyph
        {
            public const char Close = '';
            public const char Play = '';
            public const char Export = '';
            public const char Help = '';
            public const char Heart = '';
            public const char Contact = '';
            public const char Settings = '';
            public const char Globe = '';
            public const char Document = '';
        }

        private static string _iconFontName;
        private static readonly Dictionary<string, Bitmap> IconCache = new Dictionary<string, Bitmap>();

        public static string IconFontName
        {
            get
            {
                if (_iconFontName == null)
                {
                    _iconFontName = "Segoe MDL2 Assets";
                    using (var fonts = new InstalledFontCollection())
                    {
                        foreach (var family in fonts.Families)
                        {
                            if (family.Name == "Segoe Fluent Icons")
                            {
                                _iconFontName = family.Name;
                                break;
                            }
                        }
                    }
                }
                return _iconFontName;
            }
        }

        /// <summary>Renders a glyph to a transparent bitmap (cached).</summary>
        public static Bitmap Icon(char glyph, Color color, int size = 16)
        {
            var key = $"{(int)glyph}-{color.ToArgb()}-{size}";
            if (IconCache.TryGetValue(key, out var bitmap)) return bitmap;

            bitmap = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bitmap))
            using (var font = new Font(IconFontName, size, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString(glyph.ToString(), font, brush, new RectangleF(0, 1, size, size), format);
            }

            IconCache[key] = bitmap;
            return bitmap;
        }

        public static void FillRounded(Graphics g, Color fill, RectangleF bounds, float radius)
        {
            using (var path = Theme.RoundedRect(bounds, radius))
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
            }
        }
    }

    /// <summary>
    /// Fluent command bar of Entity Fields Analyser: white surface, bottom hairline, rounded hover states.
    /// Items tagged "primary" are drawn as the accent button, items tagged "link" as quiet links.
    /// </summary>
    internal class FluentToolStripRenderer : ToolStripProfessionalRenderer
    {
        public const string Primary = "primary";
        public const string Link = "link";

        public FluentToolStripRenderer()
        {
            RoundedEdges = false;
        }

        private static bool Is(ToolStripItem item, string tag) => tag.Equals(item.Tag as string);

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(Fluent.Surface))
            {
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Fluent.Stroke))
            {
                e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
            }
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) => PaintBackground(e);

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) => PaintBackground(e);

        private static void PaintBackground(ToolStripItemRenderEventArgs e)
        {
            var item = e.Item;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(0.5f, 1.5f, item.Width - 1.5f, item.Height - 3f);
            const float radius = 4f;

            if (Is(item, Primary))
            {
                var fill = !item.Enabled ? Fluent.StrokeSubtle
                    : item.Pressed ? Fluent.BrandPressed
                    : item.Selected ? Fluent.BrandHover
                    : Fluent.Brand;
                Fluent.FillRounded(g, fill, bounds, radius);
                return;
            }

            if (!item.Enabled) return;

            if (item.Pressed) Fluent.FillRounded(g, Fluent.SurfacePressed, bounds, radius);
            else if (item.Selected) Fluent.FillRounded(g, Theme.Blend(Fluent.Surface, Fluent.SurfacePressed, 0.45f), bounds, radius);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (!e.Item.Enabled) e.TextColor = Fluent.TextDisabled;
            else if (Is(e.Item, Primary)) e.TextColor = Fluent.TextOnBrand;
            else if (Is(e.Item, Link)) e.TextColor = e.Item.Selected ? Fluent.Brand : Fluent.TextSecondary;
            else e.TextColor = Fluent.TextPrimary;

            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Fluent.TextSecondary;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (e.Item.Enabled || e.Image == null)
            {
                base.OnRenderItemImage(e);
                return;
            }

            // Disabled: tint the glyph with the disabled text color instead of the default grayscale
            var c = Fluent.TextDisabled;
            var matrix = new ColorMatrix(new[]
            {
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { c.R / 255f, c.G / 255f, c.B / 255f, 0, 1 }
            });
            using (var attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(matrix);
                var r = e.ImageRectangle;
                e.Graphics.DrawImage(e.Image, r, 0, 0, e.Image.Width, e.Image.Height, GraphicsUnit.Pixel, attributes);
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var x = e.Item.Width / 2;
            using (var pen = new Pen(Fluent.Stroke))
            {
                e.Graphics.DrawLine(pen, x, 8, x, e.Item.Height - 8);
            }
        }
    }
}
