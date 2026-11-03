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
    /// Edits the trigger and the images of a step. Nothing is written here: the popup returns the change, after the
    /// user has reviewed it.
    /// </summary>
    [DesignerCategory("Code")]
    public class StepEditorDialog : ThemedDialog
    {
        private static readonly int[] Stages = { StepStage.PreValidation, StepStage.PreOperation, StepStage.PostOperation };
        private static readonly int[] Modes = { StepMode.Synchronous, StepMode.Asynchronous };

        private readonly StepInfo _original;
        private readonly StepInfo _draft;
        private readonly EndpointService _service;
        private readonly List<KeyValuePair<Guid, string>> _messages;
        private Dictionary<string, Guid> _tables;
        private readonly bool _readOnly;

        private readonly ComboBox _message;
        private readonly ComboBox _table;
        private readonly ComboBox _stage;
        private readonly ComboBox _mode;
        private readonly NumericUpDown _rank;
        private readonly TextBox _filtering;
        private readonly ModernButton _pickFiltering;
        private readonly ModernButton _clearFiltering;
        private readonly Label _filteringHint;
        private readonly CheckBox _deleteJob;
        private readonly TextBox _name;
        private readonly TextBox _description;
        private readonly DataGridView _images;
        private readonly ModernButton _addImage;
        private readonly ModernButton _editImage;
        private readonly ModernButton _removeImage;
        private readonly Panel _issuesHost;
        private readonly Label _issues;
        private readonly ModernButton _save;
        private bool _filling;

        public StepEditorDialog(StepInfo step, List<KeyValuePair<Guid, string>> messages, Dictionary<string, Guid> tables, EndpointService service)
            : base(Strings.EditorTitle, step.Name)
        {
            _original = step;
            _draft = step.Clone();
            _messages = messages;
            _tables = tables;
            _service = service;
            _readOnly = step.IsManaged && !step.IsCustomizable;

            ClientSize = new Size(1060, 680);
            MinimumSize = new Size(960, 620);
            Resizable = true;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Canvas };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Trigger
            var trigger = new CardPanel { Dock = DockStyle.Fill, Title = Strings.TriggerTitle, Subtitle = Strings.TriggerSubtitle, Margin = new Padding(0, 0, 6, 0) };
            var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Card };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _message = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems, Dock = DockStyle.Fill, MaxDropDownItems = 20, Margin = FieldMargin };
            _message.Items.AddRange(messages.Select(m => (object)m.Value).ToArray());
            _table = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems, Dock = DockStyle.Fill, MaxDropDownItems = 20, Margin = FieldMargin };
            _stage = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = FieldMargin };
            foreach (var stage in Stages) _stage.Items.Add(Strings.StageLabel(stage));
            _mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = FieldMargin };
            foreach (var mode in Modes) _mode.Items.Add(Strings.ModeLabel(mode));
            _rank = new NumericUpDown { Minimum = 0, Maximum = 100000, Width = 90, Margin = FieldMargin };

            _filtering = new TextBox { Dock = DockStyle.Fill, Multiline = true, Height = 64, ReadOnly = true, BackColor = Theme.SameBack, ScrollBars = ScrollBars.Vertical, Margin = FieldMargin };
            var filteringButtons = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Card, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
            _pickFiltering = SmallButton(Strings.SelectColumns, IconKind.Fields, ButtonVariant.Secondary);
            _pickFiltering.Click += (s, e) => PickFiltering();
            _clearFiltering = SmallButton(Strings.ClearColumns, IconKind.Close, ButtonVariant.Ghost);
            _clearFiltering.Click += (s, e) => { _filtering.Text = string.Empty; Collect(); };
            _filteringHint = new Label { AutoSize = true, ForeColor = Theme.Subtle, Font = Theme.Small, Margin = new Padding(4, 8, 0, 0) };
            filteringButtons.Controls.AddRange(new Control[] { _pickFiltering, _clearFiltering, _filteringHint });

            _deleteJob = new CheckBox { Text = Strings.FieldDeleteJob, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 10, 0, 0) };
            _name = new TextBox { Dock = DockStyle.Fill, Margin = FieldMargin };
            _description = new TextBox { Dock = DockStyle.Fill, Multiline = true, Height = 46, ScrollBars = ScrollBars.Vertical, Margin = FieldMargin };

            AddRow(form, Strings.FieldMessage, _message);
            AddRow(form, Strings.FieldTable, _table);
            AddRow(form, Strings.FieldStage, _stage);
            AddRow(form, Strings.FieldMode, _mode);
            AddRow(form, Strings.FieldRank, _rank);
            AddRow(form, Strings.FieldFiltering, _filtering);
            AddRow(form, string.Empty, filteringButtons);
            AddRow(form, string.Empty, _deleteJob);
            AddRow(form, Strings.FieldName, _name);
            AddRow(form, Strings.FieldDescription, _description);
            trigger.Controls.Add(form);

            // Images
            var images = new CardPanel { Dock = DockStyle.Fill, Title = Strings.ImagesTitle, Subtitle = Strings.ImagesSubtitle, Margin = new Padding(6, 0, 0, 0) };
            _images = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
            };
            Theme.StyleGrid(_images, false);
            _images.Columns.Add(new DataGridViewTextBoxColumn { Name = "colType", HeaderText = Strings.ColType, Width = 80 });
            _images.Columns.Add(new DataGridViewTextBoxColumn { Name = "colAlias", HeaderText = Strings.ColAlias, Width = 120 });
            _images.Columns.Add(new DataGridViewTextBoxColumn { Name = "colColumns", HeaderText = Strings.ColColumns, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            _images.SelectionChanged += (s, e) => UpdateImageButtons();
            _images.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && !_readOnly) EditImage(); };

            var imageButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Card, WrapContents = false };
            _addImage = SmallButton(Strings.Add, IconKind.Plus, ButtonVariant.Primary);
            _addImage.Click += (s, e) => AddImage();
            _editImage = SmallButton(Strings.Edit, IconKind.Edit, ButtonVariant.Secondary);
            _editImage.Click += (s, e) => EditImage();
            _removeImage = SmallButton(Strings.Remove, IconKind.Trash, ButtonVariant.Danger);
            _removeImage.Click += (s, e) => RemoveImage();
            imageButtons.Controls.AddRange(new Control[] { _addImage, _editImage, _removeImage });

            var hint = new Label { Text = Strings.ImagesHint, Dock = DockStyle.Bottom, Height = 46, ForeColor = Theme.Muted, Font = Theme.Small, TextAlign = ContentAlignment.BottomLeft };
            images.Controls.Add(_images);
            images.Controls.Add(imageButtons);
            images.Controls.Add(hint);

            layout.Controls.Add(trigger, 0, 0);
            layout.Controls.Add(images, 1, 0);

            _issuesHost = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = Theme.Canvas, Padding = new Padding(0, 8, 0, 0), Visible = false };
            _issues = new Label { Dock = DockStyle.Fill, BackColor = Theme.WarningSoft, ForeColor = Theme.WarningText, Padding = new Padding(10, 0, 10, 0), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            _issuesHost.Controls.Add(_issues);

            Body.Controls.Add(layout);
            Body.Controls.Add(_issuesHost);

            _save = AddFooterButton(Strings.Save, ButtonVariant.Primary, DialogResult.None, IconKind.Check);
            _save.Click += (s, e) => Review();
            CancelButton = AddFooterButton(Strings.CancelAction, ButtonVariant.Secondary, DialogResult.Cancel);

            Fill();

            _message.TextChanged += async (s, e) => await MessageChanged();
            _table.TextChanged += (s, e) => TableChanged();
            foreach (var combo in new[] { _stage, _mode }) combo.SelectedIndexChanged += (s, e) => Collect();
            _rank.ValueChanged += (s, e) => Collect();
            _deleteJob.CheckedChanged += (s, e) => Collect();
            _name.TextChanged += (s, e) => Collect();
            _description.TextChanged += (s, e) => Collect();
            Shown += (s, e) => _message.SelectionLength = 0;
        }

        /// <summary>The reviewed change, when the popup returns OK.</summary>
        public StepChange Change { get; private set; }

        private static Padding FieldMargin => new Padding(0, 6, 0, 0);

        private static void AddRow(TableLayoutPanel form, string label, Control field)
        {
            form.Controls.Add(new Label { Text = label, AutoSize = true, Font = Theme.Semibold, ForeColor = Theme.Muted, Margin = new Padding(0, 10, 8, 0) });
            form.Controls.Add(field);
        }

        private static ModernButton SmallButton(string text, IconKind glyph, ButtonVariant variant) => new ModernButton
        {
            Text = text,
            Variant = variant,
            Glyph = glyph,
            AutoSize = true,
            MinimumSize = new Size(84, 30),
            Height = 30,
            Margin = new Padding(0, 0, 8, 0)
        };

        private void Fill()
        {
            _filling = true;
            _message.Text = _draft.Message;
            FillTables();
            _table.Text = _draft.Table ?? (_tables.ContainsKey(string.Empty) ? Strings.AllTables : string.Empty);
            _stage.SelectedIndex = Math.Max(0, Array.IndexOf(Stages, _draft.Stage));
            _mode.SelectedIndex = Math.Max(0, Array.IndexOf(Modes, _draft.Mode));
            _rank.Value = Math.Max(_rank.Minimum, Math.Min(_rank.Maximum, _draft.Rank));
            _filtering.Text = _draft.FilteringAttributes;
            _deleteJob.Checked = _draft.DeleteJobOnSuccess;
            _name.Text = _draft.Name;
            _description.Text = _draft.Description;
            FillImages();
            _filling = false;

            if (_readOnly)
            {
                foreach (var control in new Control[] { _message, _table, _stage, _mode, _rank, _pickFiltering, _clearFiltering, _deleteJob, _name, _description, _addImage, _editImage, _removeImage, _save }) control.Enabled = false;
            }
            UpdateState();
        }

        private void FillTables()
        {
            _table.BeginUpdate();
            _table.Items.Clear();
            if (_tables.ContainsKey(string.Empty)) _table.Items.Add(Strings.AllTables);
            _table.Items.AddRange(_tables.Keys.Where(k => k.Length > 0).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray());
            _table.EndUpdate();
        }

        private void FillImages()
        {
            var selected = _images.SelectedRows.Count > 0 ? _images.SelectedRows[0].Tag : null;
            _images.Rows.Clear();
            foreach (var image in _draft.Images)
            {
                var index = _images.Rows.Add(Strings.ImageTypeLabel(image.ImageType), image.Alias, image.AllColumns ? Strings.AllColumns : image.Attributes);
                _images.Rows[index].Tag = image;
                if (image == selected) _images.Rows[index].Selected = true;
            }
            UpdateImageButtons();
        }

        private void UpdateImageButtons()
        {
            var hasSelection = _images.SelectedRows.Count > 0 && !_readOnly;
            _editImage.Enabled = hasSelection;
            _removeImage.Enabled = hasSelection;
        }

        /// <summary>Selected table: null for « all tables ».</summary>
        private string SelectedTable => string.IsNullOrEmpty(_table.Text) || _table.Text == Strings.AllTables ? null : _table.Text.Trim();

        private async Task MessageChanged()
        {
            if (_filling) return;

            var found = _messages.FirstOrDefault(m => string.Equals(m.Value, _message.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            if (found.Key == Guid.Empty)
            {
                _draft.Message = null;
                _draft.MessageId = Guid.Empty;
                UpdateState();
                return;
            }

            _draft.Message = found.Value;
            _draft.MessageId = found.Key;
            _issuesHost.Visible = false;
            _save.Enabled = false;
            UseWaitCursor = true;
            try
            {
                var id = found.Key;
                var tables = await Task.Run(() => _service.LoadTables(id));
                if (IsDisposed || _draft.MessageId != id) return;
                _tables = tables;

                _filling = true;
                var previous = SelectedTable;
                FillTables();
                _table.Text = previous != null && tables.ContainsKey(previous) ? previous : tables.ContainsKey(string.Empty) ? Strings.AllTables : string.Empty;
                _filling = false;

                if (!StepRules.SupportsFilteringAttributes(_draft.Message)) _filtering.Text = string.Empty;
                TableChanged();
            }
            catch (Exception error)
            {
                if (!IsDisposed) MessageDialog.ShowError(this, error);
            }
            finally
            {
                if (!IsDisposed) UseWaitCursor = false;
            }
        }

        private void TableChanged()
        {
            if (_filling) return;

            // Filtering columns belong to the table: they cannot follow it.
            if (!string.Equals(SelectedTable, _original.Table, StringComparison.OrdinalIgnoreCase) && string.Equals(_draft.Table, _original.Table, StringComparison.OrdinalIgnoreCase))
            {
                _filtering.Text = string.Empty;
            }
            Collect();
        }

        private void Collect()
        {
            if (_filling) return;

            _draft.Table = SelectedTable;
            // Same trigger: the existing filter is kept as it is, nothing to write.
            var sameTrigger = _draft.MessageId == _original.MessageId && string.Equals(_draft.Table, _original.Table, StringComparison.OrdinalIgnoreCase);
            if (sameTrigger) _draft.FilterId = _original.FilterId;
            else _draft.FilterId = _tables.TryGetValue(_draft.Table ?? string.Empty, out var filter) ? filter : (Guid?)null;
            _draft.Stage = Stages[_stage.SelectedIndex];
            _draft.Mode = Modes[_mode.SelectedIndex];
            _draft.Rank = (int)_rank.Value;
            _draft.FilteringAttributes = string.IsNullOrEmpty(_filtering.Text) ? null : _filtering.Text;
            _draft.DeleteJobOnSuccess = _deleteJob.Checked && _draft.Mode == StepMode.Asynchronous;
            _draft.Name = _name.Text.Trim();
            _draft.Description = string.IsNullOrEmpty(_description.Text) ? null : _description.Text;
            UpdateState();
        }

        private void UpdateState()
        {
            var message = _draft.Message;
            var filtering = StepRules.SupportsFilteringAttributes(message) && !_readOnly;
            _pickFiltering.Enabled = filtering && SelectedTable != null;
            _clearFiltering.Enabled = filtering && !string.IsNullOrEmpty(_filtering.Text);
            _filteringHint.Text = StepRules.SupportsFilteringAttributes(message) ? string.Empty : Strings.FilteringOnlyUpdate;
            _deleteJob.Enabled = _draft.Mode == StepMode.Asynchronous && !_readOnly;
            _addImage.Enabled = StepRules.SupportsImages(message) && !_readOnly && StepRules.AllowedImageTypes(message, _draft.Stage).Count > 0;

            var issues = StepRules.Validate(_draft);
            var hasTrigger = _draft.FilterId != null || (_draft.MessageId == _original.MessageId && _draft.Table == null && _original.Table == null);
            if (message != null && !hasTrigger) issues.Insert(0, Strings.TableRequired);

            if (_readOnly) ShowIssues(Strings.ReadOnlyStep);
            else if (issues.Count > 0) ShowIssues(string.Join("  ·  ", issues));
            else if (_original.IsManaged) ShowIssues(Strings.ManagedStepWarning);
            else _issuesHost.Visible = false;

            _save.Enabled = !_readOnly && issues.Count == 0;
        }

        private void ShowIssues(string text)
        {
            _issues.Text = text;
            _issuesHost.Visible = true;
        }

        private void PickFiltering()
        {
            using (var picker = new ColumnPickerDialog(_service, SelectedTable, _filtering.Text))
            {
                if (picker.ShowModal(this) != DialogResult.OK) return;
                _filtering.Text = picker.Selected;
                Collect();
            }
        }

        private void AddImage()
        {
            var allowed = StepRules.AllowedImageTypes(_draft.Message, _draft.Stage);
            var image = new ImageInfo { ImageType = allowed[0] };
            using (var dialog = new ImageEditorDialog(image, allowed, _service, SelectedTable))
            {
                if (dialog.ShowModal(this) != DialogResult.OK) return;
            }
            _draft.Images.Add(image);
            FillImages();
            Collect();
        }

        private void EditImage()
        {
            if (_images.SelectedRows.Count == 0) return;
            var image = (ImageInfo)_images.SelectedRows[0].Tag;
            var copy = image.Clone();

            // An invalid type stays listed so it can be replaced, not silently changed.
            var allowed = StepRules.AllowedImageTypes(_draft.Message, _draft.Stage);
            if (allowed.Count == 0) allowed.Add(image.ImageType);
            using (var dialog = new ImageEditorDialog(copy, allowed, _service, SelectedTable))
            {
                if (dialog.ShowModal(this) != DialogResult.OK) return;
            }
            _draft.Images[_draft.Images.IndexOf(image)] = copy;
            FillImages();
            Collect();
        }

        private void RemoveImage()
        {
            if (_images.SelectedRows.Count == 0) return;
            _draft.Images.Remove((ImageInfo)_images.SelectedRows[0].Tag);
            FillImages();
            Collect();
        }

        private void Review()
        {
            Collect();
            var change = new StepChange { Original = _original, Draft = _draft };
            var lines = StepRules.Describe(change);
            if (lines.Count == 0)
            {
                MessageDialog.Show(this, MessageKind.Info, Strings.ReviewTitle, Strings.NoChanges);
                return;
            }

            var bullet = "•  ";
            var message = Strings.ReviewMessage(_original.Name) + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines.Select(l => bullet + l));
            if (!MessageDialog.Ask(this, MessageKind.Warning, Strings.ReviewTitle, message, null, Strings.ReviewQuestion, Strings.Save, Strings.CancelAction)) return;

            Change = change;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
