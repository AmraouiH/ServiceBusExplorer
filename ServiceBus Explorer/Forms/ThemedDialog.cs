using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ServiceBusExplorer.UI;
using ServiceBusExplorer.UI.Controls;

namespace ServiceBusExplorer.Forms
{
    /// <summary>
    /// Base of the popups: borderless window with a soft shadow (rounded corners on Windows 11), a gradient
    /// or compact header with its own close button, a light footer for the buttons, a short fade-in and
    /// a dimmed backdrop over the XrmToolBox window while the popup is open.
    /// </summary>
    [DesignerCategory("Code")]
    public class ThemedDialog : Form
    {
        private const int BannerHeight = 72;
        private const int CompactHeight = 38;
        private const int FooterHeight = 64;

        protected readonly Panel Body;
        protected readonly FlowLayoutPanel Footer;

        private readonly DialogHeader header;
        private readonly Panel footerHost;
        private readonly SizeGrip grip;
        private Rectangle restoreBounds;
        private bool maximized;
        private bool resizable;
        private Timer fadeTimer;

        /// <summary>Popup with a gradient banner showing a title and a subtitle</summary>
        public ThemedDialog(string title, string subtitle)
            : this(title, subtitle, false, Theme.Accent)
        {
        }

        /// <summary>Popup with a compact white header crossed by a colored line</summary>
        protected ThemedDialog(string title, Color accent)
            : this(title, null, true, accent)
        {
        }

        private ThemedDialog(string title, string subtitle, bool compact, Color accent)
        {
            Text = title;
            Font = Theme.Base;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            ShowInTaskbar = false;
            KeyPreview = true;

            // The 1px padding shows the form background as a thin border around the content
            Padding = new Padding(1);
            BackColor = Color.FromArgb(196, 202, 214);

            header = new DialogHeader(this, compact, accent)
            {
                Dock = DockStyle.Top,
                Height = compact ? CompactHeight : BannerHeight,
                Title = title,
                Subtitle = subtitle
            };

            Body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 10), BackColor = Theme.Canvas };

            footerHost = new Panel { Dock = DockStyle.Bottom, Height = FooterHeight, BackColor = FooterBack };
            footerHost.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 0, 0, footerHost.Width, 0);
            };
            Footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(14, 15, 10, 0),
                BackColor = FooterBack
            };
            grip = new SizeGrip(this) { Dock = DockStyle.Right, Width = 16, Visible = false };
            footerHost.Controls.Add(Footer);
            footerHost.Controls.Add(grip);

            Controls.Add(Body);
            Controls.Add(footerHost);
            Controls.Add(header);
        }

        internal static readonly Color FooterBack = Color.FromArgb(249, 250, 252);

        /// <summary>Shows the size grip and the maximize button, and allows double-clicking the header</summary>
        protected bool Resizable
        {
            get => resizable;
            set
            {
                resizable = value;
                grip.Visible = value;
                header.ShowMaximize = value;
            }
        }

        protected ModernButton AddFooterButton(string text, ButtonVariant variant, DialogResult result, IconKind glyph = IconKind.None)
        {
            var button = new ModernButton
            {
                Text = text,
                Variant = variant,
                Glyph = glyph,
                DialogResult = result,
                AutoSize = true,
                MinimumSize = new Size(100, 34),
                Height = 34,
                Margin = new Padding(8, 0, 0, 0),
                Radius = 8
            };
            Footer.Controls.Add(button);
            return button;
        }

        /// <summary>Height taken by the header, the footer and the border, to size the popup from its content</summary>
        protected int ChromeHeight => header.Height + FooterHeight + Padding.Vertical;

        /// <summary>
        /// Shows the popup as a modal window above a dimmed copy of the window that owns it
        /// </summary>
        public DialogResult ShowModal(IWin32Window owner)
        {
            var host = (owner as Control)?.TopLevelControl as Form ?? owner as Form;
            Backdrop backdrop = null;
            if (host != null && host.Visible && host.WindowState != FormWindowState.Minimized)
            {
                backdrop = new Backdrop(host);
                backdrop.Show(host);
            }

            try
            {
                return ShowDialog(owner);
            }
            finally
            {
                if (backdrop != null)
                {
                    backdrop.Close();
                    backdrop.Dispose();
                }
            }
        }

        #region Window chrome

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                if (!Native.IsCompositionEnabled())
                {
                    cp.ClassStyle |= Native.CS_DROPSHADOW;
                }
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.ApplyModernFrame(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCPAINT)
            {
                Native.ApplyModernFrame(Handle);
            }
            base.WndProc(ref m);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (DesignMode)
            {
                return;
            }

            // Short fade-in
            Opacity = 0;
            fadeTimer = new Timer { Interval = 15 };
            fadeTimer.Tick += (s, a) =>
            {
                var next = Opacity + 0.2;
                if (next >= 1)
                {
                    fadeTimer.Stop();
                    Opacity = 1;
                    return;
                }
                Opacity = next;
            };
            fadeTimer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            fadeTimer?.Stop();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fadeTimer?.Dispose();
            }
            base.Dispose(disposing);
        }

        internal void BeginDrag()
        {
            if (maximized)
            {
                return;
            }
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        internal void ToggleMaximize()
        {
            if (!resizable)
            {
                return;
            }

            if (maximized)
            {
                Bounds = restoreBounds;
            }
            else
            {
                restoreBounds = Bounds;
                Bounds = Screen.FromHandle(Handle).WorkingArea;
            }
            maximized = !maximized;
            grip.Visible = !maximized;
            header.IsMaximized = maximized;
        }

        internal void CloseFromHeader()
        {
            DialogResult = CancelButton is Button cancel && cancel.DialogResult != DialogResult.None
                ? cancel.DialogResult
                : DialogResult.Cancel;
            Close();
        }

        #endregion

        #region Parts

        /// <summary>
        /// Gradient banner (logo, title, subtitle) or compact white header, with the caption buttons
        /// </summary>
        [DesignerCategory("Code")]
        private class DialogHeader : Control
        {
            private readonly ThemedDialog dialog;
            private readonly bool compact;
            private readonly Color accent;
            private readonly CaptionButton close;
            private readonly CaptionButton maximize;

            public DialogHeader(ThemedDialog dialog, bool compact, Color accent)
            {
                this.dialog = dialog;
                this.compact = compact;
                this.accent = accent;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

                close = new CaptionButton(IconKind.Close, !compact, true);
                close.Click += (s, e) => dialog.CloseFromHeader();
                maximize = new CaptionButton(IconKind.Maximize, !compact, false) { Visible = false };
                maximize.Click += (s, e) => dialog.ToggleMaximize();
                Controls.Add(close);
                Controls.Add(maximize);
            }

            public string Title { get; set; }

            public string Subtitle { get; set; }

            public bool ShowMaximize
            {
                set
                {
                    maximize.Visible = value;
                    LayoutButtons();
                }
            }

            public bool IsMaximized
            {
                set => maximize.Icon = value ? IconKind.Restore : IconKind.Maximize;
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                LayoutButtons();
            }

            private void LayoutButtons()
            {
                var top = compact ? 4 : 10;
                close.Bounds = new Rectangle(Width - 40, top, 32, 32);
                maximize.Bounds = new Rectangle(Width - 74, top, 32, 32);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left)
                {
                    return;
                }
                if (e.Clicks == 2)
                {
                    dialog.ToggleMaximize();
                }
                else
                {
                    dialog.BeginDrag();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                var bounds = ClientRectangle;
                if (bounds.Width <= 0) return;

                if (compact)
                {
                    PaintCompact(g, bounds);
                }
                else
                {
                    PaintBanner(g, bounds);
                }
            }

            private void PaintCompact(Graphics g, Rectangle bounds)
            {
                g.Clear(Theme.Card);

                var line = new Rectangle(0, 0, bounds.Width, 4);
                using (var brush = new LinearGradientBrush(line, accent, Theme.Blend(accent, Color.White, 0.35f), LinearGradientMode.Horizontal))
                {
                    g.FillRectangle(brush, line);
                }

                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var logo = Theme.CreateLogo(18))
                {
                    g.DrawImage(logo, 18, 13);
                }
                TextRenderer.DrawText(g, Localization.Strings.AppTitle, Theme.SmallBold, new Point(42, 16), Theme.Muted, TextFormatFlags.NoPadding);
            }

            private void PaintBanner(Graphics g, Rectangle bounds)
            {
                using (var brush = new LinearGradientBrush(bounds, Theme.HeaderStart, Theme.HeaderEnd, LinearGradientMode.Horizontal))
                {
                    g.FillRectangle(brush, bounds);
                }

                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var violet = new SolidBrush(Color.FromArgb(34, 139, 128, 255)))
                using (var teal = new SolidBrush(Color.FromArgb(30, Theme.Teal)))
                using (var coral = new SolidBrush(Color.FromArgb(26, Theme.Slave)))
                {
                    g.FillEllipse(violet, bounds.Right - 330, -70, 160, 160);
                    g.FillEllipse(teal, bounds.Right - 230, -50, 130, 130);
                    g.FillEllipse(coral, bounds.Right - 150, 14, 110, 110);
                }

                // Logo on a frosted tile
                var tile = new RectangleF(18, (Height - 44) / 2f - 1, 44, 44);
                using (var path = Theme.RoundedRect(tile, 12))
                using (var brush = new SolidBrush(Color.FromArgb(28, Color.White)))
                using (var pen = new Pen(Color.FromArgb(40, Color.White)))
                {
                    g.FillPath(brush, path);
                    g.DrawPath(pen, path);
                }
                using (var logo = Theme.CreateLogo(30))
                {
                    g.DrawImage(logo, tile.X + 7, tile.Y + 7);
                }

                var textWidth = Width - 76 - (maximize.Visible ? 90 : 56);
                TextRenderer.DrawText(g, Title, Theme.HeaderTitle, new Rectangle(76, 14, textWidth, 26), Color.White,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, Subtitle, Theme.Base, new Rectangle(77, 42, textWidth, 18), Color.FromArgb(201, 199, 242),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

                var line = new Rectangle(0, Height - 3, Width, 3);
                using (var accentLine = new LinearGradientBrush(line, Theme.Teal, Theme.Accent, LinearGradientMode.Horizontal))
                {
                    g.FillRectangle(accentLine, line);
                }
            }
        }

        /// <summary>
        /// Round header button (close, maximize) drawn over the header
        /// </summary>
        [DesignerCategory("Code")]
        private class CaptionButton : Control
        {
            private readonly bool onDark;
            private readonly bool isClose;
            private IconKind icon;
            private bool hover;
            private bool pressed;

            public CaptionButton(IconKind icon, bool onDark, bool isClose)
            {
                this.icon = icon;
                this.onDark = onDark;
                this.isClose = isClose;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                Cursor = Cursors.Hand;
            }

            public IconKind Icon
            {
                get => icon;
                set { icon = value; Invalidate(); }
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                Color fore;
                if (hover || pressed)
                {
                    Color back;
                    if (isClose)
                    {
                        back = pressed ? Color.FromArgb(196, 43, 28) : Color.FromArgb(232, 17, 35);
                        fore = Color.White;
                    }
                    else if (onDark)
                    {
                        back = Color.FromArgb(pressed ? 70 : 40, Color.White);
                        fore = Color.White;
                    }
                    else
                    {
                        back = pressed ? Color.FromArgb(226, 230, 238) : Color.FromArgb(238, 241, 246);
                        fore = Theme.Text;
                    }
                    using (var brush = new SolidBrush(back))
                    {
                        g.FillEllipse(brush, 1, 1, Width - 3, Height - 3);
                    }
                }
                else
                {
                    fore = onDark ? Color.FromArgb(220, 222, 255) : Theme.Muted;
                }

                LineIcons.Draw(g, icon, new RectangleF((Width - 16) / 2f, (Height - 16) / 2f, 16, 16), fore);
            }
        }

        /// <summary>
        /// Dotted corner used to resize the popup
        /// </summary>
        [DesignerCategory("Code")]
        private class SizeGrip : Control
        {
            private readonly Form form;
            private Point start;
            private Size startSize;
            private bool dragging;

            public SizeGrip(Form form)
            {
                this.form = form;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                BackColor = FooterBack;
                Cursor = Cursors.SizeNWSE;
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button == MouseButtons.Left)
                {
                    dragging = true;
                    start = Cursor.Position;
                    startSize = form.Size;
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (!dragging)
                {
                    return;
                }
                var position = Cursor.Position;
                form.Size = new Size(
                    Math.Max(form.MinimumSize.Width, startSize.Width + position.X - start.X),
                    Math.Max(form.MinimumSize.Height, startSize.Height + position.Y - start.Y));
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                dragging = false;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(BackColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(Theme.Subtle))
                {
                    for (var row = 0; row < 3; row++)
                    {
                        for (var col = 2 - row; col < 3; col++)
                        {
                            g.FillEllipse(brush, Width - 14 + col * 4, Height - 14 + row * 4, 2.4f, 2.4f);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Translucent layer dimming the window behind a popup
        /// </summary>
        [DesignerCategory("Code")]
        private class Backdrop : Form
        {
            public Backdrop(Form target)
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                BackColor = Theme.HeaderStart;
                Opacity = 0.32;
                Bounds = target.Bounds;
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                    return cp;
                }
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                Native.SetRoundCorners(Handle);
            }
        }

        private static class Native
        {
            public const int WM_NCPAINT = 0x0085;
            public const int WM_NCLBUTTONDOWN = 0x00A1;
            public const int HTCAPTION = 2;
            public const int CS_DROPSHADOW = 0x00020000;
            public const int WS_EX_TOOLWINDOW = 0x00000080;
            public const int WS_EX_NOACTIVATE = 0x08000000;

            private const int DWMWA_NCRENDERING_POLICY = 2;
            private const int DWMNCRP_ENABLED = 2;
            private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            private const int DWMWCP_ROUND = 2;

            [StructLayout(LayoutKind.Sequential)]
            private struct Margins
            {
                public int Left;
                public int Right;
                public int Top;
                public int Bottom;
            }

            [DllImport("dwmapi.dll")]
            private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

            [DllImport("dwmapi.dll")]
            private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

            [DllImport("dwmapi.dll")]
            private static extern int DwmIsCompositionEnabled(out bool enabled);

            [DllImport("user32.dll")]
            public static extern bool ReleaseCapture();

            [DllImport("user32.dll")]
            public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

            public static bool IsCompositionEnabled()
            {
                try
                {
                    bool enabled;
                    return Environment.OSVersion.Version.Major >= 6 && DwmIsCompositionEnabled(out enabled) == 0 && enabled;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            /// <summary>Native shadow of a borderless window, and rounded corners on Windows 11</summary>
            public static void ApplyModernFrame(IntPtr handle)
            {
                if (!IsCompositionEnabled())
                {
                    return;
                }
                try
                {
                    var policy = DWMNCRP_ENABLED;
                    DwmSetWindowAttribute(handle, DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));
                    var margins = new Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
                    DwmExtendFrameIntoClientArea(handle, ref margins);
                    SetRoundCorners(handle);
                }
                catch (Exception)
                {
                    // Older systems keep the square window
                }
            }

            public static void SetRoundCorners(IntPtr handle)
            {
                try
                {
                    var corner = DWMWCP_ROUND;
                    DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
                }
                catch (Exception)
                {
                    // Not available before Windows 11
                }
            }
        }

        #endregion
    }
}
