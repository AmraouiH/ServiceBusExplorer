using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ServiceBusExplorer.UI
{
    /// <summary>
    /// The "AH" icon of the tool in motion, used as the loading indicator: its four colored ring segments turn
    /// with an eased motion while their gaps open and close, and a light sweep crosses the letters.
    /// Each frame is drawn three times larger then reduced with a high quality filter, with a blurred glow
    /// and shadow, so the result stays smooth and sharp at any size.
    /// </summary>
    internal static class AnimatedLogo
    {
        private const int Supersample = 3;
        private const int GlowReduction = 8;

        /// <summary>Room around the icon so that the glow and the shadow fade out completely</summary>
        private const float Margin = 1.35f;
        private const string Letters = "ΛH";

        // Colors of the ring, clockwise from the top: red, gold, green, blue
        private static readonly Color[] RingColors =
        {
            Color.FromArgb(226, 30, 66),
            Color.FromArgb(232, 170, 40),
            Color.FromArgb(22, 163, 74),
            Color.FromArgb(24, 128, 200)
        };

        [ThreadStatic] private static Bitmap frame;
        [ThreadStatic] private static Bitmap glow;
        [ThreadStatic] private static GraphicsPath letters;
        [ThreadStatic] private static float lettersSize;

        /// <summary>Paints one frame. <paramref name="size"/> is the diameter of the area used by the animation.</summary>
        public static void Paint(Graphics g, PointF center, float size, double seconds)
        {
            var pixels = Math.Max(16, (int)Math.Ceiling(size * Margin * Supersample));
            frame = Ensure(frame, pixels);
            glow = Ensure(glow, Math.Max(4, pixels / GlowReduction));

            var segments = GetSegments(seconds);
            var layout = new Layout(pixels);

            PaintGlow(layout, segments);

            using (var fg = Graphics.FromImage(frame))
            {
                fg.Clear(Color.Transparent);
                fg.SmoothingMode = SmoothingMode.AntiAlias;
                fg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                fg.CompositingQuality = CompositingQuality.HighQuality;

                // Blurred glow and shadow: the small layer enlarged with a smooth filter
                fg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                fg.DrawImage(glow, new Rectangle(0, 0, pixels, pixels));

                PaintRing(fg, layout, segments);
                PaintDisc(fg, layout);
                PaintLetters(fg, layout, seconds);
            }

            var state = g.Save();
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            var drawn = size * Margin;
            g.DrawImage(frame, new RectangleF(center.X - drawn / 2f, center.Y - drawn / 2f, drawn, drawn));
            g.Restore(state);
        }

        private static Bitmap Ensure(Bitmap bitmap, int pixels)
        {
            if (bitmap != null && bitmap.Width == pixels)
            {
                return bitmap;
            }
            bitmap?.Dispose();
            return new Bitmap(pixels, pixels, PixelFormat.Format32bppPArgb);
        }

        /// <summary>Sizes of the parts of the icon for a frame of the given size</summary>
        private struct Layout
        {
            public Layout(float pixels)
            {
                Center = new PointF(pixels / 2f, pixels / 2f);
                pixels /= Margin;
                Size = pixels;
                RingWidth = pixels * 0.068f;
                RingRadius = pixels * 0.5f - pixels * 0.115f;
                DiscRadius = RingRadius - RingWidth * 0.5f - pixels * 0.032f;
            }

            public readonly float Size;
            public readonly PointF Center;
            public readonly float RingWidth;
            public readonly float RingRadius;
            public readonly float DiscRadius;

            public RectangleF RingBounds => new RectangleF(Center.X - RingRadius, Center.Y - RingRadius, RingRadius * 2, RingRadius * 2);

            public RectangleF DiscBounds => new RectangleF(Center.X - DiscRadius, Center.Y - DiscRadius, DiscRadius * 2, DiscRadius * 2);
        }

        /// <summary>Start angle and sweep of each segment of the ring at a given time</summary>
        private static PointF[] GetSegments(double seconds)
        {
            // Steady turn plus an eased push every 1.6 s
            const double period = 1.6;
            var turns = Math.Floor(seconds / period);
            var t = seconds / period - turns;
            var eased = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
            var rotation = (float)((seconds * 90 + (turns + eased) * 180) % 360);

            // Gaps between the segments open and close
            var gap = (float)(12 + 24 * (0.5 + 0.5 * Math.Sin(seconds * Math.PI * 1.25)));

            var segments = new PointF[RingColors.Length];
            for (var i = 0; i < segments.Length; i++)
            {
                segments[i] = new PointF(rotation - 90f + i * 90f + gap / 2f, 90f - gap);
            }
            return segments;
        }

        private static void PaintGlow(Layout layout, PointF[] segments)
        {
            var scale = (float)glow.Width / frame.Width;
            using (var gg = Graphics.FromImage(glow))
            {
                gg.Clear(Color.Transparent);
                gg.SmoothingMode = SmoothingMode.AntiAlias;
                gg.ScaleTransform(scale, scale);

                // Soft shadow under the disc, slightly lower
                var disc = layout.DiscBounds;
                disc.Inflate(layout.Size * 0.02f, layout.Size * 0.02f);
                disc.Offset(0, layout.Size * 0.035f);
                using (var shadow = new SolidBrush(Color.FromArgb(150, 10, 10, 20)))
                {
                    gg.FillEllipse(shadow, disc);
                }

                // Colored light around the ring
                for (var i = 0; i < segments.Length; i++)
                {
                    using (var pen = new Pen(Color.FromArgb(130, RingColors[i]), layout.RingWidth * 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        gg.DrawArc(pen, layout.RingBounds, segments[i].X, segments[i].Y);
                    }
                }
            }
        }

        private static void PaintRing(Graphics g, Layout layout, PointF[] segments)
        {
            var bounds = layout.RingBounds;
            var width = layout.RingWidth;

            for (var i = 0; i < segments.Length; i++)
            {
                var start = segments[i].X;
                var sweep = segments[i].Y;
                var tail = Theme.Blend(RingColors[i], Color.Black, 0.22f);
                var head = Theme.Blend(RingColors[i], Color.White, 0.18f);

                // Body: from the darker tail to the lighter head, like a comet
                using (var pen = new Pen(tail, width) { StartCap = LineCap.Round, EndCap = LineCap.Flat })
                {
                    g.DrawArc(pen, bounds, start, 1f);
                }
                const int steps = 28;
                for (var step = 0; step < steps; step++)
                {
                    var color = Theme.Blend(tail, head, step / (float)(steps - 1));
                    using (var pen = new Pen(color, width) { StartCap = LineCap.Flat, EndCap = LineCap.Flat })
                    {
                        g.DrawArc(pen, bounds, start + sweep * step / steps, sweep / steps + 0.6f);
                    }
                }
                using (var pen = new Pen(head, width) { StartCap = LineCap.Flat, EndCap = LineCap.Round })
                {
                    g.DrawArc(pen, bounds, start + sweep - 1f, 1f);
                }

                // Glossy line along the inner side, for a tube effect
                var inner = RectangleF.Inflate(bounds, -width * 0.18f, -width * 0.18f);
                using (var shine = new Pen(Color.FromArgb(80, Color.White), width * 0.22f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawArc(shine, inner, start + sweep * 0.12f, sweep * 0.76f);
                }
            }
        }

        private static void PaintDisc(Graphics g, Layout layout)
        {
            var bounds = layout.DiscBounds;
            var radius = layout.DiscRadius;
            var center = layout.Center;

            using (var path = new GraphicsPath())
            {
                path.AddEllipse(bounds);
                using (var brush = new PathGradientBrush(path)
                {
                    CenterPoint = new PointF(center.X - radius * 0.3f, center.Y - radius * 0.42f),
                    CenterColor = Color.FromArgb(86, 86, 94),
                    SurroundColors = new[] { Color.FromArgb(26, 26, 31) }
                })
                {
                    g.FillPath(brush, path);
                }

                // Glassy reflection on the upper half
                var state = g.Save();
                g.SetClip(path);
                var gloss = new RectangleF(center.X - radius * 0.86f, center.Y - radius * 1.02f, radius * 1.72f, radius * 1.1f);
                using (var brush = new LinearGradientBrush(gloss, Color.FromArgb(56, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical))
                {
                    g.FillEllipse(brush, gloss);
                }
                g.Restore(state);
            }

            // Light catching the top edge, dark outline
            var rim = RectangleF.Inflate(bounds, -layout.Size * 0.006f, -layout.Size * 0.006f);
            using (var brush = new LinearGradientBrush(bounds, Color.FromArgb(110, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical))
            using (var pen = new Pen(brush, layout.Size * 0.008f))
            {
                g.DrawEllipse(pen, rim);
            }
            using (var outline = new Pen(Color.FromArgb(200, 14, 14, 18), layout.Size * 0.008f))
            {
                g.DrawEllipse(outline, bounds);
            }
        }

        private static GraphicsPath GetLetters(Layout layout)
        {
            if (letters != null && Math.Abs(lettersSize - layout.Size) < 0.5f)
            {
                return letters;
            }

            letters?.Dispose();
            letters = new GraphicsPath();
            using (var family = new FontFamily("Segoe UI"))
            {
                letters.AddString(Letters, family, (int)FontStyle.Bold, layout.DiscRadius * 0.98f, PointF.Empty, StringFormat.GenericTypographic);
            }
            var box = letters.GetBounds();
            using (var matrix = new Matrix())
            {
                matrix.Translate(layout.Center.X - box.Width * 0.5f - box.X, layout.Center.Y - box.Height * 0.5f - box.Y);
                letters.Transform(matrix);
            }
            lettersSize = layout.Size;
            return letters;
        }

        private static void PaintLetters(Graphics g, Layout layout, double seconds)
        {
            var path = GetLetters(layout);
            var box = path.GetBounds();

            // Engraved look: dark shadow below, silver body, fine outline
            var state = g.Save();
            g.TranslateTransform(0, layout.Size * 0.012f);
            using (var shadow = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            {
                g.FillPath(shadow, path);
            }
            g.Restore(state);

            var fillBounds = RectangleF.Inflate(box, 0, 1);
            using (var fill = new LinearGradientBrush(fillBounds, Color.White, Color.FromArgb(150, 152, 162), LinearGradientMode.Vertical))
            {
                fill.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(255, 255, 255), Color.FromArgb(226, 228, 234), Color.FromArgb(160, 162, 172) },
                    Positions = new[] { 0f, 0.45f, 1f }
                };
                g.FillPath(fill, path);
            }
            using (var outline = new Pen(Color.FromArgb(70, 0, 0, 0), layout.Size * 0.004f))
            {
                g.DrawPath(outline, path);
            }

            // Light sweep crossing the letters during the first part of each 2.4 s cycle
            const double cycle = 2.4;
            var progress = (seconds % cycle) / 0.9;
            if (progress >= 1)
            {
                return;
            }

            var bandWidth = box.Width * 0.4f;
            var x = box.Left - bandWidth + (float)progress * (box.Width + bandWidth * 2);
            var band = new RectangleF(x - bandWidth / 2, box.Top - box.Height, bandWidth, box.Height * 3);

            state = g.Save();
            g.SetClip(path);
            g.TranslateTransform(layout.Center.X, layout.Center.Y);
            g.RotateTransform(20);
            g.TranslateTransform(-layout.Center.X, -layout.Center.Y);
            using (var shine = new LinearGradientBrush(band, Color.Transparent, Color.Transparent, LinearGradientMode.Horizontal))
            {
                shine.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(0, Color.White), Color.FromArgb(235, Color.White), Color.FromArgb(0, Color.White) },
                    Positions = new[] { 0f, 0.5f, 1f }
                };
                g.FillRectangle(shine, band);
            }
            g.Restore(state);
        }
    }
}
