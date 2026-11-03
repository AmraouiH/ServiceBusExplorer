using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using ServiceBusExplorer.Forms;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.Models;
using ServiceBusExplorer.Services;
using ServiceBusExplorer.Services.Azure;
using ServiceBusExplorer.Services.Dataverse;
using ServiceBusExplorer.UI;
using XrmToolBox.Extensibility;
using Timer = System.Windows.Forms.Timer;

namespace ServiceBusExplorer
{
    /// <summary>
    /// Tool UI: tree of the namespaces, matching with the Dataverse endpoints, steps and images.
    /// Business logic lives in the Services folder; this class only orchestrates and displays.
    /// </summary>
    public partial class ServiceBusExplorerControl : PluginControlBase
    {
        private const string LinkedInUrl = "https://www.linkedin.com/in/hamza-amraoui/";
        private const string DonateUrl = "https://www.paypal.com/paypalme/EntityFieldsAnalyser";

        private const int PageExplorer = 0;
        private const int PageMatching = 1;
        private const int PageSteps = 2;

        /// <summary>Delay after Stop before a task stuck in a server call is abandoned (its result will be ignored).</summary>
        private const int AbandonDelayMs = 3000;

        private sealed class BusyTask
        {
            public BackgroundWorker Worker;
            public CancellationTokenSource Stop = new CancellationTokenSource();
            public bool Abandoned;
        }

        private readonly ThemeRenderer _renderer = new ThemeRenderer();
        private PluginSettings _settings = new PluginSettings();
        private ExplorerModel _model;
        private List<SbNamespace> _azure = new List<SbNamespace>();
        private EndpointService _endpoints;
        private BusyTask _busy;
        private bool _controlLoaded;
        private bool _suppressEvents;
        private DateTime _loadedAt;

        public ServiceBusExplorerControl()
        {
            BuildLayout();
            overlay.StopRequested += (s, e) => StopBusyTask();
            tsHeader.SizeChanged += (s, e) => AdjustHeader();
            ApplyTheme();
            WireEvents();
            ApplyLanguage();
            ShowPage(PageExplorer);
            Strings.LanguageChanged += Strings_LanguageChanged;
            Load += ServiceBusExplorerControl_Load;
        }

        #region Theme / language

        private void ApplyTheme()
        {
            tsHeader.Renderer = new FluentToolStripRenderer();
            tsddLanguage.DropDown.Renderer = _renderer;

            // Same glyphs and colors as the Entity Fields Analyser header
            tsbClose.Image = Fluent.Icon(Fluent.Glyph.Close, Fluent.TextSecondary);
            tsbLoad.Image = Fluent.Icon(Fluent.Glyph.Play, Fluent.TextOnBrand);
            tsbConnections.Image = LineIcons.ToBitmap(IconKind.Cloud, 16, Fluent.Brand);
            tsbExport.Image = Fluent.Icon(Fluent.Glyph.Export, Fluent.ExcelGreen);
            tsbOptions.Image = Fluent.Icon(Fluent.Glyph.Settings, Fluent.TextSecondary);
            tsddLanguage.Image = Fluent.Icon(Fluent.Glyph.Globe, Fluent.TextSecondary);
            tsbAuthor.Image = Fluent.Icon(Fluent.Glyph.Contact, Fluent.TextSecondary);
            tsbHelp.Image = Fluent.Icon(Fluent.Glyph.Help, Fluent.TextSecondary);
            tsbDonate.Image = Fluent.Icon(Fluent.Glyph.Heart, Fluent.HeartRed);
        }

        private void Strings_LanguageChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) ApplyLanguage();
        }

        /// <summary>Applies the current language to every static text, then refreshes what is displayed.</summary>
        private void ApplyLanguage()
        {
            _suppressEvents = true;
            try
            {
                overlay.StopText = Strings.Stop;
                overlay.StoppingText = Strings.Stopping;
                overlay.ElapsedFormat = Strings.ElapsedFormat;
                overlay.LongRunningText = Strings.LongRunning;

                tsbClose.Text = Strings.Close;
                tsbClose.ToolTipText = Strings.CloseTip;
                tsbLoad.Text = Strings.Load;
                tsbLoad.ToolTipText = Strings.LoadTip;
                tsbConnections.Text = Strings.Connections;
                tsbConnections.ToolTipText = Strings.ConnectionsTip;
                tsbExport.Text = Strings.Export;
                tsbExport.ToolTipText = Strings.ExportTip;
                tsbOptions.Text = Strings.Options;
                tsddLanguage.Text = Strings.IsFrench ? Strings.FrenchName : Strings.EnglishName;
                tsddLanguage.ToolTipText = Strings.LanguageMenu;
                tsmiEnglish.Checked = !Strings.IsFrench;
                tsmiFrench.Checked = Strings.IsFrench;
                tsbAuthor.Text = Strings.AuthorName;
                tsbAuthor.ToolTipText = Strings.AuthorTip;
                tsbDonate.Text = Strings.Donate;
                tsbDonate.ToolTipText = Strings.DonateTip;
                tsbHelp.Text = Strings.Help;
                tsbHelp.ToolTipText = Strings.HelpTip;

                tabs.Tabs[PageExplorer].Text = Strings.TabExplorer;
                tabs.Tabs[PageMatching].Text = Strings.TabMatching;
                tabs.Tabs[PageSteps].Text = Strings.TabSteps;
                tabs.Invalidate();

                cardTree.Title = Strings.TreeTitle;
                cardTree.Subtitle = Strings.TreeSubtitle;
                chkOnlyLinked.Text = Strings.OnlyWithSteps;
                toolTip.SetToolTip(btnAddNamespace, Strings.AddNamespaceTip);
                lblDetailEmptyTitle.Text = Strings.DetailEmptyTitle;
                lblDetailEmpty.Text = Strings.DetailEmpty;
                btnCopyRule.Text = Strings.CopyExpression;
                lblSubscriptions.Text = Strings.TileSubscriptions;
                lblRuleDetail.Text = Strings.SelectedFilter;
                SetHeaders(dgvSubscriptions, Strings.ColSubscription, Strings.ColActive, Strings.ColDeadLetter, Strings.ColFilters, Strings.ColForwardTo, Strings.ColStatus);
                SetHeaders(dgvRules, Strings.ColRule, Strings.ColType, Strings.ColExpression, Strings.ColAction);
                stepsExplorer.ApplyLanguage();
                messagesExplorer.ApplyLanguage();

                cardMatching.Title = Strings.MatchTitle;
                cardMatching.Subtitle = Strings.MatchSubtitle;
                tileEndpoints.Caption = Strings.TileEndpoints;
                tileLinked.Caption = Strings.TileLinked;
                tileProblems.Caption = Strings.TileProblems;
                tileNotConnected.Caption = Strings.TileNotConnected;
                tileUnused.Caption = Strings.TileUnused;
                btnOpenMatch.Text = Strings.OpenInExplorer;
                FillMatchFilter();
                SetHeaders(dgvMatching, Strings.ColMatch, Strings.ColEndpoint, Strings.ColContract, Strings.ColPath, Strings.ColNamespace, Strings.ColSteps, Strings.ColImages, Strings.ColMessages);

                cardSteps.Title = Strings.StepsTitle;
                cardSteps.Subtitle = Strings.StepsSubtitle;
                tileAllSteps.Caption = Strings.TileAllSteps;
                tileEnabledSteps.Caption = Strings.TileEnabledSteps;
                tileImages.Caption = Strings.ColImages;
                stepsAll.ApplyLanguage();

                if (IsHandleCreated) ApplyCues();
            }
            finally
            {
                _suppressEvents = false;
            }

            RefreshAll();
            AdjustHeader();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyCues();
        }

        private void ApplyCues()
        {
            Theme.SetCue(txtTreeFilter, Strings.TreeFilterCue);
            Theme.SetCue(txtMatchSearch, Strings.MatchSearchCue);
        }

        private static void SetHeaders(DataGridView grid, params string[] headers)
        {
            for (var i = 0; i < headers.Length && i < grid.Columns.Count; i++) grid.Columns[i].HeaderText = headers[i];
        }

        /// <summary>
        /// Keeps every header item visible (never in the overflow menu): on narrow windows the buttons show their icon only,
        /// starting with the links on the right (their text stays as tooltip).
        /// </summary>
        private void AdjustHeader()
        {
            if (tsHeader.Width <= 0) return;
            var compactable = new ToolStripItem[] { tsbAuthor, tsbHelp, tsbDonate, tsbOptions, tsddLanguage, tsbExport, tsbConnections };

            tsHeader.SuspendLayout();
            foreach (var item in compactable) item.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;

            bool Fits() => tsHeader.Items.Cast<ToolStripItem>().Where(i => i.Available)
                               .Sum(i => i.GetPreferredSize(Size.Empty).Width + i.Margin.Horizontal) + tsHeader.Padding.Horizontal + 24 <= tsHeader.Width;

            foreach (var item in compactable)
            {
                if (Fits()) break;
                item.DisplayStyle = ToolStripItemDisplayStyle.Image;
            }
            tsHeader.ResumeLayout(true);
        }

        #endregion

        #region Events

        private void WireEvents()
        {
            tsbClose.Click += (s, e) => CloseTool();
            tsbLoad.Click += (s, e) => ExecuteMethod(LoadAll);
            tsbConnections.Click += (s, e) => ShowConnections(null);
            tsbExport.Click += (s, e) => ExportReport();
            tsbOptions.Click += (s, e) => ShowOptions();
            tsmiEnglish.Click += (s, e) => SetLanguage(Language.English);
            tsmiFrench.Click += (s, e) => SetLanguage(Language.French);
            tsbAuthor.Click += (s, e) => OpenUrl(LinkedInUrl);
            tsbDonate.Click += (s, e) => OpenUrl(DonateUrl);
            tsbHelp.Click += (s, e) => TextViewerDialog.Show(this, Strings.Help, Strings.HelpSubtitle, Strings.HelpText);

            tabs.SelectedIndexChanged += (s, e) => ShowPage(tabs.SelectedIndex);
            tabsDetail.SelectedIndexChanged += (s, e) => ShowDetailPage();
            btnAddNamespace.Click += (s, e) => ShowConnections(null);
            txtTreeFilter.TextChanged += (s, e) => RebuildTree();
            chkOnlyLinked.CheckedChanged += (s, e) => RebuildTree();
            tree.SelectedNodeChanged += (s, e) => ShowSelection();
            btnAlert.Click += (s, e) => RunAlertAction();
            dgvSubscriptions.SelectionChanged += (s, e) => ShowSubscriptionRules();
            dgvRules.SelectionChanged += (s, e) => ShowRuleDetail();
            btnCopyRule.Click += (s, e) => CopyRule();

            cboMatchFilter.SelectedIndexChanged += (s, e) => { if (!_suppressEvents) FillMatching(); };
            txtMatchSearch.TextChanged += (s, e) => FillMatching();
            dgvMatching.CellPainting += PaintMatchStatus;
            dgvMatching.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OpenMatch(); };
            dgvMatching.SelectionChanged += (s, e) => UpdateMatchButton();
            btnOpenMatch.Click += (s, e) => OpenMatch();

            foreach (var panel in new[] { stepsExplorer, stepsAll })
            {
                panel.EditRequested += (s, step) => EditStep(step);
                panel.EnableRequested += (s, steps) => SetStepsEnabled(steps, true);
                panel.DisableRequested += (s, steps) => SetStepsEnabled(steps, false);
                panel.MessagesRequested += (s, steps) => ShowStepMessages(steps);
            }
            messagesExplorer.ReadRequested += (s, e) => ReadExplorerMessages();
        }

        private void ShowPage(int page)
        {
            pageExplorer.Visible = page == PageExplorer;
            pageMatching.Visible = page == PageMatching;
            pageSteps.Visible = page == PageSteps;
        }

        /// <summary>Opens a link in the default browser (replaced by the tests).</summary>
        internal static Action<string> UrlOpener = url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        private static void OpenUrl(string url)
        {
            try
            {
                UrlOpener(url);
            }
            catch (Exception)
            {
                // No default browser: nothing else to offer.
            }
        }

        #endregion

        #region Lifecycle / connection

        private void ServiceBusExplorerControl_Load(object sender, EventArgs e)
        {
            if (!SettingsManager.Instance.TryLoad(GetType(), out _settings) || _settings == null)
            {
                _settings = new PluginSettings();
                LogInfo("Default settings created.");
            }
            _settings.Connections = _settings.Connections ?? new List<SavedConnection>();

            Strings.Current = Strings.FromCode(_settings.Language);
            ApplyLanguage();
            _controlLoaded = true;

            // No automatic load: nothing is read until the user clicks Load.
        }

        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            _model = null;
            _endpoints = newService == null ? null : new EndpointService(newService);
            RefreshAll();

            // When actionName is set (connection requested by ExecuteMethod), the base class replays the action.
            base.UpdateConnection(newService, detail, actionName, parameter);

            if (detail != null) LogInfo("Connection: {0}", detail.WebApplicationUrl);
        }

        private void SetLanguage(Language language)
        {
            _settings.Language = Strings.ToCode(language);
            Strings.Current = language;
            SaveSettings();
        }

        private void ShowOptions()
        {
            using (var dialog = new OptionsDialog(_settings))
            {
                if (dialog.ShowModal(this) == DialogResult.OK) SaveSettings();
            }
        }

        /// <param name="host">Namespace to connect, when the user comes from a namespace of the tree.</param>
        private void ShowConnections(string host)
        {
            var missing = _model?.Namespaces.Where(n => n.IsGhost).Select(n => n.Host).ToList() ?? new List<string>();
            using (var dialog = new ConnectionsDialog(_settings, missing, host))
            {
                dialog.ShowModal(this);
                if (!dialog.Changed) return;
            }
            SaveSettings();
            SetInfo(Strings.ReloadHint, Theme.WarningText);
        }

        private void SaveSettings()
        {
            if (_settings == null || !_controlLoaded) return;
            SettingsManager.Instance.Save(GetType(), _settings);
        }

        public override void ClosingPlugin(XrmToolBox.Extensibility.PluginCloseInfo info)
        {
            SaveSettings();
            base.ClosingPlugin(info);
        }

        #endregion

        #region Load

        private sealed class LoadResult
        {
            public List<SbNamespace> Azure;
            public List<EndpointInfo> Endpoints;
            public List<StepInfo> Steps;
        }

        /// <summary>Reads Azure and Dataverse (public: invoked by ExecuteMethod after connecting).</summary>
        public void LoadAll()
        {
            if (_endpoints == null) _endpoints = new EndpointService(Service);
            var connections = _settings.Connections.ToList();
            var parallel = _settings.ParallelRequests;
            var settings = _settings;

            RunWithOverlay(Strings.Loading, (worker, token) =>
            {
                var result = new LoadResult { Azure = new List<SbNamespace>() };
                worker.ReportProgress(0, Strings.ReadingEndpoints);
                result.Endpoints = _endpoints.LoadEndpoints();
                worker.ReportProgress(0, Strings.ReadingSteps);
                result.Steps = _endpoints.LoadSteps(result.Endpoints);
                token.ThrowIfCancellationRequested();

                foreach (var connection in connections)
                {
                    worker.ReportProgress(0, connection.Method == ConnectionMethod.Browser ? Strings.SigningIn : Strings.ReadingNamespace(connection.Host));
                    result.Azure.Add(ReadNamespace(connection, settings, parallel, m => worker.ReportProgress(0, m), token));
                }
                return result;
            }, value =>
            {
                var result = (LoadResult)value;
                _azure = result.Azure;
                _loadedAt = DateTime.Now;

                // Azure was read again: the messages shown before are no longer current.
                _messagesKey = null;
                _model = MatchingService.Build(_azure, result.Endpoints, result.Steps);
                RefreshAll();
            });
        }

        /// <summary>A namespace that cannot be read must not stop the others: its error is displayed on its node.</summary>
        private static SbNamespace ReadNamespace(SavedConnection connection, PluginSettings settings, int parallel, Action<string> progress, CancellationToken token)
        {
            try
            {
                var ns = NamespaceLoader.Load(ConnectionFactory.CreateReader(connection, settings), parallel, progress, token);
                ns.SubscriptionName = connection.SubscriptionName;
                return ns;
            }
            catch (Exception error) when (!(error is OperationCanceledException))
            {
                return new SbNamespace { Host = connection.Host, Error = error, SubscriptionName = connection.SubscriptionName };
            }
        }

        /// <summary>Reads Dataverse again after a change, without calling Azure.</summary>
        private void ReloadDataverse(string doneMessage)
        {
            RunWithOverlay(Strings.Loading, (worker, token) =>
            {
                worker.ReportProgress(0, Strings.ReadingSteps);
                var endpoints = _endpoints.LoadEndpoints();
                return new LoadResult { Endpoints = endpoints, Steps = _endpoints.LoadSteps(endpoints) };
            }, value =>
            {
                var result = (LoadResult)value;
                _model = MatchingService.Build(_azure, result.Endpoints, result.Steps);
                RefreshAll();
                if (doneMessage != null) SetInfo(doneMessage, Theme.SuccessText);
            });
        }

        private void RefreshAll()
        {
            RebuildTree();
            FillMatching();
            FillStepsPage();
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            if (_model == null)
            {
                SetInfo(string.Empty, Theme.Muted);
                tabs.Tabs[PageMatching].Badge = null;
                tabs.Tabs[PageSteps].Badge = null;
                tabs.Invalidate();
                return;
            }

            var errors = _model.Namespaces.Count(n => n.Error != null);
            var text = Strings.Summary(_model.Namespaces.Count, _model.Endpoints.Count(e => e.IsServiceBus), _model.Steps.Count)
                       + (errors > 0 ? Strings.SummaryErrors(errors) : string.Empty)
                       + Strings.LoadedAt(_loadedAt.ToString("HH:mm"));
            SetInfo(text, errors > 0 ? Theme.WarningText : Theme.Muted);

            var problems = ProblemCount();
            tabs.Tabs[PageMatching].Badge = problems > 0 ? problems.ToString() : null;
            tabs.Tabs[PageSteps].Badge = _model.Steps.Count > 0 ? _model.Steps.Count.ToString() : null;
            tabs.Invalidate();
        }

        private void SetInfo(string text, Color color)
        {
            lblResultInfo.Text = text;
            lblResultInfo.ForeColor = color;
        }

        private void ExportReport()
        {
            if (_model == null)
            {
                MessageDialog.Show(this, MessageKind.Info, Strings.LoadFirstTitle, Strings.LoadFirst);
                return;
            }

            using (var dialog = new SaveFileDialog { Filter = Strings.ExportFilter, FileName = $"ServiceBus Explorer {DateTime.Now:yyyy-MM-dd HH-mm}.xlsx" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) ExportTo(dialog.FileName);
            }
        }

        internal void ExportTo(string path)
        {
            try
            {
                ExportService.Export(path, _model);
                MessageDialog.Show(this, MessageKind.Success, Strings.ExportDoneTitle, Strings.ExportDone(path));
            }
            catch (Exception error)
            {
                MessageDialog.ShowError(this, error);
            }
        }

        #endregion

        #region Step actions

        private void EditStep(StepInfo step)
        {
            ExecuteMethod(() =>
            {
                RunWithOverlay(Strings.PreparingEditor, (worker, token) =>
                {
                    worker.ReportProgress(0, Strings.PreparingEditor);
                    var messages = _endpoints.LoadMessages();
                    var tables = step.MessageId != Guid.Empty ? _endpoints.LoadTables(step.MessageId) : new Dictionary<string, Guid>();
                    return new object[] { messages, tables };
                }, value =>
                {
                    var data = (object[])value;
                    OpenEditor(step, (List<KeyValuePair<Guid, string>>)data[0], (Dictionary<string, Guid>)data[1]);
                });
            });
        }

        private void OpenEditor(StepInfo step, List<KeyValuePair<Guid, string>> messages, Dictionary<string, Guid> tables)
        {
            StepChange change;
            using (var dialog = new StepEditorDialog(step, messages, tables, _endpoints))
            {
                if (dialog.ShowModal(this) != DialogResult.OK) return;
                change = dialog.Change;
            }

            RunWithOverlay(Strings.Saving, (worker, token) =>
            {
                worker.ReportProgress(0, Strings.WritingStep(step.Name));
                _endpoints.SaveStep(change);
                return null;
            }, value => ReloadDataverse(Strings.StepSaved(step.Name)));
        }

        private void SetStepsEnabled(List<StepInfo> steps, bool enabled)
        {
            var targets = steps.Where(s => s.Enabled != enabled && (s.IsCustomizable || !s.IsManaged)).ToList();
            if (targets.Count == 0)
            {
                MessageDialog.Show(this, MessageKind.Info, Strings.ConfirmBulkTitle(enabled), Strings.NothingToChange);
                return;
            }

            var skipped = steps.Count(s => s.Enabled != enabled) - targets.Count;
            var message = Strings.ConfirmBulkMessage(targets.Count, enabled) + (skipped > 0 ? Environment.NewLine + Strings.BulkSkipped(skipped) : string.Empty);
            var details = string.Join(Environment.NewLine, targets.Select(s => s.Name));
            if (!MessageDialog.Ask(this, enabled ? MessageKind.Info : MessageKind.Warning, Strings.ConfirmBulkTitle(enabled), message,
                    details, Strings.ConfirmBulkQuestion, Strings.ApplyAction, Strings.CancelAction))
            {
                return;
            }

            ExecuteMethod(() =>
            {
                var done = 0;
                RunWithOverlay(Strings.Saving, (worker, token) =>
                {
                    foreach (var step in targets)
                    {
                        token.ThrowIfCancellationRequested();
                        worker.ReportProgress(done * 100 / targets.Count, Strings.WritingStep(step.Name));
                        _endpoints.SetEnabled(step, enabled);
                        done++;
                    }
                    return null;
                }, value => ReloadDataverse(Strings.BulkDone(done)), error =>
                {
                    // Part of the batch may be written: the screen must show the real state.
                    SetInfo(Strings.BulkFailed(done, targets.Count), Theme.WarningText);
                    ReloadDataverse(null);
                });
            });
        }

        #endregion

        #region Background tasks

        /// <summary>
        /// Runs a task in the background behind the loading overlay (replaces the XrmToolBox WorkAsync panel).
        /// The task can be stopped: its token is cancelled, and a task stuck in a server call is abandoned after a few seconds.
        /// </summary>
        private void RunWithOverlay(string title, Func<BackgroundWorker, CancellationToken, object> work, Action<object> done, Action<Exception> failed = null)
        {
            if (_busy != null) return;

            var task = new BusyTask { Worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true } };
            _busy = task;

            task.Worker.DoWork += (s, e) =>
            {
                try
                {
                    e.Result = work(task.Worker, task.Stop.Token);
                }
                catch (OperationCanceledException)
                {
                    e.Cancel = true;
                }
            };

            // Marshalled explicitly: the worker only comes back on the UI thread when it finds a WinForms synchronization context.
            task.Worker.ProgressChanged += (s, e) => OnUiThread(() =>
            {
                if (_busy == task) overlay.SetProgress(e.UserState as string, e.ProgressPercentage > 0 ? e.ProgressPercentage : (int?)null);
            });
            task.Worker.RunWorkerCompleted += (s, e) => OnUiThread(() =>
            {
                task.Worker.Dispose();
                if (task.Abandoned)
                {
                    LogInfo("Abandoned task finished: {0}", title);
                    return;
                }

                EndBusyTask(task);
                if (IsDisposed) return;
                if (e.Cancelled)
                {
                    ShowStopped();
                    return;
                }
                if (e.Error != null)
                {
                    LogError("{0}: {1}", title, e.Error.ToString());
                    MessageDialog.ShowError(this, e.Error);
                    failed?.Invoke(e.Error);
                    return;
                }
                done?.Invoke(e.Result);
            });

            overlay.Start(this, title, true);
            task.Worker.RunWorkerAsync();
        }

        private void OnUiThread(Action action)
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }

        private void StopBusyTask()
        {
            var task = _busy;
            if (task == null) return;

            task.Stop.Cancel();
            overlay.SetStopping();

            // A Dataverse call cannot be interrupted: if the task does not stop by itself, the UI is given back and the result ignored.
            var timer = new Timer { Interval = AbandonDelayMs };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                if (_busy != task || IsDisposed) return;
                task.Abandoned = true;
                EndBusyTask(task);
                ShowStopped();
            };
            timer.Start();
        }

        private void EndBusyTask(BusyTask task)
        {
            if (_busy != task) return;
            _busy = null;
            overlay.Finish();
        }

        private void ShowStopped()
        {
            SetInfo(Strings.TaskStopped, Theme.WarningText);
            LogInfo("Task stopped by the user.");
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing) Strings.LanguageChanged -= Strings_LanguageChanged;
            base.Dispose(disposing);
        }
    }
}
