using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.UI;
using ServiceBusExplorer.UI.Controls;

namespace ServiceBusExplorer.Forms
{
    /// <summary>Settings of the tool.</summary>
    [DesignerCategory("Code")]
    public class OptionsDialog : ThemedDialog
    {
        public OptionsDialog(PluginSettings settings)
            : base(Strings.OptionsTitle, Strings.OptionsSubtitle)
        {
            ClientSize = new Size(620, 540);

            var card = new CardPanel { Dock = DockStyle.Fill };
            var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Card };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var parallel = new NumericUpDown { Minimum = 1, Maximum = 16, Width = 70, Value = Math.Max(1, Math.Min(16, settings.ParallelRequests)), Margin = new Padding(0, 2, 0, 0) };
            var maxMessages = new NumericUpDown
            {
                Minimum = 1,
                Maximum = PluginSettings.MaxMessagesLimit,
                Increment = 50,
                Width = 90,
                Value = Math.Max(1, Math.Min(PluginSettings.MaxMessagesLimit, settings.MaxMessages)),
                Margin = new Padding(0, 2, 0, 0)
            };
            var tenant = new TextBox { Text = settings.TenantId, Dock = DockStyle.Fill };
            var client = new TextBox { Text = settings.ClientId, Dock = DockStyle.Fill };

            AddField(stack, Strings.OptionParallel, parallel, Strings.OptionParallelHint);
            AddField(stack, Strings.OptionMaxMessages, maxMessages, Strings.OptionMaxMessagesHint);
            AddField(stack, Strings.OptionTenant, tenant, Strings.OptionTenantHint);
            AddField(stack, Strings.OptionClient, client, Strings.OptionClientHint);
            card.Controls.Add(stack);
            Body.Controls.Add(card);

            var save = AddFooterButton(Strings.Save, ButtonVariant.Primary, DialogResult.None, IconKind.Check);
            CancelButton = AddFooterButton(Strings.CancelAction, ButtonVariant.Secondary, DialogResult.Cancel);
            AcceptButton = save;
            save.Click += (s, e) =>
            {
                settings.ParallelRequests = (int)parallel.Value;
                settings.MaxMessages = (int)maxMessages.Value;
                settings.TenantId = tenant.Text.Trim();
                settings.ClientId = client.Text.Trim();
                DialogResult = DialogResult.OK;
                Close();
            };
        }

        private static void AddField(TableLayoutPanel stack, string label, Control field, string hint)
        {
            stack.Controls.Add(new Label { Text = label, AutoSize = true, Font = Theme.Semibold, ForeColor = Theme.Muted, Margin = new Padding(0, 8, 0, 4) });
            stack.Controls.Add(field);
            stack.Controls.Add(new Label { Text = hint, AutoSize = true, MaximumSize = new Size(540, 0), Font = Theme.Small, ForeColor = Theme.Subtle, Margin = new Padding(0, 4, 0, 6) });
        }
    }
}
