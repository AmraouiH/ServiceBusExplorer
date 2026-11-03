using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ServiceBusExplorer.Forms;
using ServiceBusExplorer.Helpers;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.Models;

namespace ServiceBusExplorer.UI.Controls
{
    /// <summary>
    /// Peeked active messages of queues and subscriptions (never the dead-letter queue), with their Dataverse context and body.
    /// Shared by the explorer (one entity) and the messages popup of the steps. The owner reads, this panel displays.
    /// </summary>
    [DesignerCategory("Code")]
    public class MessagesPanel : Panel
    {
        private const int DetailHeight = 260;

        private readonly ModernButton _read;
        private readonly CheckBox _onlySteps;
        private readonly TextBox _search;
        private readonly Label _count;
        private readonly Label _hint;
        private readonly Panel _alert;
        private readonly Label _alertText;
        private readonly ModernButton _alertButton;
        private readonly SplitContainer _split;
        private readonly DataGridView _grid;
        private readonly Label _empty;
        private readonly DataGridView _properties;
        private readonly Label _bodyTitle;
        private readonly TextBox _body;
        private readonly ModernButton _copy;
        private readonly ModernButton _open;
        private readonly ToolTip _toolTip = new ToolTip { AutoPopDelay = 15000 };

        private List<MessageBatch> _batches;
        private HashSet<Guid> _stepFilter;
        private string _intro;
        private bool _canRead;
        private bool _busy;

        /// <summary>The user asked to read (or read again) the messages.</summary>
        public event EventHandler ReadRequested;

        /// <summary>The user stopped a read in progress (popup only: the explorer has the loading overlay).</summary>
        public event EventHandler StopRequested;

        public MessagesPanel()
        {
            BackColor = Theme.Card;

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Theme.Card, Padding = new Padding(0, 0, 0, 6) };
            _read = new ModernButton { Variant = ButtonVariant.Primary, Glyph = IconKind.Reload, AutoSize = true, MinimumSize = new Size(130, 30), Height = 30, Margin = new Padding(0, 0, 12, 0) };
            _onlySteps = Toggle();
            _onlySteps.Visible = false;
            _search = new TextBox { Width = 240, Margin = new Padding(4, 5, 10, 0) };
            _count = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(4, 8, 0, 0) };
            toolbar.Controls.AddRange(new Control[] { _read, _onlySteps, _search, _count });

            _hint = new Label { Dock = DockStyle.Top, Height = 22, ForeColor = Theme.Muted, Font = Theme.Small, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };

            _alert = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Theme.WarningSoft, Padding = new Padding(10, 3, 4, 3), Visible = false };
            _alertText = new Label { Dock = DockStyle.Fill, ForeColor = Theme.WarningText, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            _alertButton = new ModernButton { Variant = ButtonVariant.Secondary, Dock = DockStyle.Right, Width = 110 };
            _alert.Controls.Add(_alertText);
            _alert.Controls.Add(_alertButton);

            _split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Theme.Card, SplitterWidth = 8, FixedPanel = FixedPanel.Panel2 };
            _split.Panel1.BackColor = Theme.Card;
            _split.Panel2.BackColor = Theme.Card;

            _grid = NewGrid();
            // The size of the body is in the title of the detail: the columns fit the explorer without scrolling.
            _grid.Columns.AddRange(
                Column("colSequence", 60, DataGridViewContentAlignment.MiddleRight),
                Column("colEnqueued", 140),
                Column("colStep", 0),
                Column("colMessage", 90),
                Column("colTable", 130),
                Column("colDelivery", 80, DataGridViewContentAlignment.MiddleRight),
                Column("colSource", 160));
            _grid.Columns["colStep"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _grid.Columns["colStep"].MinimumWidth = 160;
            _grid.Columns["colTable"].DefaultCellStyle.Font = Theme.Semibold;
            _grid.CellPainting += PaintCell;
            _grid.SelectionChanged += (s, e) => ShowSelection();
            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0) OpenBody();
            };
            _empty = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted, BackColor = Theme.Card, Padding = new Padding(60, 0, 60, 30) };
            _split.Panel1.Controls.Add(_grid);
            _split.Panel1.Controls.Add(_empty);

            var detail = new SplitContainer { Dock = DockStyle.Fill, BackColor = Theme.Card, SplitterWidth = 8 };
            detail.Panel1.BackColor = Theme.Card;
            detail.Panel2.BackColor = Theme.Card;
            _properties = NewGrid();
            _properties.ColumnHeadersVisible = false;
            _properties.RowTemplate.Height = 24;
            _properties.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _properties.Columns.AddRange(Column("colKey", 170), Column("colValue", 0));
            _properties.Columns["colKey"].DefaultCellStyle.ForeColor = Theme.Muted;
            _properties.Columns["colValue"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            detail.Panel1.Controls.Add(_properties);

            var bodyHead = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Theme.Card };
            _bodyTitle = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Font = Theme.Semibold, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            _open = new ModernButton { Variant = ButtonVariant.Ghost, Glyph = IconKind.Maximize, Dock = DockStyle.Right, Width = 92, Height = 26 };
            _copy = new ModernButton { Variant = ButtonVariant.Ghost, Glyph = IconKind.Copy, Dock = DockStyle.Right, Width = 92, Height = 26 };
            bodyHead.Controls.Add(_bodyTitle);
            bodyHead.Controls.Add(_copy);
            bodyHead.Controls.Add(_open);
            _body = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.SameBack,
                ForeColor = Theme.Text,
                Font = Theme.Mono
            };
            detail.Panel2.Controls.Add(_body);
            detail.Panel2.Controls.Add(bodyHead);
            detail.SizeChanged += (s, e) =>
            {
                if (detail.Width > 400) detail.SplitterDistance = Math.Max(260, Math.Min(detail.Width / 2, 460));
            };
            _split.Panel2.Controls.Add(detail);
            _split.SizeChanged += (s, e) => PlaceSplitter();

            _read.Click += (s, e) =>
            {
                if (_busy) StopRequested?.Invoke(this, EventArgs.Empty);
                else ReadRequested?.Invoke(this, EventArgs.Empty);
            };
            _onlySteps.CheckedChanged += (s, e) => Fill();
            _search.TextChanged += (s, e) => Fill();
            _alertButton.Click += (s, e) => ShowErrors();
            _copy.Click += (s, e) => CopyBody();
            _open.Click += (s, e) => OpenBody();

            Controls.Add(_split);
            Controls.Add(_alert);
            Controls.Add(_hint);
            Controls.Add(toolbar);
            ApplyLanguage();
            SetContext(null, false);
        }

        #region Public surface

        /// <summary>The messages read last, null before the first read.</summary>
        public List<MessageBatch> Batches => _batches;

        /// <summary>Clears the messages: the panel waits for a click on Read.</summary>
        /// <param name="intro">Text shown before the first read, null when the entity cannot be read.</param>
        public void SetContext(string intro, bool canRead, HashSet<Guid> stepFilter = null)
        {
            _intro = intro;
            _canRead = canRead;
            _stepFilter = stepFilter;
            _onlySteps.Visible = stepFilter != null;
            _onlySteps.Checked = stepFilter != null;
            _onlySteps.Text = stepFilter != null && stepFilter.Count > 1 ? Strings.OnlyStepsMessages : Strings.OnlyStepMessages;
            _batches = null;
            Busy = false;
            Fill();
        }

        public void SetBatches(List<MessageBatch> batches)
        {
            _batches = batches;
            Fill();
            if (_grid.Rows.Count > 0) _grid.Rows[0].Selected = true;
            ShowSelection();
        }

        /// <summary>A read is in progress: the Read button becomes Stop.</summary>
        public bool Busy
        {
            get => _busy;
            set
            {
                _busy = value;
                _read.Text = value ? Strings.Stop : Strings.ReadMessages;
                _read.Glyph = value ? IconKind.Stop : IconKind.Reload;
                _read.Variant = value ? ButtonVariant.Secondary : ButtonVariant.Primary;
                _read.Enabled = _canRead;
                if (value) _count.Text = Strings.ReadingMessagesShort;
                else UpdateCount();
            }
        }

        /// <summary>What the read in progress is doing.</summary>
        public void ShowProgress(string text)
        {
            if (_busy) _count.Text = text;
        }

        public void ApplyLanguage()
        {
            string[] headers =
            {
                Strings.ColSequence, Strings.ColEnqueued, Strings.ColStep, Strings.ColMessage, Strings.ColTable,
                Strings.ColDelivery, Strings.ColSource
            };
            for (var i = 0; i < headers.Length; i++) _grid.Columns[i].HeaderText = headers[i];

            Busy = _busy;
            _onlySteps.Text = _stepFilter != null && _stepFilter.Count > 1 ? Strings.OnlyStepsMessages : Strings.OnlyStepMessages;
            _hint.Text = Strings.PeekHint;
            _copy.Text = Strings.CopyBody;
            _open.Text = Strings.OpenBody;
            _alertButton.Text = Strings.ShowError;
            _toolTip.SetToolTip(_read, Strings.ReadMessagesTip);
            if (_search.IsHandleCreated) Theme.SetCue(_search, Strings.MessagesSearchCue);
            Fill();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _search.HandleCreated += (s, a) => Theme.SetCue(_search, Strings.MessagesSearchCue);
            if (_search.IsHandleCreated) Theme.SetCue(_search, Strings.MessagesSearchCue);
        }

        #endregion

        #region Grid

        private IEnumerable<SbMessage> AllMessages => _batches?.SelectMany(b => b.Messages) ?? Enumerable.Empty<SbMessage>();

        private bool StepOnly => _stepFilter != null && _onlySteps.Checked;

        private void Fill()
        {
            var selected = Selected();
            var search = _search.Text.Trim();
            var visible = AllMessages
                .Where(m => !StepOnly || m.Context?.StepId != null && _stepFilter.Contains(m.Context.StepId.Value))
                .Where(m => search.Length == 0 || Matches(m, search))
                .OrderBy(m => m.EnqueuedOn ?? DateTime.MaxValue).ThenBy(m => m.SequenceNumber)
                .ToList();

            var multipleSources = _batches != null && _batches.Select(b => b.Source).Distinct().Count() > 1;
            _grid.Columns["colSource"].Visible = multipleSources;

            _grid.SuspendLayout();
            _grid.Rows.Clear();
            foreach (var message in visible)
            {
                var index = _grid.Rows.Add(
                    message.SequenceNumber.ToString("N0"),
                    message.EnqueuedOn.HasValue ? message.EnqueuedOn.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : string.Empty,
                    StepText(message),
                    message.Context?.MessageName,
                    message.Context?.Table,
                    message.DeliveryCount,
                    message.Source?.Name);
                var row = _grid.Rows[index];
                row.Tag = message;
                row.Selected = message == selected;
            }
            _grid.ResumeLayout();

            _grid.Visible = visible.Count > 0;
            _empty.Visible = visible.Count == 0;

            // No message, no detail: the explanation takes the whole page.
            var collapsed = visible.Count == 0;
            if (_split.Panel2Collapsed != collapsed)
            {
                _split.Panel2Collapsed = collapsed;
                if (!collapsed) PlaceSplitter();
            }
            _empty.Text = EmptyText();
            UpdateCount(visible.Count);
            UpdateAlert();
            ShowSelection();
        }

        private string EmptyText()
        {
            if (!_canRead) return Strings.MessagesUnavailable;
            if (_batches == null) return _intro ?? string.Empty;
            if (_batches.All(b => b.Error != null)) return Strings.MessagesAllFailed;
            if (!AllMessages.Any()) return Strings.MessagesNone;
            if (StepOnly) return Strings.MessagesNoneForStep(AllMessages.Count());
            return Strings.MessagesNoMatch;
        }

        private void UpdateCount(int? shown = null)
        {
            if (_busy) return;
            if (_batches == null)
            {
                _count.Text = string.Empty;
                return;
            }
            var all = AllMessages.ToList();
            var visible = shown ?? _grid.Rows.Count;
            var text = Strings.MessagesCount(visible, all.Count);
            if (_batches.Any(b => b.Truncated)) text += Strings.MessagesTruncated(_batches.Where(b => b.Truncated).Max(b => b.Messages.Count));
            _count.Text = text;
        }

        private void UpdateAlert()
        {
            var failed = _batches?.Where(b => b.Error != null).ToList() ?? new List<MessageBatch>();
            _alert.Visible = failed.Count > 0;
            if (failed.Count == 0) return;
            var first = failed[0];
            _alertText.Text = Strings.MessagesErrors(failed.Count, Label(first)) + " " + first.Error.Message;
        }

        private void ShowErrors()
        {
            var failed = _batches?.Where(b => b.Error != null).ToList();
            if (failed == null || failed.Count == 0) return;
            var details = string.Join(Environment.NewLine + Environment.NewLine, failed.Select(b =>
                Label(b) + Environment.NewLine + b.Error.Message + ((b.Error as Services.Azure.AzureRequestException)?.Details is string d && d.Length > 0 ? Environment.NewLine + d : string.Empty)));
            TextViewerDialog.Show(this, Strings.MessagesErrorsTitle, Strings.MessagesErrors(failed.Count, Label(failed[0])), details);
        }

        private static string Label(MessageBatch batch) => Services.MessageService.PathOf(batch.Source);

        private static string StepText(SbMessage message)
        {
            if (message.Step != null) return message.Step.Name;
            if (message.Context == null) return Strings.NotDataverseMessage;
            return string.IsNullOrEmpty(message.Context.StepName) ? Strings.UnknownStep : message.Context.StepName + " · " + Strings.UnknownStep;
        }

        private static bool Matches(SbMessage message, string search)
        {
            bool Has(string value) => value != null && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
            var context = message.Context;
            return Has(message.Step?.Name) || Has(context?.StepName) || Has(context?.MessageName) || Has(context?.Table) || Has(context?.RecordId?.ToString())
                   || Has(message.MessageId) || Has(message.CorrelationId) || Has(message.Source?.Name) || Has(message.BodyText);
        }

        private SbMessage Selected() => _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Tag as SbMessage : null;

        private void PaintCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (!(_grid.Rows[e.RowIndex].Tag is SbMessage message)) return;
            if (_grid.Columns[e.ColumnIndex].Name == "colStep" && message.Step == null)
            {
                e.CellStyle.ForeColor = Theme.Muted;
            }
        }

        #endregion

        #region Detail

        private void ShowSelection()
        {
            var message = Selected();
            _properties.Rows.Clear();
            _copy.Enabled = _open.Enabled = message != null;
            if (message == null)
            {
                _bodyTitle.Text = Strings.BodyTitle(null, 0);
                _body.Text = string.Empty;
                return;
            }

            void Section(string title)
            {
                var index = _properties.Rows.Add(title, string.Empty);
                _properties.Rows[index].DefaultCellStyle.Font = Theme.Semibold;
                _properties.Rows[index].DefaultCellStyle.ForeColor = Theme.Text;
            }

            void Add(string key, string value)
            {
                if (!string.IsNullOrEmpty(value)) _properties.Rows.Add(key, value);
            }

            string Date(DateTime? value) => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

            Section(Strings.SectionMessage);
            Add(Strings.PropSource, Label(new MessageBatch { Source = message.Source }));
            Add(Strings.PropSequence, message.SequenceNumber.ToString());
            Add(Strings.PropMessageId, message.MessageId);
            Add(Strings.PropEnqueued, Date(message.EnqueuedOn));
            Add(Strings.PropScheduled, Date(message.ScheduledFor));
            Add(Strings.PropExpires, Date(message.ExpiresOn));
            Add(Strings.PropDeliveryCount, message.DeliveryCount.ToString());
            Add(Strings.PropContentType, message.ContentType);
            Add(Strings.PropSubject, message.Subject);
            Add(Strings.PropCorrelation, message.CorrelationId);
            Add(Strings.PropSession, message.SessionId);
            Add(Strings.PropTo, message.To);
            Add(Strings.PropReplyTo, message.ReplyTo);

            var context = message.Context;
            if (context != null)
            {
                Section(Strings.SectionDataverse);
                Add(Strings.ColStep, message.Step?.Name ?? (string.IsNullOrEmpty(context.StepName) ? Strings.UnknownStep : context.StepName + " · " + Strings.UnknownStep));
                Add(Strings.PropStepId, context.StepId?.ToString());
                Add(Strings.ColMessage, context.MessageName);
                Add(Strings.ColTable, context.Table);
                Add(Strings.PropRecord, context.RecordId?.ToString());
                Add(Strings.ColStage, context.Stage.HasValue ? Strings.StageLabel(context.Stage.Value) : null);
                Add(Strings.ColMode, context.Mode.HasValue ? Strings.ModeLabel(context.Mode.Value) : null);
                Add(Strings.PropDepth, context.Depth?.ToString());
                Add(Strings.PropUser, context.UserId?.ToString());
                Add(Strings.PropInitiatingUser, context.InitiatingUserId?.ToString());
                Add(Strings.PropOrganization, context.Organization);
                Add(Strings.PropOperationCorrelation, context.CorrelationId?.ToString());
                Add(Strings.PropOperationCreated, Date(context.OperationCreatedOn));
            }

            if (message.Properties.Count > 0)
            {
                Section(Strings.SectionApplicationProperties);
                foreach (var property in message.Properties) Add(property.Key, property.Value);
            }

            _properties.ClearSelection();
            _bodyTitle.Text = Strings.BodyTitle(message.BodyFormat, message.Body?.Length ?? 0);
            _body.Text = (message.BodyText ?? string.Empty).Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            _body.SelectionStart = 0;
            _body.SelectionLength = 0;
        }

        private void CopyBody()
        {
            if (Selected() == null) return;
            try
            {
                Clipboard.SetText(string.IsNullOrEmpty(_body.Text) ? " " : _body.Text);
                _copy.Text = Strings.Copied;
            }
            catch (Exception)
            {
                // The clipboard is locked by another application: nothing to copy this time.
            }
        }

        private void OpenBody()
        {
            var message = Selected();
            if (message == null) return;
            var subtitle = $"{Label(new MessageBatch { Source = message.Source })} · #{message.SequenceNumber} · {StepText(message)}";
            TextViewerDialog.Show(this, Strings.BodyTitle(message.BodyFormat, message.Body?.Length ?? 0), subtitle, message.BodyText);
        }

        #endregion

        #region Helpers

        private void PlaceSplitter()
        {
            if (_split.Height <= DetailHeight + _split.Panel1MinSize + _split.SplitterWidth) return;
            _split.SplitterDistance = _split.Height - DetailHeight - _split.SplitterWidth;
        }

        private static CheckBox Toggle() => new CheckBox { AutoSize = true, Checked = true, ForeColor = Theme.Text, BackColor = Theme.Card, Margin = new Padding(0, 7, 12, 0) };

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

        private static DataGridViewTextBoxColumn Column(string name, int width, DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
        {
            var column = new DataGridViewTextBoxColumn { Name = name, ReadOnly = true };
            if (width > 0) column.Width = width;
            column.DefaultCellStyle.Alignment = alignment;
            if (alignment != DataGridViewContentAlignment.MiddleLeft) column.HeaderCell.Style.Alignment = alignment;
            return column;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _toolTip.Dispose();
            base.Dispose(disposing);
        }

        #endregion
    }
}
