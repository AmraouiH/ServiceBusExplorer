using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ServiceBusExplorer.Helpers;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.Models;
using ServiceBusExplorer.Services.Azure;
using ServiceBusExplorer.UI;
using ServiceBusExplorer.UI.Controls;

namespace ServiceBusExplorer.Forms
{
    /// <summary>
    /// Namespaces read by the tool: the connected ones, and the two ways to add one
    /// (connection string, Microsoft account).
    /// </summary>
    [DesignerCategory("Code")]
    public class ConnectionsDialog : ThemedDialog
    {
        private const int PageConnected = 0;
        private const int PageKey = 1;
        private const int PageMicrosoft = 2;

        private readonly PluginSettings _settings;
        private readonly SegmentedTabs _tabs;
        private readonly Panel[] _pages;
        private readonly DataGridView _saved;
        private readonly ModernButton _remove;
        private readonly TextBox _connectionString;
        private readonly ModernButton _testAndAdd;
        private readonly ModernButton _signIn;
        private readonly ModernButton _useCli;
        private readonly ModernButton _addSelected;
        private readonly DataGridView _found;
        private readonly Label _keyStatus;
        private readonly Label _msStatus;
        private readonly Label _needed;
        private readonly List<string> _missing;
        private CancellationTokenSource _running;
        private ConnectionMethod _discoveredWith;

        public ConnectionsDialog(PluginSettings settings, List<string> missingHosts, string host)
            : base(Strings.ConnectionsTitle, Strings.ConnectionsSubtitle)
        {
            _settings = settings;
            _missing = missingHosts;
            ClientSize = new Size(840, 600);

            _tabs = new SegmentedTabs { Dock = DockStyle.Top, Height = 44 };
            _tabs.Tabs.Add(new SegmentedTabs.Tab { Text = Strings.TabConnected, Glyph = IconKind.Cloud, BadgeColor = Theme.Accent });
            _tabs.Tabs.Add(new SegmentedTabs.Tab { Text = Strings.TabConnectionString, Glyph = IconKind.Key });
            _tabs.Tabs.Add(new SegmentedTabs.Tab { Text = Strings.TabMicrosoft, Glyph = IconKind.Contact });
            _tabs.SelectedIndexChanged += (s, e) => ShowPage();

            var card = new CardPanel { Dock = DockStyle.Fill };
            var pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };
            _pages = new[] { NewPage(), NewPage(), NewPage() };
            pageHost.Controls.AddRange(_pages);
            card.Controls.Add(pageHost);

            // Connected namespaces
            _saved = NewGrid();
            _saved.Columns.AddRange(Column("colHost", 0), Column("colMethod", 170), Column("colDetail", 250));
            _saved.Columns["colHost"].HeaderText = Strings.ColHost;
            _saved.Columns["colHost"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _saved.Columns["colHost"].DefaultCellStyle.Font = Theme.Semibold;
            _saved.Columns["colMethod"].HeaderText = Strings.ColMethod;
            _saved.Columns["colDetail"].HeaderText = Strings.ColDetail;
            _saved.SelectionChanged += (s, e) => _remove.Enabled = _saved.SelectedRows.Count > 0;
            _needed = new Label { Dock = DockStyle.Bottom, Height = 34, ForeColor = Theme.WarningText, Font = Theme.Small, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            var savedButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, BackColor = Theme.Card, Padding = new Padding(0, 8, 0, 0) };
            _remove = Button(Strings.Remove, IconKind.Trash, ButtonVariant.Danger);
            _remove.Click += (s, e) => RemoveSelected();
            savedButtons.Controls.Add(_remove);
            _pages[PageConnected].Controls.Add(_saved);
            _pages[PageConnected].Controls.Add(savedButtons);
            _pages[PageConnected].Controls.Add(_needed);

            // Connection string
            var keyStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Card };
            keyStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _connectionString = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
            _testAndAdd = Button(Strings.TestAndAdd, IconKind.Check, ButtonVariant.Primary);
            _testAndAdd.Click += async (s, e) => await AddConnectionString();
            _keyStatus = new Label { AutoSize = true, MaximumSize = new Size(760, 0), ForeColor = Theme.Muted, Margin = new Padding(0, 12, 0, 0) };
            keyStack.Controls.Add(new Label { Text = Strings.ConnectionStringLabel, AutoSize = true, Font = Theme.Semibold, ForeColor = Theme.Muted, Margin = new Padding(0, 8, 0, 4) });
            keyStack.Controls.Add(_connectionString);
            keyStack.Controls.Add(new Label { Text = Strings.ConnectionStringHint, AutoSize = true, MaximumSize = new Size(760, 0), Font = Theme.Small, ForeColor = Theme.Muted, Margin = new Padding(0, 8, 0, 12) });
            keyStack.Controls.Add(_testAndAdd);
            keyStack.Controls.Add(_keyStatus);
            _pages[PageKey].Controls.Add(keyStack);

            // Microsoft account
            var msTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.Card, WrapContents = false };
            _signIn = Button(Strings.SignInBrowser, IconKind.Contact, ButtonVariant.Primary);
            _signIn.Click += async (s, e) => await Discover(ConnectionMethod.Browser);
            _useCli = Button(Strings.UseAzureCli, IconKind.Code, ButtonVariant.Secondary);
            _useCli.Click += async (s, e) => await Discover(ConnectionMethod.AzureCli);
            msTop.Controls.AddRange(new Control[] { _signIn, _useCli });
            var msHint = new Label { Text = Strings.MicrosoftHint, Dock = DockStyle.Top, Height = 24, Font = Theme.Small, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
            _msStatus = new Label { Dock = DockStyle.Top, Height = 24, ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            _found = NewGrid();
            _found.ReadOnly = false;
            _found.Columns.Add(new DataGridViewCheckBoxColumn { Name = "colPick", Width = 34, HeaderText = string.Empty });
            _found.Columns.AddRange(Column("colName", 0), Column("colSubscription", 190), Column("colGroup", 140), Column("colTier", 80));
            _found.Columns["colName"].HeaderText = Strings.ColHost;
            _found.Columns["colName"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _found.Columns["colSubscription"].HeaderText = Strings.ColSubscriptionAzure;
            _found.Columns["colGroup"].HeaderText = Strings.ColResourceGroup;
            _found.Columns["colTier"].HeaderText = Strings.ColTier;
            _found.CellContentClick += (s, e) => { if (e.ColumnIndex == 0) _found.CommitEdit(DataGridViewDataErrorContexts.Commit); UpdateButtons(); };
            _found.CellValueChanged += (s, e) => UpdateButtons();
            var msButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, BackColor = Theme.Card, Padding = new Padding(0, 8, 0, 0) };
            _addSelected = Button(Strings.AddSelected, IconKind.Plus, ButtonVariant.Primary);
            _addSelected.Enabled = false;
            _addSelected.Click += (s, e) => AddSelected();
            msButtons.Controls.Add(_addSelected);
            _pages[PageMicrosoft].Controls.Add(_found);
            _pages[PageMicrosoft].Controls.Add(msButtons);
            _pages[PageMicrosoft].Controls.Add(_msStatus);
            _pages[PageMicrosoft].Controls.Add(msHint);
            _pages[PageMicrosoft].Controls.Add(msTop);

            Body.Controls.Add(card);
            Body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 6, BackColor = Theme.Canvas });
            Body.Controls.Add(_tabs);

            CancelButton = AddFooterButton(Strings.Close, ButtonVariant.Secondary, DialogResult.Cancel);

            FillSaved();
            _tabs.SelectedIndex = host != null || settings.Connections.Count == 0 ? PageKey : PageConnected;
            if (host != null) SetKeyStatus(Strings.ConnectHost(host), Theme.Text);
            ShowPage();
            FormClosed += (s, e) => _running?.Cancel();
        }

        /// <summary>A namespace was added or removed.</summary>
        public bool Changed { get; private set; }

        private void ShowPage()
        {
            for (var i = 0; i < _pages.Length; i++) _pages[i].Visible = i == _tabs.SelectedIndex;
        }

        private static Panel NewPage() => new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Visible = false };

        private void FillSaved()
        {
            _saved.Rows.Clear();
            foreach (var connection in _settings.Connections.OrderBy(c => c.Host, StringComparer.OrdinalIgnoreCase))
            {
                var index = _saved.Rows.Add(connection.Host, Strings.MethodLabel(connection.Method), connection.Method == ConnectionMethod.ConnectionString ? Strings.KeyStored : connection.SubscriptionName);
                _saved.Rows[index].Tag = connection;
            }

            _tabs.Tabs[PageConnected].Badge = _settings.Connections.Count.ToString();
            _tabs.Invalidate();
            _remove.Enabled = _saved.SelectedRows.Count > 0;

            var needed = _missing.Where(h => _settings.Connections.All(c => !string.Equals(c.Host, h, StringComparison.OrdinalIgnoreCase))).ToList();
            _needed.Text = needed.Count > 0 ? Strings.NeededByDataverse(string.Join(", ", needed)) : string.Empty;
        }

        private void RemoveSelected()
        {
            foreach (DataGridViewRow row in _saved.SelectedRows) _settings.Connections.Remove((SavedConnection)row.Tag);
            Changed = true;
            FillSaved();
        }

        private void Upsert(SavedConnection connection)
        {
            _settings.Connections.RemoveAll(c => string.Equals(c.Host, connection.Host, StringComparison.OrdinalIgnoreCase));
            _settings.Connections.Add(connection);
            Changed = true;
            FillSaved();
        }

        private void SetBusy(bool busy)
        {
            _testAndAdd.Enabled = !busy;
            _signIn.Enabled = !busy;
            _useCli.Enabled = !busy;
            UseWaitCursor = busy;
            if (!busy) UpdateButtons();
        }

        private void UpdateButtons() => _addSelected.Enabled = !UseWaitCursor && Checked().Any();

        private async Task AddConnectionString()
        {
            var text = _connectionString.Text.Trim();
            ServiceBusConnectionInfo info;
            try
            {
                info = ServiceBusConnectionInfo.Parse(text);
                if (!string.IsNullOrEmpty(info.EntityPath)) throw new FormatException(Strings.ConnectionStringEntityScoped);
            }
            catch (FormatException error)
            {
                SetKeyStatus(error.Message, Theme.Danger);
                return;
            }

            SetBusy(true);
            SetKeyStatus(Strings.TestingConnection, Theme.Muted);
            _running = new CancellationTokenSource();
            var token = _running.Token;
            try
            {
                await Task.Run(() => new SasServiceBusReader(info).ReadQueues(token));
                Upsert(new SavedConnection { Host = info.Host, Method = ConnectionMethod.ConnectionString, ProtectedSecret = SecretStore.Protect(text) });
                _connectionString.Clear();
                SetKeyStatus(Strings.ConnectionAdded(info.Host), Theme.SuccessText);
            }
            catch (OperationCanceledException)
            {
                // The popup was closed.
            }
            catch (Exception error)
            {
                SetKeyStatus(string.Empty, Theme.Muted);
                MessageDialog.ShowError(this, error);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private void SetKeyStatus(string text, Color color)
        {
            _keyStatus.Text = text;
            _keyStatus.ForeColor = color;
        }

        private async Task Discover(ConnectionMethod method)
        {
            SetBusy(true);
            _msStatus.Text = Strings.Searching;
            _found.Rows.Clear();
            _running = new CancellationTokenSource();
            var token = _running.Token;
            IArmTokenProvider provider = method == ConnectionMethod.AzureCli
                ? (IArmTokenProvider)new AzureCliTokenProvider()
                : new BrowserTokenProvider(_settings.ClientId, _settings.TenantId);
            var tenant = _settings.TenantId;

            try
            {
                var found = await Task.Run(() => new ArmClient(provider).DiscoverNamespaces(tenant,
                    subscription =>
                    {
                        if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(() => _msStatus.Text = Strings.SearchingIn(subscription)));
                    }, token));

                _discoveredWith = method;
                foreach (var ns in found)
                {
                    var index = _found.Rows.Add(false, ns.Host, ns.SubscriptionName, ns.ResourceGroup, ns.Sku);
                    _found.Rows[index].Tag = ns;
                }
                _msStatus.Text = found.Count > 0 ? Strings.FoundNamespaces(found.Count) : Strings.NoNamespaceFound;
            }
            catch (OperationCanceledException)
            {
                // The popup was closed.
            }
            catch (Exception error)
            {
                _msStatus.Text = string.Empty;
                MessageDialog.ShowError(this, error);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private IEnumerable<DiscoveredNamespace> Checked() =>
            _found.Rows.Cast<DataGridViewRow>().Where(r => true.Equals(r.Cells[0].Value)).Select(r => (DiscoveredNamespace)r.Tag);

        private void AddSelected()
        {
            var selected = Checked().ToList();
            foreach (var ns in selected)
            {
                Upsert(new SavedConnection { Host = ns.Host, Method = _discoveredWith, ResourceId = ns.ResourceId, TenantId = ns.TenantId, SubscriptionName = ns.SubscriptionName });
            }
            _msStatus.Text = Strings.ConnectionsAdded(selected.Count);
            foreach (DataGridViewRow row in _found.Rows) row.Cells[0].Value = false;
            UpdateButtons();
        }

        private static ModernButton Button(string text, IconKind glyph, ButtonVariant variant) => new ModernButton
        {
            Text = text,
            Variant = variant,
            Glyph = glyph,
            AutoSize = true,
            MinimumSize = new Size(110, 32),
            Height = 32,
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
    }
}
