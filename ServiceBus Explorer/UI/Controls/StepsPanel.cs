using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.Models;

namespace ServiceBusExplorer.UI.Controls
{
    /// <summary>
    /// List of steps with their images and actions, shared by the explorer and the Steps page.
    /// </summary>
    [DesignerCategory("Code")]
    public class StepsPanel : Panel
    {
        private const int ImagesHeight = 150;

        private readonly DataGridView _grid;
        private readonly DataGridView _images;
        private readonly SplitContainer _split;
        private readonly ModernButton _edit;
        private readonly ModernButton _enable;
        private readonly ModernButton _disable;
        private readonly ModernButton _messages;
        private readonly Label _count;
        private readonly Label _hint;
        private readonly Label _imagesTitle;
        private readonly Label _empty;
        private readonly TextBox _search;
        private readonly ToolTip _toolTip = new ToolTip { AutoPopDelay = 15000 };
        private readonly bool _searchable;
        private List<StepInfo> _steps = new List<StepInfo>();

        public event EventHandler<StepInfo> EditRequested;

        public event EventHandler<List<StepInfo>> EnableRequested;

        public event EventHandler<List<StepInfo>> DisableRequested;

        /// <summary>Peek the messages sent by the selected steps.</summary>
        public event EventHandler<List<StepInfo>> MessagesRequested;

        public StepsPanel(bool searchable)
        {
            BackColor = Theme.Card;
            _searchable = searchable;

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Theme.Card, Padding = new Padding(0, 0, 0, 6) };
            _search = new TextBox { Width = 260, Margin = new Padding(0, 5, 10, 0), Visible = searchable };
            _search.TextChanged += (s, e) => Fill();
            _edit = Button(IconKind.Edit, ButtonVariant.Primary);
            _enable = Button(IconKind.Play, ButtonVariant.Success);
            _disable = Button(IconKind.Stop, ButtonVariant.Secondary);
            _messages = Button(IconKind.Log, ButtonVariant.Secondary);
            _messages.Margin = new Padding(8, 0, 8, 0);
            _count = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(6, 8, 0, 0) };
            toolbar.Controls.AddRange(new Control[] { _search, _edit, _enable, _disable, _messages, _count });

            _hint = new Label { Dock = DockStyle.Top, Height = 22, ForeColor = Theme.Muted, Font = Theme.Small, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Visible = false };

            _split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Theme.Card, SplitterWidth = 8, FixedPanel = FixedPanel.Panel2 };
            _split.Panel1.BackColor = Theme.Card;
            _split.Panel2.BackColor = Theme.Card;

            _grid = NewGrid();
            _grid.MultiSelect = true;
            _grid.Columns.AddRange(
                Column("colState", 92),
                Column("colMessage", 92),
                Column("colTable", 120),
                Column("colStage", 104),
                Column("colMode", 104),
                Column("colRank", 48),
                Column("colFiltering", 0),
                Column("colImages", 58),
                Column("colTarget", 150),
                Column("colEndpoint", 150));
            _grid.Columns["colFiltering"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            // In the explorer the selected item already says which queue or topic it is.
            _grid.Columns["colTarget"].Visible = searchable;
            _grid.Columns["colFiltering"].MinimumWidth = 140;
            _grid.Columns["colTable"].DefaultCellStyle.Font = Theme.Semibold;
            _grid.CellPainting += PaintState;
            _grid.SelectionChanged += (s, e) => ShowSelection();
            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].Tag is StepInfo step) EditRequested?.Invoke(this, step);
            };
            _empty = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted, BackColor = Theme.Card, Visible = false };
            _split.Panel1.Controls.Add(_grid);
            _split.Panel1.Controls.Add(_empty);

            _imagesTitle = new Label { Dock = DockStyle.Top, Height = 24, ForeColor = Theme.Muted, Font = Theme.Semibold, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            _images = NewGrid();
            _images.Columns.AddRange(Column("colImageType", 90), Column("colImageAlias", 150), Column("colImageColumns", 0), Column("colImageName", 170));
            _images.Columns["colImageColumns"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _split.Panel2.Controls.Add(_images);
            _split.Panel2.Controls.Add(_imagesTitle);
            _split.SizeChanged += (s, e) => PlaceSplitter();

            _edit.Click += (s, e) =>
            {
                var step = Selected().FirstOrDefault();
                if (step != null) EditRequested?.Invoke(this, step);
            };
            _enable.Click += (s, e) => EnableRequested?.Invoke(this, Selected());
            _disable.Click += (s, e) => DisableRequested?.Invoke(this, Selected());
            _messages.Click += (s, e) => MessagesRequested?.Invoke(this, Selected());

            Controls.Add(_split);
            Controls.Add(_hint);
            Controls.Add(toolbar);
            ApplyLanguage();
        }

        private void PlaceSplitter()
        {
            if (_split.Height <= ImagesHeight + _split.Panel1MinSize + _split.SplitterWidth) return;
            _split.SplitterDistance = _split.Height - ImagesHeight - _split.SplitterWidth;
        }

        /// <summary>
        /// Shows the Messages button: messages live in a queue or a subscription, so it has no meaning
        /// for the steps of a whole namespace.
        /// </summary>
        public bool ShowMessages
        {
            get => _showMessages;
            set
            {
                _showMessages = value;
                _messages.Visible = value;
            }
        }

        private bool _showMessages = true;

        public void SetSteps(IEnumerable<StepInfo> steps, string hint = null)
        {
            _steps = steps.ToList();
            _hint.Text = hint;
            _hint.Visible = !string.IsNullOrEmpty(hint);
            Fill();
        }

        public void ApplyLanguage()
        {
            string[] headers =
            {
                Strings.ColState, Strings.ColMessage, Strings.ColTable, Strings.ColStage, Strings.ColMode, Strings.ColRank,
                Strings.ColFiltering, Strings.ColImages, Strings.ColTarget, Strings.ColEndpoint
            };
            for (var i = 0; i < headers.Length; i++) _grid.Columns[i].HeaderText = headers[i];
            string[] imageHeaders = { Strings.ColType, Strings.ColAlias, Strings.ColColumns, Strings.ColName };
            for (var i = 0; i < imageHeaders.Length; i++) _images.Columns[i].HeaderText = imageHeaders[i];

            _edit.Text = Strings.Edit;
            _enable.Text = Strings.Enable;
            _disable.Text = Strings.Disable;
            _messages.Text = Strings.MessagesButton;
            _empty.Text = Strings.NoSteps;
            _toolTip.SetToolTip(_edit, Strings.EditStepTip);
            _toolTip.SetToolTip(_messages, Strings.MessagesButtonTip);
            if (_search.IsHandleCreated) Theme.SetCue(_search, Strings.StepsSearchCue);
            Fill();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _search.HandleCreated += (s, a) => Theme.SetCue(_search, Strings.StepsSearchCue);
            if (_search.IsHandleCreated) Theme.SetCue(_search, Strings.StepsSearchCue);
        }

        private void Fill()
        {
            var selection = new HashSet<Guid>(Selected().Select(s => s.Id));
            var search = _search.Text.Trim();
            var visible = _steps.Where(s => search.Length == 0 || Matches(s, search)).ToList();

            _grid.SuspendLayout();
            _grid.Rows.Clear();
            foreach (var step in visible)
            {
                var index = _grid.Rows.Add(
                    step.Enabled ? Strings.Enabled : Strings.Disabled,
                    step.Message,
                    step.Table ?? Strings.AllTables,
                    Strings.StageLabel(step.Stage),
                    Strings.ModeLabel(step.Mode),
                    step.Rank,
                    Filtering(step),
                    step.Images.Count,
                    step.TargetPath,
                    step.EndpointName);
                var row = _grid.Rows[index];
                row.Tag = step;
                row.Selected = selection.Contains(step.Id);
            }
            _grid.ResumeLayout();

            if (selection.Count == 0 && _grid.Rows.Count > 0) _grid.Rows[0].Selected = true;
            _empty.Visible = visible.Count == 0;
            _grid.Visible = visible.Count > 0;
            _count.Text = _searchable ? Strings.StepCount(visible.Count, _steps.Count) : string.Empty;
            ShowSelection();
        }

        private static string Filtering(StepInfo step)
        {
            if (!string.IsNullOrEmpty(step.FilteringAttributes)) return step.FilteringAttributes;
            return string.Equals(step.Message, "Update", StringComparison.OrdinalIgnoreCase) ? Strings.AllColumnsTrigger : string.Empty;
        }

        private static bool Matches(StepInfo step, string search)
        {
            bool Has(string value) => value != null && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
            return Has(step.Table) || Has(step.Message) || Has(step.FilteringAttributes) || Has(step.EndpointName) || Has(step.TargetPath) || Has(step.Name);
        }

        private List<StepInfo> Selected() => _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag as StepInfo).Where(s => s != null).Reverse().ToList();

        private void ShowSelection()
        {
            var selected = Selected();
            _edit.Enabled = selected.Count == 1;
            _enable.Enabled = selected.Any(s => !s.Enabled);
            _disable.Enabled = selected.Any(s => s.Enabled);
            _messages.Enabled = selected.Count > 0;

            var step = selected.Count == 1 ? selected[0] : null;
            _imagesTitle.Text = step == null ? Strings.ImagesNoStep : Strings.ImagesOfStep(step.Name) + (step.IsManaged ? " · " + Strings.Managed : string.Empty);
            _images.Rows.Clear();
            if (step == null) return;
            foreach (var image in step.Images)
            {
                _images.Rows.Add(Strings.ImageTypeLabel(image.ImageType), image.Alias, image.AllColumns ? Strings.AllColumns : image.Attributes, image.Name);
            }
        }

        private void PaintState(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
            var step = _grid.Rows[e.RowIndex].Tag as StepInfo;
            if (step == null) return;
            var color = step.Enabled ? Theme.Success : Theme.Subtle;
            Theme.PaintPillCell(e, _grid, e.Value as string, step.Enabled ? Theme.SuccessText : Theme.Muted, Theme.Soft(color));
        }

        private static ModernButton Button(IconKind glyph, ButtonVariant variant) => new ModernButton
        {
            Variant = variant,
            Glyph = glyph,
            AutoSize = true,
            MinimumSize = new Size(90, 30),
            Height = 30,
            Margin = new Padding(0, 0, 8, 0)
        };

        private static DataGridView NewGrid()
        {
            var grid = new DataGridView
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
            Theme.StyleGrid(grid, false);
            return grid;
        }

        private static DataGridViewTextBoxColumn Column(string name, int width)
        {
            var column = new DataGridViewTextBoxColumn { Name = name, ReadOnly = true };
            if (width > 0) column.Width = width;
            return column;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _toolTip.Dispose();
            base.Dispose(disposing);
        }
    }
}
