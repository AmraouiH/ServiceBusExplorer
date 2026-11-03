using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.Models;
using ServiceBusExplorer.Services.Dataverse;
using ServiceBusExplorer.UI;
using ServiceBusExplorer.UI.Controls;

namespace ServiceBusExplorer.Forms
{
    /// <summary>Adds or edits an image of a step.</summary>
    [DesignerCategory("Code")]
    public class ImageEditorDialog : ThemedDialog
    {
        private readonly ImageInfo _image;
        private readonly ComboBox _type;
        private readonly TextBox _alias;
        private readonly TextBox _name;
        private readonly RadioButton _all;
        private readonly RadioButton _some;
        private readonly TextBox _columns;
        private readonly ModernButton _pick;
        private readonly Label _error;
        private readonly List<int> _types;

        public ImageEditorDialog(ImageInfo image, List<int> allowedTypes, EndpointService service, string table)
            : base(image.Id == Guid.Empty && string.IsNullOrEmpty(image.Alias) ? Strings.ImageEditorAdd : Strings.ImageEditorEdit, table ?? Strings.AllTables)
        {
            _image = image;
            _types = allowedTypes;
            ClientSize = new Size(600, 520);

            var card = new CardPanel { Dock = DockStyle.Fill };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Card };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, Margin = new Padding(0, 6, 0, 0) };
            foreach (var type in _types) _type.Items.Add(Strings.ImageTypeLabel(type));
            _type.SelectedIndex = Math.Max(0, _types.IndexOf(image.ImageType));

            _alias = new TextBox { Text = image.Alias, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0) };
            _name = new TextBox { Text = image.Name, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0) };

            var choices = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
            _all = new RadioButton { Text = Strings.ColumnsAll, AutoSize = true, Checked = image.AllColumns, ForeColor = Theme.Text };
            _some = new RadioButton { Text = Strings.ColumnsSelected, AutoSize = true, Checked = !image.AllColumns, ForeColor = Theme.Text, Margin = new Padding(14, 3, 0, 0) };
            choices.Controls.AddRange(new Control[] { _all, _some });
            _all.CheckedChanged += (s, e) => UpdateColumns();

            _columns = new TextBox { Text = image.Attributes, Dock = DockStyle.Fill, Multiline = true, Height = 76, ReadOnly = true, BackColor = Theme.SameBack, ScrollBars = ScrollBars.Vertical, Margin = new Padding(0, 6, 0, 0) };
            _pick = new ModernButton { Text = Strings.SelectColumns, Variant = ButtonVariant.Secondary, Glyph = IconKind.Fields, AutoSize = true, MinimumSize = new Size(110, 30), Height = 30, Margin = new Padding(0, 8, 0, 0), Enabled = table != null };
            _pick.Click += (s, e) => PickColumns(service, table);

            AddRow(grid, Strings.FieldImageType, _type);
            AddRow(grid, Strings.FieldAlias, _alias);
            AddRow(grid, Strings.FieldName, _name);
            AddRow(grid, Strings.FieldColumns, choices);
            AddRow(grid, string.Empty, _columns);
            AddRow(grid, string.Empty, _pick);

            _error = new Label { Dock = DockStyle.Bottom, Height = 30, ForeColor = Theme.WarningText, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            card.Controls.Add(grid);
            card.Controls.Add(_error);
            Body.Controls.Add(card);

            var ok = AddFooterButton(Strings.Ok, ButtonVariant.Primary, DialogResult.None, IconKind.Check);
            ok.Click += (s, e) => Accept();
            AcceptButton = ok;
            CancelButton = AddFooterButton(Strings.CancelAction, ButtonVariant.Secondary, DialogResult.Cancel);
            UpdateColumns();
        }

        private static void AddRow(TableLayoutPanel grid, string label, Control field)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Font = Theme.Semibold, ForeColor = Theme.Muted, Margin = new Padding(0, 10, 8, 0) });
            grid.Controls.Add(field);
        }

        private void UpdateColumns()
        {
            _columns.Enabled = _some.Checked;
            _pick.Enabled = _some.Checked;
            _error.Text = _all.Checked ? Strings.AllColumnsWarning : string.Empty;
        }

        private void PickColumns(EndpointService service, string table)
        {
            using (var picker = new ColumnPickerDialog(service, table, _columns.Text))
            {
                if (picker.ShowModal(this) == DialogResult.OK) _columns.Text = picker.Selected;
            }
        }

        private void Accept()
        {
            var alias = _alias.Text.Trim();
            if (alias.Length == 0)
            {
                _error.Text = Strings.ImageNeedsAlias;
                return;
            }
            if (_some.Checked && string.IsNullOrWhiteSpace(_columns.Text))
            {
                _error.Text = Strings.ImageNeedsColumns;
                return;
            }

            _image.ImageType = _types[_type.SelectedIndex];
            _image.Alias = alias;
            _image.Name = string.IsNullOrWhiteSpace(_name.Text) ? alias : _name.Text.Trim();
            _image.Attributes = _all.Checked ? null : StepRules.NormalizeColumns(_columns.Text);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
