using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.UI;
using ServiceBusExplorer.UI.Controls;

namespace ServiceBusExplorer.Forms
{
    /// <summary>
    /// Large, resizable window to read a long error or message comfortably
    /// </summary>
    [DesignerCategory("Code")]
    public class TextViewerDialog : ThemedDialog
    {
        private TextViewerDialog(string title, string subtitle, string text)
            : base(title, subtitle)
        {
            Size = new Size(980, 640);
            MinimumSize = new Size(600, 400);
            Resizable = true;

            var box = new TextBox
            {
                Text = (text ?? string.Empty).Replace("\r\n", "\n").Replace("\n", Environment.NewLine),
                Multiline = true,
                ReadOnly = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 10F),
                BackColor = Theme.Card,
                ForeColor = Theme.Text,
                Dock = DockStyle.Fill
            };
            box.Disposed += (s, e) => box.Font.Dispose();

            var card = new CardPanel { Dock = DockStyle.Fill };
            card.Controls.Add(box);
            Body.Controls.Add(card);

            var close = AddFooterButton(Strings.Close, ButtonVariant.Primary, DialogResult.OK);
            var copy = AddFooterButton(Strings.CopyDetails, ButtonVariant.Secondary, DialogResult.None, IconKind.Copy);
            copy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(box.Text);
                    copy.Text = Strings.Copied;
                    copy.Glyph = IconKind.Check;
                }
                catch (Exception)
                {
                    // The clipboard is locked by another application: nothing to copy this time
                }
            };

            var wrap = new CheckBox
            {
                Text = Strings.WordWrap,
                Checked = true,
                AutoSize = true,
                BackColor = FooterBack,
                ForeColor = Theme.Text,
                Margin = new Padding(0, 8, 16, 0)
            };
            wrap.CheckedChanged += (s, e) =>
            {
                box.WordWrap = wrap.Checked;
                box.ScrollBars = wrap.Checked ? ScrollBars.Vertical : ScrollBars.Both;
            };
            Footer.Controls.Add(wrap);

            AcceptButton = close;
            CancelButton = close;
            Shown += (s, e) =>
            {
                box.SelectionStart = 0;
                box.SelectionLength = 0;
            };
        }

        public static void Show(IWin32Window owner, string title, string subtitle, string text)
        {
            using (var dialog = new TextViewerDialog(title, subtitle, text))
            {
                dialog.ShowModal(owner);
            }
        }
    }
}
