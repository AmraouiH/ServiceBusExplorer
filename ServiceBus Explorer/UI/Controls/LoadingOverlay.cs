using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ServiceBusExplorer.UI.Controls
{
    /// <summary>
    /// Modern loading overlay drawn over the whole tool: blurred and darkened snapshot of the screen, a centered card
    /// with an animated spinner, the current step, a progress bar (indeterminate or with a percentage), the elapsed
    /// time and a Stop button.
    /// </summary>
    [DesignerCategory("Code")]
    public class LoadingOverlay : Control
    {
        private const int CardWidth = 460;
        private const int CardHeight = 196;
        private const int SpinnerSize = 78;
        private readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();

        private readonly Timer _timer = new Timer { Interval = 16 };
        private readonly ModernButton _stop;
        private Bitmap _backdrop;
        private DateTime _started;
        private float _shimmer;
        private string _title;
        private string _detail;
        private int? _percent;
        private bool _stopping;

        public event EventHandler StopRequested;

        public LoadingOverlay()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Dock = DockStyle.Fill;
            Visible = false;
            Cursor = Cursors.WaitCursor;

            _stop = new ModernButton { Variant = ButtonVariant.Secondary, Glyph = IconKind.Stop, AutoSize = true, MinimumSize = new Size(110, 32), Height = 32, Cursor = Cursors.Hand };
            _stop.Click += (s, e) => StopRequested?.Invoke(this, EventArgs.Empty);
            Controls.Add(_stop);

            _timer.Tick += (s, e) =>
            {
                _shimmer = (_shimmer + 0.012f) % 1.4f;
                Invalidate(Rectangle.Inflate(CardBounds, 24, 24));
            };
        }

        /// <summary>Texts of the overlay (localized by the host).</summary>
        public string StopText { get => _stop.Text; set => _stop.Text = value; }
        public string StoppingText { get; set; } = "Stopping…";
        public string ElapsedFormat { get; set; } = "{0}";
        public string LongRunningText { get; set; }

        public bool IsRunning => Visible;

        private Rectangle CardBounds => new Rectangle((Width - CardWidth) / 2, (Height - CardHeight) / 2, CardWidth, CardHeight);

        /// <summary>Shows the overlay over <paramref name="host"/>.</summary>
        public void Start(Control host, string title, bool cancelable)
        {
            _title = title;
            _detail = null;
            _percent = null;
            _stopping = false;
            _started = DateTime.Now;
            _clock.Restart();
            _stop.Visible = cancelable;
            _stop.Enabled = true;

            CaptureBackdrop(host);

            // Not docked: docking would keep the header out, and it must stay unreachable during a task.
            Dock = DockStyle.None;
            Bounds = host.ClientRectangle;
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Visible = true;
            BringToFront();
            LayoutStop();
            Focus();
            _timer.Start();
            Invalidate();
        }

        public void SetProgress(string detail, int? percent)
        {
            if (!string.IsNullOrEmpty(detail)) _detail = detail;
            _percent = percent.HasValue ? Math.Max(0, Math.Min(100, percent.Value)) : (int?)null;
            Invalidate(CardBounds);
        }

        public void SetStopping()
        {
            _stopping = true;
            _stop.Enabled = false;
            Invalidate(CardBounds);
        }

        public void Finish()
        {
            _timer.Stop();
            _clock.Stop();
            Visible = false;
            _backdrop?.Dispose();
            _backdrop = null;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutStop();
        }

        private void LayoutStop()
        {
            var card = CardBounds;
            var size = _stop.GetPreferredSize(Size.Empty);
            _stop.Size = new Size(Math.Max(110, size.Width), 32);
            _stop.Location = new Point(card.Right - _stop.Width - 22, card.Bottom - _stop.Height - 18);
        }

        /// <summary>Blurred, darkened snapshot of the tool (downscale + bilinear upscale = cheap blur).</summary>
        private void CaptureBackdrop(Control host)
        {
            _backdrop?.Dispose();
            _backdrop = null;
            if (host.Width <= 0 || host.Height <= 0) return;

            try
            {
                using (var snapshot = new Bitmap(host.Width, host.Height))
                {
                    host.DrawToBitmap(snapshot, new Rectangle(Point.Empty, host.Size));
                    var small = new Bitmap(Math.Max(1, host.Width / 10), Math.Max(1, host.Height / 10));
                    using (var g = Graphics.FromImage(small))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.DrawImage(snapshot, new Rectangle(Point.Empty, small.Size));
                    }

                    _backdrop = new Bitmap(host.Width, host.Height);
                    using (var g = Graphics.FromImage(_backdrop))
                    using (small)
                    using (var veil = new SolidBrush(Color.FromArgb(120, Theme.HeaderStart)))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(small, new Rectangle(Point.Empty, _backdrop.Size));
                        g.FillRectangle(veil, 0, 0, _backdrop.Width, _backdrop.Height);
                    }
                }
            }
            catch
            {
                // Snapshot unavailable (minimized window…): plain veil.
                _backdrop = null;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (_backdrop != null) g.DrawImageUnscaled(_backdrop, 0, 0);
            else g.Clear(Theme.Blend(Theme.Canvas, Theme.HeaderStart, 0.45f));

            g.SmoothingMode = SmoothingMode.AntiAlias;
            var card = CardBounds;

            // Shadow + card
            for (var i = 1; i <= 4; i++)
            {
                using (var shadow = Theme.RoundedRect(new RectangleF(card.X - i, card.Y - i + 4, card.Width + 2 * i, card.Height + 2 * i), 16 + i))
                using (var brush = new SolidBrush(Color.FromArgb(14, 0, 0, 0)))
                {
                    g.FillPath(brush, shadow);
                }
            }
            using (var path = Theme.RoundedRect(card, 14))
            using (var fill = new SolidBrush(Theme.Card))
            {
                g.FillPath(fill, path);
            }

            // Spinner: the "AH" icon in motion
            AnimatedLogo.Paint(g, new PointF(card.X + 18 + SpinnerSize / 2f, card.Y + 22 + SpinnerSize / 2f), SpinnerSize, _clock.Elapsed.TotalSeconds);

            // Texts
            var textX = card.X + 18 + SpinnerSize + 14;
            var textWidth = card.Right - 24 - textX;
            TextRenderer.DrawText(g, _stopping ? StoppingText : _title, Theme.CardTitle, new Rectangle(textX, card.Y + 28, textWidth, 24), Theme.Text,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, _detail ?? string.Empty, Theme.Base, new Rectangle(textX, card.Y + 56, textWidth, 36), Theme.Muted,
                TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);

            // Progress bar
            var bar = new RectangleF(textX, card.Y + 100, textWidth, 6);
            using (var trackPath = Theme.RoundedRect(bar, 3))
            using (var trackBrush = new SolidBrush(Theme.Divider))
            {
                g.FillPath(trackBrush, trackPath);
            }
            var previousClip = g.Clip;
            using (var clip = Theme.RoundedRect(bar, 3))
            {
                g.SetClip(clip);
                RectangleF filled;
                if (_percent.HasValue)
                {
                    filled = new RectangleF(bar.X, bar.Y, Math.Max(6, bar.Width * _percent.Value / 100f), bar.Height);
                }
                else
                {
                    var width = bar.Width * 0.35f;
                    filled = new RectangleF(bar.X - width + (bar.Width + width) * (_shimmer / 1.4f) * 1.0f, bar.Y, width, bar.Height);
                }
                if (filled.Width > 0)
                {
                    using (var brush = new LinearGradientBrush(new RectangleF(filled.X - 1, filled.Y, filled.Width + 2, filled.Height), Theme.Teal, Theme.Accent, LinearGradientMode.Horizontal))
                    {
                        g.FillRectangle(brush, filled);
                    }
                }
                g.Clip = previousClip;
            }

            // Elapsed time (+ hint when it takes long)
            var elapsed = DateTime.Now - _started;
            var elapsedText = string.Format(ElapsedFormat, elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"m\:ss"))
                              + (_percent.HasValue ? $" · {_percent}%" : string.Empty);
            TextRenderer.DrawText(g, elapsedText, Theme.Small, new Point(textX, card.Y + 116), Theme.Subtle, TextFormatFlags.NoPadding);
            if (elapsed.TotalSeconds >= 8 && _stop.Visible && !_stopping && !string.IsNullOrEmpty(LongRunningText))
            {
                TextRenderer.DrawText(g, LongRunningText, Theme.Small, new Rectangle(card.X + 28, card.Bottom - 44, _stop.Left - card.X - 40, 30), Theme.WarningText,
                    TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape && _stop.Visible && _stop.Enabled) StopRequested?.Invoke(this, EventArgs.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _backdrop?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
