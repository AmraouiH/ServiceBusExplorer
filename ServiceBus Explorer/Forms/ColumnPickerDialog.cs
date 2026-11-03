using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.Models;
using ServiceBusExplorer.Services.Dataverse;
using ServiceBusExplorer.UI;
using ServiceBusExplorer.UI.Controls;

namespace ServiceBusExplorer.Forms
{
    /// <summary>
    /// Searchable list of the columns of a table, used for the filtering columns of a step and the columns of an image.
    /// </summary>
    [DesignerCategory("Code")]
    public class ColumnPickerDialog : ThemedDialog
    {
        private readonly DataGridView _grid;
        private readonly TextBox _search;
        private readonly CheckBox _onlySelected;
        private readonly Label _count;
        private readonly ModernButton _ok;
        private readonly HashSet<string> _selected;

        public ColumnPickerDialog(EndpointService service, string table, string selected)
            : base(Strings.PickerTitle, Strings.PickerSubtitle(table))
        {
            ClientSize = new Size(720, 640);
            MinimumSize = new Size(560, 460);
            Resizable = true;
            _selected = new HashSet<string>(StepRules.NormalizeColumns(selected).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

            var card = new CardPanel { Dock = DockStyle.Fill };
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, BackColor = Theme.Card, WrapContents = false };
            _search = new TextBox { Width = 280, Margin = new Padding(0, 4, 14, 0) };
            _search.TextChanged += (s, e) => ApplyFilter();
            _search.HandleCreated += (s, e) => Theme.SetCue(_search, Strings.PickerSearchCue);
            _onlySelected = new CheckBox { Text = Strings.PickerOnlySelected, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 6, 0, 0) };
            _onlySelected.CheckedChanged += (s, e) => ApplyFilter();
            toolbar.Controls.AddRange(new Control[] { _search, _onlySelected });

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
            };
            Theme.StyleGrid(_grid, false);
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "colPick", Width = 34, HeaderText = string.Empty });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colDisplay", HeaderText = Strings.ColDisplayName, ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colLogical", HeaderText = Strings.ColLogicalName, ReadOnly = true, Width = 220 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colType", HeaderText = Strings.ColColumnType, ReadOnly = true, Width = 110 });
            _grid.Columns["colLogical"].DefaultCellStyle.ForeColor = Theme.Muted;
            _grid.Columns["colType"].DefaultCellStyle.ForeColor = Theme.Muted;
            _grid.CellClick += (s, e) => { if (e.RowIndex >= 0) Toggle(_grid.Rows[e.RowIndex]); };

            card.Controls.Add(_grid);
            card.Controls.Add(toolbar);
            Body.Controls.Add(card);

            _ok = AddFooterButton(Strings.Ok, ButtonVariant.Primary, DialogResult.OK, IconKind.Check);
            _ok.Enabled = false;
            CancelButton = AddFooterButton(Strings.CancelAction, ButtonVariant.Secondary, DialogResult.Cancel);
            _count = new Label { AutoSize = true, ForeColor = Theme.Muted, BackColor = FooterBack, Margin = new Padding(0, 8, 16, 0), Text = Strings.PickerLoading };
            Footer.Controls.Add(_count);

            Shown += async (s, e) => await LoadColumns(service, table);
        }

        /// <summary>Sorted, comma-separated logical names.</summary>
        public string Selected => string.Join(",", _selected.OrderBy(c => c, StringComparer.Ordinal));

        private async Task LoadColumns(EndpointService service, string table)
        {
            List<ColumnInfo> columns;
            try
            {
                columns = await Task.Run(() => service.LoadColumns(table));
            }
            catch (Exception error)
            {
                if (IsDisposed) return;
                MessageDialog.ShowError(this, error);
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }
            if (IsDisposed) return;

            _grid.SuspendLayout();
            foreach (var column in columns)
            {
                var index = _grid.Rows.Add(_selected.Contains(column.LogicalName), column.DisplayName, column.LogicalName, column.Type);
                _grid.Rows[index].Tag = column;
            }
            _grid.ResumeLayout();
            _ok.Enabled = true;
            UpdateCount();
            _search.Focus();
        }

        private void Toggle(DataGridViewRow row)
        {
            var column = (ColumnInfo)row.Tag;
            var value = !_selected.Contains(column.LogicalName);
            if (value) _selected.Add(column.LogicalName);
            else _selected.Remove(column.LogicalName);

            _grid.EndEdit();
            row.Cells[0].Value = value;
            UpdateCount();
        }

        private void UpdateCount()
        {
            _count.Text = Strings.PickerCount(_selected.Count);
        }

        private void ApplyFilter()
        {
            var search = _search.Text.Trim();
            _grid.CurrentCell = null;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var column = (ColumnInfo)row.Tag;
                var matches = search.Length == 0
                              || column.LogicalName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                              || column.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                row.Visible = matches && (!_onlySelected.Checked || _selected.Contains(column.LogicalName));
            }
        }
    }
}
