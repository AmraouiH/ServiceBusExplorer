using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.ServiceModel;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.UI;
using ServiceBusExplorer.UI.Controls;
using Microsoft.Xrm.Sdk;
using Label = System.Windows.Forms.Label;

namespace ServiceBusExplorer.Forms
{
    public enum MessageKind
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Message popup with the look of the tool, used instead of the system message box.
    /// Errors show a readable explanation, the technical details can be unfolded below it.
    /// </summary>
    [DesignerCategory("Code")]
    public class MessageDialog : ThemedDialog
    {
        private const int DialogWidth = 580;
        private const int BadgeSize = 48;
        private const int TextLeft = 28 + BadgeSize + 18;
        private const int DetailsHeight = 200;
        private const int PrivilegeDeniedCode = unchecked((int)0x80040220);

        private readonly Panel detailsHost;
        private readonly LinkLabel toggle;
        private readonly int collapsedHeight;

        private MessageDialog(MessageKind kind, string title, string message, string details, string question, string primary, string secondary)
            : base(title, KindColor(kind))
        {
            Body.BackColor = Theme.Card;
            Body.Padding = new Padding(0);

            var textWidth = DialogWidth - TextLeft - 30;
            var badge = new Badge(kind) { Location = new Point(28, 10), Size = new Size(BadgeSize, BadgeSize) };
            var titleLabel = new Label
            {
                Text = title,
                Font = Theme.CardTitle,
                ForeColor = Theme.Text,
                BackColor = Theme.Card,
                Location = new Point(TextLeft, 12),
                UseMnemonic = false
            };
            titleLabel.Size = new Size(textWidth, titleLabel.GetPreferredSize(new Size(textWidth, 0)).Height);

            var text = new Label
            {
                Text = message,
                Font = Theme.Base,
                Location = new Point(TextLeft, titleLabel.Bottom + 8),
                BackColor = Theme.Card,
                ForeColor = Theme.Muted,
                UseMnemonic = false
            };
            // The label computes its own wrapped height, so the text is never cut
            text.Size = new Size(textWidth, text.GetPreferredSize(new Size(textWidth, 0)).Height + 2);
            Body.Controls.Add(badge);
            Body.Controls.Add(titleLabel);
            Body.Controls.Add(text);

            var bottom = Math.Max(badge.Bottom, text.Bottom);
            if (!string.IsNullOrEmpty(question))
            {
                var questionLabel = new Label
                {
                    Text = question,
                    Font = Theme.Semibold,
                    Location = new Point(TextLeft, bottom + 12),
                    BackColor = Theme.Card,
                    ForeColor = Theme.Text,
                    UseMnemonic = false
                };
                questionLabel.Size = new Size(textWidth, questionLabel.GetPreferredSize(new Size(textWidth, 0)).Height + 2);
                Body.Controls.Add(questionLabel);
                bottom = questionLabel.Bottom;
            }

            if (!string.IsNullOrEmpty(details))
            {
                toggle = new LinkLabel
                {
                    Text = "▸  " + Strings.ShowDetails,
                    Font = Theme.Semibold,
                    AutoSize = true,
                    Location = new Point(TextLeft, bottom + 14),
                    BackColor = Theme.Card,
                    LinkColor = Theme.Accent,
                    ActiveLinkColor = Theme.AccentPressed,
                    LinkBehavior = LinkBehavior.HoverUnderline
                };
                toggle.LinkClicked += (s, e) => ToggleDetails();
                Body.Controls.Add(toggle);

                var expand = new LinkLabel
                {
                    Text = "↗  " + Strings.ExpandDetails,
                    Font = Theme.Semibold,
                    AutoSize = true,
                    BackColor = Theme.Card,
                    LinkColor = Theme.Accent,
                    ActiveLinkColor = Theme.AccentPressed,
                    LinkBehavior = LinkBehavior.HoverUnderline
                };
                expand.Location = new Point(TextLeft + textWidth - expand.PreferredWidth, toggle.Top);
                expand.LinkClicked += (s, e) => TextViewerDialog.Show(this, Strings.DetailsViewerTitle, title,
                    message + Environment.NewLine + Environment.NewLine + details);
                Body.Controls.Add(expand);
                bottom = toggle.Bottom;

                var box = new TextBox
                {
                    Text = details,
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    BorderStyle = BorderStyle.None,
                    Font = Theme.Mono,
                    BackColor = Theme.SameBack,
                    ForeColor = Theme.Muted,
                    Dock = DockStyle.Fill,
                    TabStop = false
                };
                detailsHost = new RoundedPanel
                {
                    Location = new Point(TextLeft, bottom + 10),
                    Size = new Size(textWidth, DetailsHeight),
                    Padding = new Padding(10, 8, 4, 8),
                    BackColor = Theme.SameBack,
                    Visible = false
                };
                detailsHost.Controls.Add(box);
                Body.Controls.Add(detailsHost);
            }

            collapsedHeight = ChromeHeight + bottom + 26;
            ClientSize = new Size(DialogWidth, collapsedHeight);

            ModernButton accept;
            if (primary == null)
            {
                accept = AddFooterButton(Strings.Close, ButtonVariant.Primary, DialogResult.OK);
                CancelButton = accept;
            }
            else
            {
                accept = AddFooterButton(primary, ButtonVariant.Primary, DialogResult.Yes);
                CancelButton = AddFooterButton(secondary ?? Strings.Close, ButtonVariant.Secondary, DialogResult.No);
            }
            AcceptButton = accept;

            if (!string.IsNullOrEmpty(details))
            {
                var copy = AddFooterButton(Strings.CopyDetails, ButtonVariant.Ghost, DialogResult.None, IconKind.Copy);
                copy.Click += (s, e) =>
                {
                    try
                    {
                        Clipboard.SetText(message + Environment.NewLine + Environment.NewLine + details);
                        copy.Text = Strings.Copied;
                        copy.Glyph = IconKind.Check;
                    }
                    catch (Exception)
                    {
                        // The clipboard is locked by another application: nothing to copy this time
                    }
                };
            }
        }

        private void ToggleDetails()
        {
            var show = !detailsHost.Visible;
            detailsHost.Visible = show;
            toggle.Text = (show ? "▾  " : "▸  ") + (show ? Strings.HideDetails : Strings.ShowDetails);
            ClientSize = new Size(DialogWidth, collapsedHeight + (show ? DetailsHeight + 10 : 0));
        }

        private static Color KindColor(MessageKind kind)
        {
            switch (kind)
            {
                case MessageKind.Error:
                    return Theme.Danger;

                case MessageKind.Warning:
                    return Theme.Diff;

                case MessageKind.Success:
                    return Theme.Success;

                default:
                    return Theme.Accent;
            }
        }

        public static void Show(IWin32Window owner, MessageKind kind, string title, string message, string details = null)
        {
            using (var dialog = new MessageDialog(kind, title, message, details, null, null, null))
            {
                dialog.ShowModal(owner);
            }
        }

        /// <summary>
        /// Shows a message ending with a question; returns true when the user picks the primary button
        /// </summary>
        public static bool Ask(IWin32Window owner, MessageKind kind, string title, string message, string details, string question, string primary, string secondary)
        {
            using (var dialog = new MessageDialog(kind, title, message, details, question, primary, secondary))
            {
                return dialog.ShowModal(owner) == DialogResult.Yes;
            }
        }

        /// <summary>
        /// Shows an error with an explanation of what to do when the cause is known (missing privileges)
        /// </summary>
        public static void ShowError(IWin32Window owner, Exception error)
        {
            var nl = Environment.NewLine;
            if (error is Services.Azure.AzureRequestException azure)
            {
                Show(owner, MessageKind.Error, Strings.SomethingWentWrong, azure.Message, azure.Details);
                return;
            }

            if (!IsPrivilegeError(error))
            {
                Show(owner, MessageKind.Error, Strings.SomethingWentWrong, error.Message, error.ToString());
                return;
            }

            var privilege = Regex.Match(error.ToString(), @"\bprv[A-Z]\w+");
            var message =
                Strings.PrivilegeIntro
                + (privilege.Success ? nl + nl + Strings.MissingPrivilege(privilege.Value) : string.Empty)
                + nl + nl
                + Strings.PrivilegeSearch + nl
                + Strings.PrivilegeRestore + nl + nl
                + Strings.PrivilegeAskAdmin;

            Show(owner, MessageKind.Error, Strings.MissingPrivileges, message, error.Message);
        }

        private static bool IsPrivilegeError(Exception error)
        {
            for (var current = error; current != null; current = current.InnerException)
            {
                var fault = current as FaultException<OrganizationServiceFault>;
                if (fault?.Detail != null && fault.Detail.ErrorCode == PrivilegeDeniedCode)
                {
                    return true;
                }
                if (Regex.IsMatch(current.Message ?? string.Empty, @"privilege|\bprv[A-Z]\w+|SecLib::|access is denied", RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Round colored badge with a halo showing the kind of message
        /// </summary>
        [DesignerCategory("Code")]
        private class Badge : Control
        {
            private readonly MessageKind kind;

            public Badge(MessageKind kind)
            {
                this.kind = kind;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Card);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                var color = KindColor(kind);
                using (var halo = new SolidBrush(Theme.Blend(Color.White, color, 0.08f)))
                using (var fill = new SolidBrush(Theme.Blend(Color.White, color, 0.18f)))
                {
                    g.FillEllipse(halo, 0, 0, Width - 1, Height - 1);
                    g.FillEllipse(fill, 7, 7, Width - 15, Height - 15);
                }

                IconKind icon;
                switch (kind)
                {
                    case MessageKind.Error:
                        icon = IconKind.Close;
                        break;

                    case MessageKind.Success:
                        icon = IconKind.Check;
                        break;

                    case MessageKind.Warning:
                        icon = IconKind.Alert;
                        break;

                    default:
                        icon = IconKind.Info;
                        break;
                }
                LineIcons.Draw(g, icon, new RectangleF((Width - 22) / 2f, (Height - 22) / 2f, 22, 22), color);
            }
        }

        /// <summary>
        /// Light rounded box holding the technical details
        /// </summary>
        [DesignerCategory("Code")]
        private class RoundedPanel : Panel
        {
            public RoundedPanel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Parent?.BackColor ?? Theme.Card);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Theme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8))
                using (var fill = new SolidBrush(BackColor))
                using (var pen = new Pen(Theme.Border))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(pen, path);
                }
            }
        }
    }
}
