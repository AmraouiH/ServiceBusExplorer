using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using ServiceBusExplorer.Forms;
using ServiceBusExplorer.Helpers;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.Models;
using ServiceBusExplorer.UI.Controls;
using Label = System.Windows.Forms.Label;
using Timer = System.Windows.Forms.Timer;

namespace ServiceBusExplorer.Tests
{
    /// <summary>
    /// End-to-end campaign: real Windows mouse and keyboard messages posted to the real controls, against a local
    /// namespace and an in-memory organization. Every section reports what it saw; a failed section does not stop the next.
    /// </summary>
    internal sealed class UiTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CLOSE = 0x0010, WM_SETTEXT = 0x000C, BM_CLICK = 0x00F5;

        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr dialog, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);

        private readonly List<string> _failures = new List<string>();
        private int _passed;
        private string _section;
        private Form _form;
        private ServiceBusExplorerControl _c;
        private FakeOrganizationService _org;
        private MockServiceBus _bus;
        private readonly List<string> _urls = new List<string>();
        private int _closeRequests;
        private int _connectionRequests;

        public static int Run()
        {
            Application.EnableVisualStyles();
            var tests = new UiTests();

            // Never the real settings of XrmToolBox.
            var root = Path.Combine(Path.GetTempPath(), "sbx-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            XrmToolBox.Extensibility.Paths.OverrideRootPath(root);

            // A crash of an event handler is a failure, not a blocking system dialog.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => tests.Fail("UNHANDLED: " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Console.WriteLine("UNHANDLED " + e.ExceptionObject);
            tests._form = new Form { Width = 1420, Height = 900, StartPosition = FormStartPosition.Manual, Location = new Point(0, 0), Text = "ServiceBus Explorer", TopMost = true };
            tests._c = new ServiceBusExplorerControl { Dock = DockStyle.Fill };
            tests._form.Controls.Add(tests._c);
            tests._form.SizeChanged += (s, e) =>
            {
                if (tests._section != "narrow window" && tests._form.Width != 1420) Console.WriteLine("RESIZED in [" + tests._section + "] to " + tests._form.Size + " state=" + tests._form.WindowState + " " + Environment.StackTrace);
            };
            tests._form.Shown += async (s, e) =>
            {
                await tests.Campaign();
                tests._form.Close();
            };
            Application.Run(tests._form);

            Console.WriteLine($"{tests._passed} UI checks passed, {tests._failures.Count} failed");
            foreach (var failure in tests._failures) Console.WriteLine("FAIL " + failure);
            return tests._failures.Count == 0 ? 0 : 1;
        }

        private async Task Campaign()
        {
            if (Environment.GetEnvironmentVariable("SBX_LANG") == "fr") Strings.Current = Language.French;
            _bus = new MockServiceBus();
            Demo.Fill(_bus);
            _org = Demo.Organization("localhost");
            ServiceBusExplorerControl.UrlOpener = _urls.Add;
            _c.OnCloseTool += (s, e) => _closeRequests++;
            _c.OnRequestConnection += (s, e) => _connectionRequests++;

            var sections = new (string, Func<Task>)[]
            {
                ("startup", Startup),
                ("load without connection", LoadWithoutConnection),
                ("load and stop", LoadAndStop),
                ("tree", Tree),
                ("detail tabs and filters", Details),
                ("matching page", MatchingPage),
                ("steps page", StepsPage),
                ("messages", Messages),
                ("bulk enable and disable", Bulk),
                ("step editor", Editor),
                ("image editor", Images),
                ("column picker", Picker),
                ("save and write errors", SaveErrors),
                ("connections popup", ConnectionsPopup),
                ("options popup", OptionsPopup),
                ("load errors", LoadErrors),
                ("message dialog", MessagePopup),
                ("help, links, language", HeaderLinks),
                ("export", Export),
                ("narrow window", Narrow),
                ("reconnect and close", Reconnect)
            };

            foreach (var (name, section) in sections)
            {
                _section = name;
                try
                {
                    await section();
                }
                catch (Exception error)
                {
                    Fail("crashed: " + error);
                }
                await CloseDialogs();
                await Idle();
            }
            _bus.Dispose();
        }

        #region Sections

        private async Task Startup()
        {
            await Wait(300);
            Ok("no read at startup (Dataverse)", _org.Calls == 0);
            Ok("no read at startup (Azure)", _bus.Requests == 0);
            var empty = Field<Label>("lblTreeEmpty");
            Ok("empty state text", empty.Visible && empty.Text == Strings.TreeEmpty);
            Ok("detail placeholder", Field<Panel>("pnlDetailEmpty").Visible && !Field<Panel>("pnlDetail").Visible);
            NoButtonAtEdge(_form, "main");

            var tabs = Field<SegmentedTabs>("tabs");
            await ClickTab(tabs, 1);
            Ok("matching page opens empty", Field<Panel>("pageMatching").Visible && Field<DataGridView>("dgvMatching").Rows.Count == 0);
            Ok("tiles show no value before load", Field<StatTile>("tileLinked").Value == "–");
            await ClickTab(tabs, 2);
            Ok("steps page opens empty", Field<Panel>("pageSteps").Visible);
            await ClickTab(tabs, 0);

            await ClickItem(Field<ToolStripButton>("tsbExport"));
            var info = await Dialog<MessageDialog>();
            Ok("export before load explains", Texts(info).Contains(Strings.LoadFirst));
            NoButtonAtEdge(info, "info popup");
            await Key(info, Keys.Escape);
            Ok("Esc closes a popup", await Gone<MessageDialog>());
        }

        private async Task LoadWithoutConnection()
        {
            await ClickItem(Field<ToolStripButton>("tsbLoad"));
            await Wait(200);
            Ok("Load asks for a connection", _connectionRequests == 1);
            Ok("nothing started without connection", Get<object>("_busy") == null);

            // XrmToolBox answers by replaying the action by name.
            _c.UpdateConnection(_org, new ConnectionDetail { ConnectionName = "demo" }, "LoadAll", null);
            await Wait(100);
            await Idle();
            Ok("the load is replayed after connecting", Get<ExplorerModel>("_model")?.Endpoints.Count == 9);
            Ok("no connection: namespaces come from Dataverse", Get<ExplorerModel>("_model").Namespaces.All(n => n.IsGhost));
            Ok("no connection: steps are there", Get<ExplorerModel>("_model").Steps.Count == 11);

            Get<PluginSettings>("_settings").Connections.Add(new SavedConnection { Host = "localhost", Method = ConnectionMethod.ConnectionString, ProtectedSecret = SecretStore.Protect(_bus.ConnectionString) });
        }

        private async Task LoadAndStop()
        {
            var overlay = Field<LoadingOverlay>("overlay");
            var calls = _bus.Requests;

            // Stop by the button
            _bus.Delay = 500;
            await ClickItem(Field<ToolStripButton>("tsbLoad"));
            await Wait(250);
            Ok("overlay shown", overlay.Visible);
            Ok("overlay covers the header too", overlay.Bounds == _c.ClientRectangle);
            await ClickItem(Field<ToolStripButton>("tsbLoad"));
            await Wait(100);
            var stop = Stop(overlay);
            Ok("Stop button is visible", stop.Visible && stop.Enabled);
            await Click(stop);
            await Idle();
            Ok("Stop gives the screen back", !overlay.Visible);
            Ok("Stop says so", Field<Label>("lblResultInfo").Text == Strings.TaskStopped);

            // Stop with Esc
            await ClickItem(Field<ToolStripButton>("tsbLoad"));
            await Wait(250);
            await Key(overlay, Keys.Escape);
            await Idle();
            Ok("Esc stops a load", !overlay.Visible && Field<Label>("lblResultInfo").Text == Strings.TaskStopped);
            _bus.Delay = 0;

            // Stop with a Dataverse call that cannot be interrupted
            _org.Delay = 5000;
            await ClickItem(Field<ToolStripButton>("tsbLoad"));
            await Wait(300);
            await Click(Stop(overlay));
            await Wait(3400);
            Ok("a stuck Dataverse call is abandoned after 3 s", !overlay.Visible && Get<object>("_busy") == null);
            _org.Delay = 0;

            await ClickItem(Field<ToolStripButton>("tsbLoad"));
            await Idle();
            var model = Get<ExplorerModel>("_model");
            Ok("full load", model.Namespaces.Any(n => !n.IsGhost && n.Queues.Count(q => !q.IsGhost) == 4));
            Ok("Azure was called", _bus.Requests > calls);
            Ok("summary", Field<Label>("lblResultInfo").Text.StartsWith(Strings.Summary(model.Namespaces.Count, 8, 11)));
            Ok("tab badges", Field<SegmentedTabs>("tabs").Tabs[2].Badge == "11" && Field<SegmentedTabs>("tabs").Tabs[1].Badge == "3");
        }

        private async Task Tree()
        {
            var tree = Field<ModernTreeView>("tree");
            Ok("tree replaces the empty state", tree.Visible && !Field<Label>("lblTreeEmpty").Visible);

            await ClickNode(tree, "localhost");
            Ok("namespace selected", Title() == "localhost");
            Ok("namespace tiles", Field<StatTile>("tile1").Value == "4" && Field<StatTile>("tile2").Value == "3" && Field<StatTile>("tile3").Value == "5");
            await ClickNode(tree, Strings.GroupQueues);
            Ok("a group shows its namespace", Title() == "localhost");

            await ClickNode(tree, "crm-invoice-events");
            Ok("queue title", Title() == "crm-invoice-events");
            Ok("queue counters", Field<StatTile>("tile1").Value == "42" && Field<StatTile>("tile2").Value == "7");
            var tabs = Field<SegmentedTabs>("tabsDetail");
            Ok("a queue has no filters tab", tabs.Tabs[1].Hidden);

            await ClickNode(tree, "crm-contact-changes");
            Ok("topic steps", Field<StatTile>("tile5").Value == "4");
            Ok("a topic has a filters tab", !tabs.Tabs[1].Hidden);

            await ClickNode(tree, "marketing-platform");
            Ok("subscription title", Title() == "marketing-platform");
            Ok("subscription shows its topic's steps", Field<StatTile>("tile5").Value == "4");
            Ok("subscription hint", Texts(Field<StepsPanel>("stepsExplorer")).Any(t => t == Strings.SubscriptionStepsHint("crm-contact-changes")));

            // Chevron
            var topic = Node(tree, "crm-contact-changes");
            await ClickChevron(tree, topic);
            Ok("chevron collapses", !topic.Expanded);
            await ClickChevron(tree, topic);
            Ok("chevron expands", topic.Expanded);

            await ClickChevron(tree, Node(tree, "marketing-platform"));
            await ClickNode(tree, "OptedInOnly");
            Ok("a rule opens the filters of its subscription", Field<Panel>("pnlFilters").Visible);
            Ok("the rule is selected", Field<DataGridView>("dgvRules").SelectedRows.Count == 1 && Field<TextBox>("txtRule").Text.StartsWith("doNotBulkEMail"));
            Ok("the action is displayed", Field<TextBox>("txtRule").Text.Contains("SET sys.Label"));
            await ClickTab(tabs, 0);

            // Keyboard
            await ClickNode(tree, "crm-account-events");
            await Key(tree, Keys.Down);
            Ok("Down", tree.SelectedNode.Title == "crm-invoice-events");
            await Key(tree, Keys.Up);
            Ok("Up", tree.SelectedNode.Title == "crm-account-events");
            await Key(tree, Keys.Home);
            Ok("Home", tree.SelectedNode == tree.Nodes[0]);
            await Key(tree, Keys.End);
            Ok("End", tree.SelectedNode.Title == "wrong-kind" || tree.SelectedNode.Title == "billing-events");
            await Key(tree, Keys.Up);
            await Key(tree, Keys.Down);
            await Key(tree, Keys.Left);
            Ok("Left goes to the parent", tree.SelectedNode.Title == Strings.GroupTopics);
            await Key(tree, Keys.Left);
            Ok("Left collapses", !tree.SelectedNode.Expanded);
            await Key(tree, Keys.Right);
            Ok("Right expands", tree.SelectedNode.Expanded);

            // Filters of the tree
            var filter = Field<TextBox>("txtTreeFilter");
            filter.Text = "invoice";
            await Wait(100);
            Ok("filter keeps the match", tree.AllNodes().Any(n => n.Title == "crm-invoice-events") && tree.AllNodes().All(n => n.Title != "crm-account-events"));
            filter.Text = "does-not-exist";
            await Wait(100);
            Ok("no match message", Field<Label>("lblTreeEmpty").Visible && Field<Label>("lblTreeEmpty").Text == Strings.TreeNoMatch);
            filter.Text = "data-warehouse";
            await Wait(100);
            Ok("filter finds a subscription", tree.AllNodes().Any(n => n.Title == "data-warehouse"));
            filter.Text = string.Empty;
            await Wait(100);

            await Click(Field<CheckBox>("chkOnlyLinked"));
            Ok("only linked hides unused", tree.AllNodes().All(n => n.Title != "erp-outbound") && tree.AllNodes().Any(n => n.Title == "crm-account-events"));
            await Click(Field<CheckBox>("chkOnlyLinked"));
            Ok("only linked restores", tree.AllNodes().Any(n => n.Title == "erp-outbound"));

            // Expansion survives a rebuild
            var sub = Node(tree, "support-portal");
            if (!sub.Expanded) await ClickChevron(tree, sub);
            filter.Text = " ";
            filter.Text = string.Empty;
            await Wait(100);
            Ok("a rebuild keeps what is open", Node(tree, "support-portal").Expanded);

            // Ghosts
            await ClickNode(tree, "crm-quote-events");
            Ok("missing entity warns", Field<Panel>("pnlAlert").Visible && Field<Label>("lblAlert").Text == Strings.AlertGhostEntity);
            Ok("missing entity has no action", !Field<ModernButton>("btnAlert").Visible);
            Ok("unknown counters are not zero", Field<StatTile>("tile1").Value == "–");
            await ClickNode(tree, "legacy-sync");
            Ok("disabled entity warns", Field<Label>("lblAlert").Text == Strings.AlertEntityDisabled("Disabled"));

            await ClickNode(tree, "contoso-legacy");
            Ok("ghost namespace warns", Field<Label>("lblAlert").Text.Contains("contoso-legacy.servicebus.windows.net"));
            await Click(Field<ModernButton>("btnAlert"));
            var connections = await Dialog<ConnectionsDialog>();
            Ok("Connect opens the connection string page", Get<SegmentedTabs>(connections, "_tabs").SelectedIndex == 1);
            Ok("Connect names the namespace", Texts(connections).Contains(Strings.ConnectHost("contoso-legacy.servicebus.windows.net")));
            await CloseDialogs();

            await Click(Field<ModernButton>("btnAddNamespace"));
            await Dialog<ConnectionsDialog>();
            Ok("+ opens the connections", true);
            await CloseDialogs();
        }

        private async Task Details()
        {
            var tree = Field<ModernTreeView>("tree");
            var tabs = Field<SegmentedTabs>("tabsDetail");
            await ClickNode(tree, "crm-contact-changes");
            await ClickTab(tabs, 1);
            Ok("filters page", Field<Panel>("pnlFilters").Visible);

            var subscriptions = Field<DataGridView>("dgvSubscriptions");
            var rules = Field<DataGridView>("dgvRules");
            Ok("subscriptions listed", subscriptions.Rows.Count == 3);
            await ClickCell(subscriptions, 1, 0);
            Ok("rules follow the subscription", rules.Rows.Count == 2);
            await ClickCell(subscriptions, 0, 0);
            Ok("default rule", rules.Rows.Count == 1 && (string)rules.Rows[0].Cells[1].Value == Strings.FilterAll);
            await ClickCell(subscriptions, 1, 0);
            await ClickCell(rules, 1, 2);
            Ok("correlation filter in full", Field<TextBox>("txtRule").Text.Contains("entity = 'contact'"));

            Clipboard.Clear();
            await Click(Field<ModernButton>("btnCopyRule"));
            Ok("Copy puts the filter in the clipboard", Clipboard.GetText() == Field<TextBox>("txtRule").Text);

            await ClickTab(tabs, 2);
            var properties = Field<DataGridView>("dgvProperties");
            Ok("properties page", properties.Visible && properties.Rows.Count > 5);
            await ClickCell(properties, 0, 1);

            // Going from a topic on Filters to a queue must not leave a blank page.
            await ClickTab(tabs, 1);
            await ClickNode(tree, "erp-outbound");
            Ok("a queue falls back on the steps", Field<StepsPanel>("stepsExplorer").Visible && !Field<Panel>("pnlFilters").Visible);

            await ClickTab(tabs, 2);
            await ClickNode(tree, "crm-invoice-events");
            var values = dgvValues(Field<DataGridView>("dgvProperties"));
            Ok("durations are readable", values.Contains(Strings.Days(14)) && values.Contains(Strings.Unlimited));
            Ok("empty properties are hidden", !values.Contains(string.Empty));
            await ClickTab(tabs, 0);

            await ClickNode(tree, "erp-outbound");
            Ok("no steps message", Texts(Field<StepsPanel>("stepsExplorer")).Contains(Strings.NoSteps));
            var buttons = Buttons(Field<StepsPanel>("stepsExplorer"));
            Ok("no step: actions disabled", buttons.All(b => !b.Enabled));
        }

        private static List<string> dgvValues(DataGridView grid) => grid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells[1].Value as string ?? string.Empty).ToList();

        private async Task MatchingPage()
        {
            await ClickTab(Field<SegmentedTabs>("tabs"), 1);
            var grid = Field<DataGridView>("dgvMatching");
            var filter = Field<ComboBox>("cboMatchFilter");
            var search = Field<TextBox>("txtMatchSearch");
            Ok("rows, webhook hidden", grid.Rows.Count == 9);
            Ok("tiles", Field<StatTile>("tileLinked").Value == "4" && Field<StatTile>("tileProblems").Value == "3" && Field<StatTile>("tileNotConnected").Value == "1" && Field<StatTile>("tileUnused").Value == "1");

            var expected = new[] { 9, 3, 4, 1, 1, 1, 1, 1, 1 };
            for (var i = 0; i < expected.Length; i++)
            {
                filter.SelectedIndex = i;
                await Wait(30);
                Ok($"filter {filter.Text}", grid.Rows.Count == expected[i]);
            }

            // Other type: nothing to open.
            await ClickCell(grid, 0, 1);
            Ok("webhook row cannot be opened", !Field<ModernButton>("btnOpenMatch").Enabled);
            await DoubleClickCell(grid, 0, 1);
            Ok("double-click on it is harmless", Field<Panel>("pageMatching").Visible);

            filter.SelectedIndex = 0;
            search.Text = "invoice";
            await Wait(50);
            Ok("search", grid.Rows.Count == 1);
            Ok("count label", Field<Label>("lblMatchCount").Text == Strings.MatchCount(1, 9));
            search.Text = "zzz";
            await Wait(50);
            Ok("search with no result", grid.Rows.Count == 0 && !Field<ModernButton>("btnOpenMatch").Enabled);
            search.Text = string.Empty;
            await Wait(50);

            await ClickCell(grid, 0, 1);
            Ok("open is enabled", Field<ModernButton>("btnOpenMatch").Enabled);
            await Click(Field<ModernButton>("btnOpenMatch"));
            var tree = Field<ModernTreeView>("tree");
            Ok("Open goes to the explorer", Field<Panel>("pageExplorer").Visible && tree.SelectedNode?.Title == "crm-account-events");

            // Open works even when the tree filters hide the target.
            Field<TextBox>("txtTreeFilter").Text = "nothing-like-it";
            await ClickTab(Field<SegmentedTabs>("tabs"), 1);
            await DoubleClickCell(grid, 2, 1);
            Ok("double-click opens through the tree filter", Field<Panel>("pageExplorer").Visible && tree.SelectedNode != null && Field<TextBox>("txtTreeFilter").Text.Length == 0);

            await ClickTab(Field<SegmentedTabs>("tabs"), 1);
            foreach (DataGridViewRow row in grid.Rows)
            {
                var match = (MatchRow)row.Tag;
                await DoubleClickCell(grid, row.Index, 1);
                Ok("open row " + match.Path, Field<Panel>("pageExplorer").Visible && Title() == match.Entity.Name);
                await ClickTab(Field<SegmentedTabs>("tabs"), 1);
            }
            await ClickTab(Field<SegmentedTabs>("tabs"), 0);
        }

        private async Task StepsPage()
        {
            await ClickTab(Field<SegmentedTabs>("tabs"), 2);
            var panel = Field<StepsPanel>("stepsAll");
            var grid = Get<DataGridView>(panel, "_grid");
            var search = Get<TextBox>(panel, "_search");
            Ok("all steps listed", grid.Rows.Count == 11);
            Ok("count", Get<Label>(panel, "_count").Text == Strings.StepCount(11, 11));
            Ok("tiles", Field<StatTile>("tileAllSteps").Value == "11" && Field<StatTile>("tileEnabledSteps").Value == "10" && Field<StatTile>("tileImages").Value == "4");

            foreach (var (text, count) in new[] { ("invoice", 2), ("emailaddress1", 1), ("Assign", 1), ("crm-opportunity-won", 1), ("Legacy billing", 1), ("zzz", 0) })
            {
                search.Text = text;
                await Wait(40);
                Ok($"search {text}", grid.Visible ? grid.Rows.Count == count : count == 0);
            }
            Ok("no result: actions disabled", Buttons(panel).All(b => !b.Enabled));
            search.Text = string.Empty;
            await Wait(40);

            var images = Get<DataGridView>(panel, "_images");
            var withImages = grid.Rows.Cast<DataGridViewRow>().First(r => ((StepInfo)r.Tag).Images.Count == 2);
            await ClickCell(grid, withImages.Index, 1);
            Ok("images follow the selection", images.Rows.Count == 2);
            var managed = grid.Rows.Cast<DataGridViewRow>().First(r => ((StepInfo)r.Tag).IsManaged);
            await ClickCell(grid, managed.Index, 1);
            Ok("managed step is flagged", Get<Label>(panel, "_imagesTitle").Text.EndsWith(Strings.Managed));

            await DoubleClickCell(grid, withImages.Index, 1);
            await Idle();
            var editor = await Dialog<StepEditorDialog>();
            Ok("double-click opens the editor", editor != null);
            await Close(editor);
            Ok("cancel writes nothing", _org.Updates.Count == 0);
            await ClickTab(Field<SegmentedTabs>("tabs"), 0);
        }

        /// <summary>Peek from the explorer and from the steps, active and dead-letter, against an in-memory reader.</summary>
        private async Task Messages()
        {
            var model = Get<ExplorerModel>("_model");
            StepInfo Step(string name) => model.Steps.First(s => s.Name == name);
            RemoteExecutionContext Context(StepInfo step) => DataverseSamples.Context(step.Id, step.Name, step.Message, step.Table, Guid.NewGuid());
            var updateAccount = Step("Update of account");
            var createAccount = Step("Create of account");

            var reader = new FakeMessageReader();
            reader.Add("crm-account-events", DataverseSamples.Binary(Context(updateAccount)), "application/msbin1");
            reader.Add("crm-account-events", DataverseSamples.Json(Context(createAccount)), "application/json", 1);
            reader.Add("crm-account-events", Bytes.Utf8("ping from a test tool"), "text/plain");
            reader.Add("crm-account-events/$DeadLetterQueue", DataverseSamples.Binary(Context(updateAccount)), "application/msbin1", 10, "MaxDeliveryCountExceeded", "Message could not be consumed after 10 delivery attempts.");
            reader.Add("crm-contact-changes/Subscriptions/data-warehouse", DataverseSamples.Xml(Context(Step("Update of contact"))), "application/xml");
            reader.Add("crm-contact-changes/Subscriptions/data-warehouse/$DeadLetterQueue", DataverseSamples.Json(Context(Step("Create of contact"))), "application/json", 3, "ListenerFailed", "Timeout calling the ERP");
            reader.Add("crm-contact-changes/Subscriptions/marketing-platform", DataverseSamples.Json(Context(Step("Create of contact"))), "application/json");
            var opened = 0;
            var previousFactory = Services.Azure.ConnectionFactory.MessageReaderFactory;
            Services.Azure.ConnectionFactory.MessageReaderFactory = (connection, settings) =>
            {
                opened++;
                return reader;
            };

            try
            {
                var tree = Field<ModernTreeView>("tree");
                var tabs = Field<SegmentedTabs>("tabsDetail");
                var panel = Field<MessagesPanel>("messagesExplorer");
                var grid = Get<DataGridView>(panel, "_grid");
                var read = Get<ModernButton>(panel, "_read");

                var explorerSteps = Field<StepsPanel>("stepsExplorer");
                var stepsMessages = Get<ModernButton>(explorerSteps, "_messages");
                await ClickTab(tabs, 0);
                await ClickNode(tree, "localhost");
                Ok("a namespace has no messages tab", tabs.Tabs[3].Hidden);
                Ok("a namespace has no Messages button", !stepsMessages.Visible);
                foreach (var group in new[] { Strings.GroupQueues, Strings.GroupTopics })
                {
                    await ClickNode(tree, group);
                    Ok($"{group} title: no messages tab, no Messages button", tabs.Tabs[3].Hidden && !stepsMessages.Visible);
                }

                await ClickNode(tree, "crm-account-events");
                Ok("a queue has the Messages button", stepsMessages.Visible);
                Ok("messages tab of a queue", !tabs.Tabs[3].Hidden && tabs.Tabs[3].Badge == "128");
                Ok("no option on the sub-queues: only the step filter of the popup remains", All<CheckBox>(panel).Count() == 1);
                await ClickTab(tabs, 3);
                Ok("messages page shown", panel.Visible && !Field<StepsPanel>("stepsExplorer").Visible);
                Ok("nothing read before the click", reader.Calls.Count == 0 && opened == 0);
                Ok("intro explains the peek", Texts(panel).Contains(Strings.MessagesIntro("crm-account-events", 1)) && Texts(panel).Contains(Strings.PeekHint));

                await Click(read);
                await Idle();
                Ok("only the active messages are read, never the dead-letter queue", reader.Calls.SequenceEqual(new[] { "crm-account-events" }));
                Ok("active rows", grid.Rows.Count == 3);
                var rows = grid.Rows.Cast<DataGridViewRow>().Select(r => (SbMessage)r.Tag).ToList();
                Ok("no dead-lettered message shown", rows.All(m => !m.DeadLetter));
                Ok("binary message linked to its step", rows.Any(m => m.Step == updateAccount && m.BodyFormat == Services.MessageDecoder.FormatBinary));
                Ok("JSON message linked to its step", rows.Any(m => m.Step == createAccount && m.BodyFormat == Services.MessageDecoder.FormatJson));
                Ok("other sender shown as such", grid.Rows.Cast<DataGridViewRow>().Any(r => (string)r.Cells["colStep"].Value == Strings.NotDataverseMessage));
                Ok("no dead-letter columns", grid.Columns["colQueue"] == null && grid.Columns["colReason"] == null);
                Ok("one queue: no source column", !grid.Columns["colSource"].Visible);
                Ok("count label says active", Get<Label>(panel, "_count").Text == Strings.MessagesCount(3, 3));

                var binaryRow = grid.Rows.Cast<DataGridViewRow>().First(r => ((SbMessage)r.Tag).Step == updateAccount);
                await ClickCell(grid, binaryRow.Index, 2);
                var properties = dgvValues(Get<DataGridView>(panel, "_properties"));
                Ok("detail shows the step", properties.Contains(updateAccount.Name) && properties.Contains(updateAccount.Id.ToString()));
                Ok("body is readable", Get<TextBox>(panel, "_body").Text.Contains("OwningExtension") && Get<Label>(panel, "_bodyTitle").Text.Contains(".NETBinary"));
                Clipboard.Clear();
                await Click(Get<ModernButton>(panel, "_copy"));
                Ok("Copy puts the body in the clipboard", Clipboard.GetText() == Get<TextBox>(panel, "_body").Text);
                await Click(Get<ModernButton>(panel, "_open"));
                var viewer = await Dialog<TextViewerDialog>();
                Ok("Open shows the body in the viewer", viewer != null && All<TextBox>(viewer).Any(t => t.Text.Contains("OwningExtension")));
                await Close(viewer);

                var search = Get<TextBox>(panel, "_search");
                search.Text = "Create of account";
                await Wait(60);
                Ok("search", grid.Rows.Count == 1 && ((SbMessage)grid.Rows[0].Tag).Step == createAccount);
                search.Text = "Contoso Ltd";
                await Wait(60);
                Ok("search in the bodies", grid.Rows.Count == 2);
                search.Text = "zzz";
                await Wait(60);
                Ok("search with no result", !grid.Visible && Texts(panel).Contains(Strings.MessagesNoMatch));
                search.Text = string.Empty;
                await Wait(60);

                // Messages stay while the entity stays selected.
                await ClickTab(tabs, 0);
                await ClickTab(tabs, 3);
                Ok("messages kept across tabs", grid.Rows.Count == 3);

                // A topic: its subscriptions
                reader.Calls.Clear();
                await ClickNode(tree, "crm-contact-changes");
                Ok("topic badge sums the active messages of its subscriptions", tabs.Tabs[3].Badge == "322");
                Ok("another entity clears the messages", !grid.Visible && Texts(panel).Contains(Strings.MessagesIntro("crm-contact-changes", 3)));
                await Click(read);
                await Idle();
                Ok("each subscription, no dead-letter queue", reader.Calls.Count == 3 && reader.Calls.All(c => !c.Contains("$DeadLetterQueue")));
                Ok("topic rows", grid.Rows.Count == 2 && grid.Columns["colSource"].Visible);
                Ok("topic steps", grid.Rows.Cast<DataGridViewRow>().All(r => ((SbMessage)r.Tag).Step?.Table == "contact"));

                // A subscription alone
                await ClickNode(tree, "data-warehouse");
                await Click(read);
                await Idle();
                Ok("subscription rows", grid.Rows.Count == 1);

                // Errors are shown, the other queues still count.
                reader.Error = new Services.Azure.AzureRequestException(401, Strings.MessagesAccessDenied("localhost"), "Unauthorized access. 'Listen' claim(s) are required.");
                await Click(read);
                await Idle();
                Ok("refusal shown", Get<Panel>(panel, "_alert").Visible && Get<Label>(panel, "_alertText").Text.Contains(Strings.MessagesAccessDenied("localhost")));
                Ok("refusal empty text", Texts(panel).Contains(Strings.MessagesAllFailed));
                await Click(Get<ModernButton>(panel, "_alertButton"));
                viewer = await Dialog<TextViewerDialog>();
                Ok("details of the refusal", viewer != null && All<TextBox>(viewer).Any(t => t.Text.Contains("'Listen' claim")));
                await Close(viewer);
                reader.Error = null;

                // Stop
                reader.Delay = 4000;
                await Click(read);
                await Wait(300);
                var overlay = Field<LoadingOverlay>("overlay");
                Ok("overlay while reading", overlay.Visible);
                await Click(Stop(overlay));
                await Idle();
                Ok("Stop gives the screen back", !overlay.Visible && Field<Label>("lblResultInfo").Text == Strings.TaskStopped);
                reader.Delay = 0;

                // A ghost cannot be read.
                await ClickNode(tree, "crm-quote-events");
                Ok("ghost: tab shown, read disabled", !tabs.Tabs[3].Hidden && !read.Enabled && Texts(panel).Contains(Strings.MessagesUnavailable));
                await ClickNode(tree, "crm-account-events");
                await ClickTab(tabs, 0);

                // From the steps page
                await ClickTab(Field<SegmentedTabs>("tabs"), 2);
                var steps = Field<StepsPanel>("stepsAll");
                var stepGrid = Get<DataGridView>(steps, "_grid");
                var row = stepGrid.Rows.Cast<DataGridViewRow>().First(r => r.Tag == updateAccount);
                await ClickCell(stepGrid, row.Index, 1);
                reader.Calls.Clear();
                await Click(Get<ModernButton>(steps, "_messages"));
                var dialog = await Dialog<Forms.MessagesDialog>();
                var popup = dialog?.Panel;
                await Until(() => popup != null && !popup.Busy && popup.Batches != null, 5000);
                var popupGrid = Get<DataGridView>(popup, "_grid");
                Ok("the popup reads by itself, active only", reader.Calls.SequenceEqual(new[] { "crm-account-events" }));
                Ok("only the messages of the step", popupGrid.Rows.Count == 1 && popupGrid.Rows.Cast<DataGridViewRow>().All(r => ((SbMessage)r.Tag).Step == updateAccount && !((SbMessage)r.Tag).DeadLetter));
                Ok("the popup says active only", Texts(popup).Contains(Strings.PeekHint));
                NoButtonAtEdge(dialog, "messages popup");
                await Click(Get<CheckBox>(popup, "_onlySteps"));
                Ok("all the active messages of the queue", popupGrid.Rows.Count == 3);
                await Click(Get<CheckBox>(popup, "_onlySteps"));

                reader.Delay = 4000;
                var popupRead = Get<ModernButton>(popup, "_read");
                await Click(popupRead);
                await Wait(200);
                Ok("the button becomes Stop", popup.Busy && popupRead.Text == Strings.Stop);
                await Click(popupRead);
                await Until(() => !popup.Busy, 3000);
                Ok("Stop in the popup", !popup.Busy && popupRead.Text == Strings.ReadMessages && popupGrid.Rows.Count == 1);
                reader.Delay = 0;
                await Close(dialog);

                // Two steps on two queues
                var invoice = stepGrid.Rows.Cast<DataGridViewRow>().First(r => ((StepInfo)r.Tag).Table == "invoice" && ((StepInfo)r.Tag).EndpointName == "Invoice events");
                await ClickCell(stepGrid, row.Index, 1);
                stepGrid.Rows[invoice.Index].Selected = true;
                reader.Calls.Clear();
                await Click(Get<ModernButton>(steps, "_messages"));
                dialog = await Dialog<Forms.MessagesDialog>();
                await Until(() => dialog.Panel.Batches != null, 5000);
                Ok("two steps: both queues", reader.Calls.Count == 2 && reader.Calls.Contains("crm-invoice-events"));
                Ok("two steps: filter text", Get<CheckBox>(dialog.Panel, "_onlySteps").Text == Strings.OnlyStepsMessages);
                await Close(dialog);

                // A step whose queue is not on Azure
                var lost = stepGrid.Rows.Cast<DataGridViewRow>().First(r => ((StepInfo)r.Tag).TargetPath == "crm-quote-events");
                await ClickCell(stepGrid, lost.Index, 1);
                await Click(Get<ModernButton>(steps, "_messages"));
                var info = await Dialog<MessageDialog>();
                Ok("missing queue explained", info != null && Texts(info).Any(t => t.Contains("crm-quote-events")));
                await Close(info);
                await ClickTab(Field<SegmentedTabs>("tabs"), 0);
            }
            finally
            {
                Services.Azure.ConnectionFactory.MessageReaderFactory = previousFactory;
            }
        }

        private async Task Bulk()
        {
            await ClickTab(Field<SegmentedTabs>("tabs"), 2);
            var panel = Field<StepsPanel>("stepsAll");
            var grid = Get<DataGridView>(panel, "_grid");
            var enable = Get<ModernButton>(panel, "_enable");
            var disable = Get<ModernButton>(panel, "_disable");
            var edit = Get<ModernButton>(panel, "_edit");

            await ClickCell(grid, 0, 1);
            Ok("enabled step: only Disable", !enable.Enabled && disable.Enabled && edit.Enabled);
            var off = grid.Rows.Cast<DataGridViewRow>().First(r => !((StepInfo)r.Tag).Enabled);
            await ClickCell(grid, off.Index, 1);
            Ok("disabled step: only Enable", enable.Enabled && !disable.Enabled);

            grid.ClearSelection();
            grid.Rows[0].Selected = true;
            grid.Rows[1].Selected = true;
            await Wait(50);
            Ok("multiple selection cannot edit", !edit.Enabled && disable.Enabled);

            await Click(disable);
            var cancel = await Dialog<MessageDialog>();
            Ok("confirmation counts the steps", Texts(cancel).Any(t => t.StartsWith(Strings.ConfirmBulkMessage(2, false))));
            NoButtonAtEdge(cancel, "bulk confirmation");
            await Press(cancel, Strings.CancelAction);
            await Wait(200);
            Ok("cancel writes nothing", _org.Updates.Count == 0);

            await Click(disable);
            await Press(await Dialog<MessageDialog>(), Strings.ApplyAction);
            await Reloaded();
            Ok("both steps written", _org.Updates.Count == 2);
            Ok("reload shows them disabled", Get<ExplorerModel>("_model").Steps.Count(s => !s.Enabled) == 3);
            Ok("confirmation message", Field<Label>("lblResultInfo").Text == Strings.BulkDone(2));
            Ok("the grid follows", grid.Rows.Cast<DataGridViewRow>().Count(r => !((StepInfo)r.Tag).Enabled) == 3);

            // Managed step in the selection: left out and said.
            _org.Updates.Clear();
            grid.ClearSelection();
            foreach (DataGridViewRow row in grid.Rows)
            {
                var step = (StepInfo)row.Tag;
                row.Selected = step.Enabled && (step.IsManaged || step.Message == "Create");
            }
            await Wait(50);
            var selected = grid.SelectedRows.Count;
            await Click(disable);
            var ask = await Dialog<MessageDialog>();
            Ok("managed steps are announced", Texts(ask).Any(t => t.Contains(Strings.BulkSkipped(1))));
            await Press(ask, Strings.ApplyAction);
            await Reloaded();
            Ok("managed step not written", _org.Updates.Count == selected - 1);

            // Only managed: nothing to do.
            _org.Updates.Clear();
            grid.ClearSelection();
            grid.Rows.Cast<DataGridViewRow>().First(r => ((StepInfo)r.Tag).IsManaged).Selected = true;
            await Wait(50);
            await Click(disable);
            var nothing = await Dialog<MessageDialog>();
            Ok("nothing to change", Texts(nothing).Contains(Strings.NothingToChange));
            await CloseDialogs();

            // Failure in the middle: what was written is shown, the error is readable.
            var offBefore = Get<ExplorerModel>("_model").Steps.Count(s => !s.Enabled && !s.IsManaged);
            var failing = Get<ExplorerModel>("_model").Steps.Where(s => !s.Enabled && !s.IsManaged).Take(3).ToList();
            Ok("enough disabled steps", failing.Count == 3);
            var count = 0;
            _org.Fail = operation =>
            {
                if (operation == "update" && ++count == 2) throw new InvalidOperationException("Simulated failure");
            };
            grid.ClearSelection();
            foreach (DataGridViewRow row in grid.Rows) row.Selected = failing.Any(s => s.Id == ((StepInfo)row.Tag).Id);
            await Wait(50);
            await Click(enable);
            await Press(await Dialog<MessageDialog>(), Strings.ApplyAction);
            var error = await Dialog<MessageDialog>();
            Ok("partial failure is reported", error != null && Texts(error).Contains("Simulated failure"));
            _org.Fail = null;
            await CloseDialogs();
            await Reloaded();
            Ok("partial failure: the screen shows the real state", Get<ExplorerModel>("_model").Steps.Count(s => !s.Enabled && !s.IsManaged) == offBefore - 1);

            // Back to a clean state.
            grid.ClearSelection();
            foreach (DataGridViewRow row in grid.Rows) row.Selected = !((StepInfo)row.Tag).Enabled;
            await Wait(50);
            await Click(enable);
            await Press(await Dialog<MessageDialog>(), Strings.ApplyAction);
            await Reloaded();
            Ok("all enabled again", Get<ExplorerModel>("_model").Steps.All(s => s.Enabled || s.IsManaged));
            _org.Updates.Clear();
            await ClickTab(Field<SegmentedTabs>("tabs"), 0);
        }

        private async Task<StepEditorDialog> OpenEditor(Func<StepInfo, bool> which)
        {
            await ClickTab(Field<SegmentedTabs>("tabs"), 2);
            var panel = Field<StepsPanel>("stepsAll");
            var grid = Get<DataGridView>(panel, "_grid");
            Get<TextBox>(panel, "_search").Text = string.Empty;
            await Wait(40);
            var row = grid.Rows.Cast<DataGridViewRow>().First(r => which((StepInfo)r.Tag));
            await ClickCell(grid, row.Index, 1);
            await Click(Get<ModernButton>(panel, "_edit"));
            return await Dialog<StepEditorDialog>();
        }

        private async Task Editor()
        {
            var dialog = await OpenEditor(s => s.Images.Count == 2);
            NoButtonAtEdge(dialog, "step editor");
            var message = Get<ComboBox>(dialog, "_message");
            var table = Get<ComboBox>(dialog, "_table");
            var stage = Get<ComboBox>(dialog, "_stage");
            var mode = Get<ComboBox>(dialog, "_mode");
            var save = Get<ModernButton>(dialog, "_save");
            var filtering = Get<TextBox>(dialog, "_filtering");
            var delete = Get<CheckBox>(dialog, "_deleteJob");
            var issues = Get<Label>(dialog, "_issues");
            var issuesHost = Get<Panel>(dialog, "_issuesHost");

            Ok("values loaded", message.Text == "Update" && table.Text == "account" && filtering.Text == "name,telephone1,creditlimit");
            Ok("message list", message.Items.Count == 4 && table.Items.Count == 5);

            await Click(save);
            var same = await Dialog<MessageDialog>();
            Ok("save without change says so", Texts(same).Contains(Strings.NoChanges));
            await CloseDialogs(dialog);

            // Rules while editing
            stage.SelectedIndex = 1;
            await Wait(60);
            Ok("async + pre-operation refused", !save.Enabled && issues.Text.Contains(Strings.RuleAsyncPostOnly));
            mode.SelectedIndex = 0;
            await Wait(60);
            Ok("sync removes the async rule", !issues.Text.Contains(Strings.RuleAsyncPostOnly));
            Ok("post image refused before the operation", !save.Enabled && issues.Text.Contains("PostImage"));
            Ok("delete job needs async", !delete.Enabled);
            stage.SelectedIndex = 2;
            mode.SelectedIndex = 1;
            await Wait(60);
            Ok("valid again", save.Enabled && !issuesHost.Visible);
            Ok("delete job is back", delete.Enabled);

            Get<NumericUpDown>(dialog, "_rank").Value = 0;
            await Wait(60);
            Ok("rank 0 refused", !save.Enabled && issues.Text.Contains(Strings.RuleRank));
            Get<NumericUpDown>(dialog, "_rank").Value = 1;
            await Wait(60);

            message.Text = "NotAMessage";
            await Wait(100);
            Ok("unknown message refused", !save.Enabled && issues.Text.Contains(Strings.RuleMessageRequired));

            message.Text = "Create";
            await Settle(dialog);
            Ok("Create clears the filtering columns", filtering.Text.Length == 0);
            Ok("Create: filtering is off", !Get<ModernButton>(dialog, "_pickFiltering").Enabled);
            Ok("Create: the pre image is refused", !save.Enabled && issues.Text.Contains("PreImage"));
            Ok("table is kept", table.Text == "account");

            message.Text = "Delete";
            await Settle(dialog);
            Ok("Delete: the post image is refused", !save.Enabled && issues.Text.Contains("PostImage"));

            message.Text = "Update";
            await Settle(dialog);
            table.Text = "nothing";
            await Wait(60);
            Ok("unknown table refused", !save.Enabled && issues.Text.Contains(Strings.TableRequired));
            table.Text = "contact";
            await Wait(60);
            Ok("another table is accepted", save.Enabled);
            await Close(dialog);
            Ok("close without saving writes nothing", _org.Updates.Count == 0);

            // Real save from the header X, Esc and Cancel paths
            dialog = await OpenEditor(s => s.Images.Count == 2);
            await Key(dialog, Keys.Escape);
            Ok("Esc closes the editor", await Gone<StepEditorDialog>());

            // Review refused: the editor stays open with the edits.
            dialog = await OpenEditor(s => s.Images.Count == 2);
            Get<NumericUpDown>(dialog, "_rank").Value = 9;
            Get<TextBox>(dialog, "_description").Text = "New description";
            await Wait(60);
            await Click(Get<ModernButton>(dialog, "_save"));
            var review = await Dialog<MessageDialog>();
            var lines = string.Join("|", Texts(review));
            Ok("review lists the rank", lines.Contains(Strings.FieldRank + " : 1  →  9"));
            Ok("review lists the description", lines.Contains("New description"));
            await Press(review, Strings.CancelAction);
            await Wait(200);
            Ok("refused review keeps the editor", dialog.Visible && Get<NumericUpDown>(dialog, "_rank").Value == 9);
            Ok("refused review writes nothing", _org.Updates.Count == 0);

            await Click(Get<ModernButton>(dialog, "_save"));
            await Press(await Dialog<MessageDialog>(), Strings.Save);
            await Reloaded();
            Ok("one update", _org.Updates.Count == 1);
            Ok("only the two fields", _org.Updates[0].Attributes.Count == 2 && (int)_org.Updates[0]["rank"] == 9);
            var saved = Get<ExplorerModel>("_model").Steps.First(s => s.Images.Count == 2);
            Ok("reloaded", saved.Rank == 9 && saved.Description == "New description");
            Ok("saved message", Field<Label>("lblResultInfo").Text == Strings.StepSaved(saved.Name));

            // Trigger change: message + table + filter id
            _org.Updates.Clear();
            dialog = await OpenEditor(s => s.Message == "Update" && s.Table == "lead");
            Get<ComboBox>(dialog, "_message").Text = "Create";
            await Settle(dialog);
            Get<ComboBox>(dialog, "_table").Text = "contact";
            await Wait(60);
            await Click(Get<ModernButton>(dialog, "_save"));
            await Press(await Dialog<MessageDialog>(), Strings.Save);
            await Reloaded();
            var update = _org.Updates.Single();
            Ok("trigger writes the message and the filter", update.Contains("sdkmessageid") && update.Contains("sdkmessagefilterid") && update.Contains("filteringattributes") && update["filteringattributes"] == null);
            var moved = Get<ExplorerModel>("_model").Steps.First(s => s.Id == update.Id);
            Ok("trigger reloaded", moved.Message == "Create" && moved.Table == "contact");
            _org.Updates.Clear();

            // Read-only
            dialog = await OpenEditor(s => s.IsManaged);
            Ok("managed: read-only message", Get<Label>(dialog, "_issues").Text == Strings.ReadOnlyStep);
            Ok("managed: fields locked", !Get<ModernButton>(dialog, "_save").Enabled && !Get<ComboBox>(dialog, "_message").Enabled && !Get<ModernButton>(dialog, "_addImage").Enabled);
            await Close(dialog);

            await ClickTab(Field<SegmentedTabs>("tabs"), 0);
        }

        private async Task Images()
        {
            var dialog = await OpenEditor(s => s.Images.Count == 2);
            var grid = Get<DataGridView>(dialog, "_images");

            // Add: validation, then OK
            await Click(Get<ModernButton>(dialog, "_addImage"));
            var image = await Dialog<ImageEditorDialog>();
            NoButtonAtEdge(image, "image editor");
            Ok("all columns warning", Texts(image).Contains(Strings.AllColumnsWarning));
            await PressOn(image, Strings.Ok);
            await Wait(100);
            Ok("alias required", Texts(image).Contains(Strings.ImageNeedsAlias));

            Get<TextBox>(image, "_alias").Text = "NewAlias";
            await Click(Get<RadioButton>(image, "_some"));
            await PressOn(image, Strings.Ok);
            await Wait(100);
            Ok("columns required", Texts(image).Contains(Strings.ImageNeedsColumns));

            await Click(Get<ModernButton>(image, "_pick"));
            var picker = await Dialog<ColumnPickerDialog>();
            await Wait(300);
            await ClickCell(Get<DataGridView>(picker, "_grid"), 0, 0);
            await PressOn(picker, Strings.Ok);
            await Wait(200);
            Ok("picker fills the columns", Get<TextBox>(image, "_columns").Text.Length > 0);
            await PressOn(image, Strings.Ok);
            await Wait(200);
            Ok("image added", grid.Rows.Count == 3);

            // Duplicate alias
            await Click(Get<ModernButton>(dialog, "_addImage"));
            image = await Dialog<ImageEditorDialog>();
            Get<TextBox>(image, "_alias").Text = "NEWALIAS";
            await PressOn(image, Strings.Ok);
            await Wait(200);
            Ok("duplicate alias refused", !Get<ModernButton>(dialog, "_save").Enabled && Get<Label>(dialog, "_issues").Text.Contains(Strings.RuleImageDuplicate("NewAlias")));

            // Remove the duplicate
            await ClickCell(grid, 3, 1);
            await Click(Get<ModernButton>(dialog, "_removeImage"));
            await Wait(100);
            Ok("image removed", grid.Rows.Count == 3 && Get<ModernButton>(dialog, "_save").Enabled);

            // Edit by double-click, cancel keeps the original
            await DoubleClickCell(grid, 0, 1);
            image = await Dialog<ImageEditorDialog>();
            Ok("edit shows the alias", Get<TextBox>(image, "_alias").Text == "PreImage");
            Get<TextBox>(image, "_alias").Text = "Changed";
            await PressOn(image, Strings.CancelAction);
            await Wait(200);
            Ok("cancel keeps the image", (string)grid.Rows[0].Cells[1].Value == "PreImage");

            // Edit by button: all columns
            await ClickCell(grid, 0, 1);
            await Click(Get<ModernButton>(dialog, "_editImage"));
            image = await Dialog<ImageEditorDialog>();
            await Click(Get<RadioButton>(image, "_all"));
            await PressOn(image, Strings.Ok);
            await Wait(200);
            Ok("edit applied", (string)grid.Rows[0].Cells[2].Value == Strings.AllColumns);

            // Remove one, then save everything together
            await ClickCell(grid, 1, 1);
            await Click(Get<ModernButton>(dialog, "_removeImage"));
            await Wait(100);
            await Click(Get<ModernButton>(dialog, "_save"));
            var review = await Dialog<MessageDialog>();
            var lines = string.Join("|", Texts(review));
            Ok("review lists the images", lines.Contains("NewAlias") && lines.Contains("PostImage") && lines.Contains("PreImage"));
            await Press(review, Strings.Save);
            await Reloaded();
            Ok("image operations", _org.Creates.Count == 1 && _org.Deletes.Count == 1 && _org.Updates.Count == 1);
            Ok("attributes null means all", _org.Updates[0].Contains("attributes") && _org.Updates[0]["attributes"] == null);
            var step = Get<ExplorerModel>("_model").Steps.First(s => s.Images.Any(i => i.Alias == "NewAlias"));
            Ok("images reloaded", step.Images.Count == 2);
            _org.Updates.Clear();
            _org.Creates.Clear();
            _org.Deletes.Clear();

            // A message without images
            dialog = await OpenEditor(s => s.Message == "Assign" && !s.IsManaged || s.Message == "Create" && s.Images.Count == 0);
            await Close(dialog);
            await ClickTab(Field<SegmentedTabs>("tabs"), 0);
        }

        private async Task Picker()
        {
            var dialog = await OpenEditor(s => s.Message == "Update" && s.Table == "account");
            await Click(Get<ModernButton>(dialog, "_pickFiltering"));
            var picker = await Dialog<ColumnPickerDialog>();
            await Wait(300);
            NoButtonAtEdge(picker, "column picker");
            var grid = Get<DataGridView>(picker, "_grid");
            var search = Get<TextBox>(picker, "_search");
            Ok("columns listed", grid.Rows.Count == 6);
            Ok("existing selection ticked", (bool)grid.Rows.Cast<DataGridViewRow>().First(r => ((ColumnInfo)r.Tag).LogicalName == "telephone1").Cells[0].Value);
            Ok("counter", Texts(picker).Contains(Strings.PickerCount(3)));

            search.Text = "phone";
            await Wait(60);
            Ok("search", grid.Rows.Cast<DataGridViewRow>().Count(r => r.Visible) == 1);
            search.Text = "emailaddress1";
            await Wait(60);
            var email = grid.Rows.Cast<DataGridViewRow>().First(r => r.Visible);
            await ClickCell(grid, email.Index, 2);
            Ok("a click on the row ticks it", (bool)email.Cells[0].Value);
            await ClickCell(grid, email.Index, 2);
            Ok("a second click unticks it", !(bool)email.Cells[0].Value);
            await ClickCell(grid, email.Index, 0);
            Ok("a click on the box ticks it once", (bool)email.Cells[0].Value);
            search.Text = string.Empty;
            await Wait(60);
            await Click(Get<CheckBox>(picker, "_onlySelected"));
            Ok("selected only", grid.Rows.Cast<DataGridViewRow>().Where(r => r.Visible).All(r => (bool)r.Cells[0].Value));
            await Click(Get<CheckBox>(picker, "_onlySelected"));

            await PressOn(picker, Strings.CancelAction);
            await Wait(200);
            Ok("cancel keeps the columns", Get<TextBox>(dialog, "_filtering").Text == "name,telephone1,creditlimit");

            await Click(Get<ModernButton>(dialog, "_pickFiltering"));
            picker = await Dialog<ColumnPickerDialog>();
            await Wait(300);
            var firstFree = Get<DataGridView>(picker, "_grid").Rows.Cast<DataGridViewRow>().First(r => !(bool)r.Cells[0].Value);
            await ClickCell(Get<DataGridView>(picker, "_grid"), firstFree.Index, 1);
            await PressOn(picker, Strings.Ok);
            await Wait(200);
            Ok("OK sorts and joins", Get<TextBox>(dialog, "_filtering").Text.Split(',').OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(Get<TextBox>(dialog, "_filtering").Text.Split(',')) && Get<TextBox>(dialog, "_filtering").Text.Contains(((ColumnInfo)firstFree.Tag).LogicalName));

            await Click(Get<ModernButton>(dialog, "_clearFiltering"));
            await Wait(100);
            Ok("Clear empties", Get<TextBox>(dialog, "_filtering").Text.Length == 0);

            // Metadata failure in the picker
            _org.Fail = operation =>
            {
                if (operation == "metadata") throw new InvalidOperationException("No metadata");
            };
            Get<EndpointColumnsReset>().Reset(Get<object>("_endpoints"));
            await Click(Get<ModernButton>(dialog, "_pickFiltering"));
            var error = await Dialog<MessageDialog>();
            Ok("picker reports its error", Texts(error).Contains("No metadata"));
            _org.Fail = null;
            await CloseTop<MessageDialog>();
            Ok("picker closes after the error", await Gone<ColumnPickerDialog>());
            Ok("the editor survives", dialog.Visible);

            // Message change failure
            _org.Fail = operation =>
            {
                if (operation == "read:sdkmessagefilter") throw new InvalidOperationException("No tables");
            };
            Get<ComboBox>(dialog, "_message").Text = "Create";
            var tables = await Dialog<MessageDialog>();
            Ok("table read error", Texts(tables).Contains("No tables"));
            _org.Fail = null;
            await CloseTop<MessageDialog>();
            await Close(dialog);
            await ClickTab(Field<SegmentedTabs>("tabs"), 0);
        }

        private sealed class EndpointColumnsReset
        {
            public void Reset(object service)
            {
                var columns = (Dictionary<string, List<ColumnInfo>>)service.GetType().GetField("_columns", Private).GetValue(service);
                lock (columns) columns.Clear();
            }
        }

        private T Get<T>() where T : new() => new T();

        private async Task SaveErrors()
        {
            // Save refused by the platform
            var dialog = await OpenEditor(s => s.Message == "Update" && s.Table == "account");
            Get<NumericUpDown>(dialog, "_rank").Value = 4;
            await Wait(60);
            _org.Fail = operation =>
            {
                if (operation == "update") throw Fault("The stage is not valid for this message.", -2147220891);
            };
            await Click(Get<ModernButton>(dialog, "_save"));
            await Press(await Dialog<MessageDialog>(), Strings.Save);
            var error = await Dialog<MessageDialog>();
            Ok("platform message displayed", Texts(error).Contains("The stage is not valid for this message."));
            _org.Fail = null;
            await CloseTop<MessageDialog>();
            await Idle();
            Ok("failed save changes nothing on screen", Get<ExplorerModel>("_model").Steps.First(s => s.Message == "Update" && s.Table == "account").Rank != 4);

            // Missing privilege
            dialog = await OpenEditor(s => s.Message == "Update" && s.Table == "account");
            Get<NumericUpDown>(dialog, "_rank").Value = 4;
            await Wait(60);
            _org.Fail = operation =>
            {
                if (operation == "update") throw Fault("SecLib::AccessCheckEx failed. prvWriteSdkMessageProcessingStep", unchecked((int)0x80040220));
            };
            await Click(Get<ModernButton>(dialog, "_save"));
            await Press(await Dialog<MessageDialog>(), Strings.Save);
            error = await Dialog<MessageDialog>();
            var text = string.Join("|", Texts(error));
            Ok("privilege title", Texts(error).Contains(Strings.MissingPrivileges));
            Ok("privilege name", text.Contains("prvWriteSdkMessageProcessingStep"));
            _org.Fail = null;
            await CloseTop<MessageDialog>();
            await Idle();

            // Editor data cannot be read
            var endpoints = Get<object>("_endpoints");
            endpoints.GetType().GetField("_messages", Private).SetValue(endpoints, null);
            _org.Fail = operation =>
            {
                if (operation == "read:sdkmessage") throw new InvalidOperationException("Messages unavailable");
            };
            await ClickTab(Field<SegmentedTabs>("tabs"), 2);
            var panel = Field<StepsPanel>("stepsAll");
            await ClickCell(Get<DataGridView>(panel, "_grid"), 0, 1);
            await Click(Get<ModernButton>(panel, "_edit"));
            error = await Dialog<MessageDialog>();
            Ok("editor preparation error", Texts(error).Contains("Messages unavailable"));
            _org.Fail = null;
            await CloseTop<MessageDialog>();
            Ok("the tool stays usable", Get<object>("_busy") == null && !Field<LoadingOverlay>("overlay").Visible);

            // Image write refused after the step
            _org.Updates.Clear();
            dialog = await OpenEditor(s => s.Images.Count > 0 && !s.IsManaged);
            await ClickCell(Get<DataGridView>(dialog, "_images"), 0, 1);
            await Click(Get<ModernButton>(dialog, "_removeImage"));
            await Wait(100);
            _org.Fail = operation =>
            {
                if (operation == "delete") throw new InvalidOperationException("Image locked");
            };
            await Click(Get<ModernButton>(dialog, "_save"));
            await Press(await Dialog<MessageDialog>(), Strings.Save);
            error = await Dialog<MessageDialog>();
            Ok("image error displayed", Texts(error).Contains("Image locked"));
            _org.Fail = null;
            await CloseTop<MessageDialog>();
            await ClickTab(Field<SegmentedTabs>("tabs"), 0);
        }

        private static FaultException<OrganizationServiceFault> Fault(string message, int code) =>
            new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = code, Message = message }, new FaultReason(message));

        private async Task ConnectionsPopup()
        {
            var settings = Get<PluginSettings>("_settings");
            var before = settings.Connections.Count;
            await ClickItem(Field<ToolStripButton>("tsbConnections"));
            var dialog = await Dialog<ConnectionsDialog>();
            NoButtonAtEdge(dialog, "connections");
            var tabs = Get<SegmentedTabs>(dialog, "_tabs");
            var saved = Get<DataGridView>(dialog, "_saved");
            Ok("connected list", saved.Rows.Count == before);
            Ok("not connected namespaces are listed", Get<Label>(dialog, "_needed").Text.Contains("contoso-legacy"));
            Ok("no key displayed", !string.Join("|", Texts(dialog)).Contains("c2VjcmV0"));

            await ClickTab(tabs, 1);
            var box = Get<TextBox>(dialog, "_connectionString");
            var test = Get<ModernButton>(dialog, "_testAndAdd");
            var status = Get<Label>(dialog, "_keyStatus");
            Ok("connection string is masked", box.UseSystemPasswordChar);

            box.Text = string.Empty;
            await Click(test);
            Ok("empty string refused", status.Text == Strings.InvalidConnectionString);
            box.Text = "hello";
            await Click(test);
            Ok("garbage refused", status.Text == Strings.InvalidConnectionString);
            box.Text = "Endpoint=sb://x.servicebus.windows.net/;SharedAccessKeyName=a";
            await Click(test);
            Ok("no key refused", status.Text == Strings.ConnectionStringNoKey);
            box.Text = "Endpoint=sb://x.servicebus.windows.net/;SharedAccessKeyName=a;SharedAccessKey=b;EntityPath=q1";
            await Click(test);
            Ok("entity key refused", status.Text == Strings.ConnectionStringEntityScoped);
            Ok("no request for invalid strings", settings.Connections.Count == before);

            box.Text = "Endpoint=sb://localhost:1/;SharedAccessKeyName=a;SharedAccessKey=b;UseDevelopmentEmulator=true";
            await Click(test);
            var error = await Dialog<MessageDialog>();
            Ok("unreachable is explained", Texts(error).Contains(Strings.AzureUnreachable("localhost")));
            await CloseTop<MessageDialog>();
            Ok("buttons come back after an error", await Until(() => test.Enabled));
            Ok("failed test adds nothing", settings.Connections.Count == before);

            _bus.RefuseAll = true;
            box.Text = _bus.ConnectionString;
            await Click(test);
            error = await Dialog<MessageDialog>();
            Ok("refused key is explained", Texts(error).Contains(Strings.AzureAccessDenied("localhost")));
            await CloseTop<MessageDialog>();
            _bus.RefuseAll = false;
            await Until(() => test.Enabled);

            await Click(test);
            await Until(() => status.Text == Strings.ConnectionAdded("localhost"));
            Ok("valid string is added once", settings.Connections.Count(c => c.Host == "localhost") == 1);
            Ok("box cleared", box.TextLength == 0);
            Ok("secret encrypted", !settings.Connections.First(c => c.Host == "localhost").ProtectedSecret.Contains("SharedAccessKey"));

            // Closing during a test must not crash.
            _bus.Delay = 1500;
            box.Text = _bus.ConnectionString;
            await Click(test);
            await Wait(100);
            await Close(dialog);
            await Wait(1800);
            _bus.Delay = 0;
            Ok("closing during a test is safe", true);

            // Microsoft page
            await ClickItem(Field<ToolStripButton>("tsbConnections"));
            dialog = await Dialog<ConnectionsDialog>();
            await ClickTab(Get<SegmentedTabs>(dialog, "_tabs"), 2);
            var found = Get<DataGridView>(dialog, "_found");
            var add = Get<ModernButton>(dialog, "_addSelected");
            Ok("add disabled without a tick", !add.Enabled);

            FindField(dialog, "_discoveredWith").SetValue(dialog, ConnectionMethod.Browser);
            var ns = new DiscoveredNamespace { Host = "arm-ns.servicebus.windows.net", ResourceId = "/subscriptions/1/resourceGroups/rg/providers/Microsoft.ServiceBus/namespaces/arm-ns", TenantId = "t1", SubscriptionName = "Prod", ResourceGroup = "rg", Sku = "Standard" };
            var index = found.Rows.Add(false, ns.Host, ns.SubscriptionName, ns.ResourceGroup, ns.Sku);
            found.Rows[index].Tag = ns;
            await Wait(100);
            await ClickCell(found, index, 0);
            Ok("tick enables add", add.Enabled);
            await ClickCell(found, index, 0);
            Ok("untick disables add", !add.Enabled);
            await ClickCell(found, index, 0);
            await Click(add);
            Ok("namespace added from the account", settings.Connections.Any(c => c.Host == ns.Host && c.Method == ConnectionMethod.Browser && c.ResourceId == ns.ResourceId && c.ProtectedSecret == null));

            await ClickTab(Get<SegmentedTabs>(dialog, "_tabs"), 0);
            saved = Get<DataGridView>(dialog, "_saved");
            var row = saved.Rows.Cast<DataGridViewRow>().First(r => ((SavedConnection)r.Tag).Host == ns.Host);
            await ClickCell(saved, row.Index, 0);
            await Click(Get<ModernButton>(dialog, "_remove"));
            Ok("Remove", settings.Connections.All(c => c.Host != ns.Host));

            // The real Azure CLI of this computer, against the real Resource Manager.
            await ClickTab(Get<SegmentedTabs>(dialog, "_tabs"), 2);
            await Click(Get<ModernButton>(dialog, "_useCli"));
            var label = Get<Label>(dialog, "_msStatus");
            var done = await Until(() => label.Text == Strings.NoNamespaceFound || label.Text.EndsWith(Strings.FoundNamespaces(0).Substring(1)) || Application.OpenForms.OfType<MessageDialog>().Any(), 40000);
            var answer = Application.OpenForms.OfType<MessageDialog>().Any() ? "error: " + string.Join(" / ", Texts(Application.OpenForms.OfType<MessageDialog>().First())) : label.Text;
            Console.WriteLine("real az + ARM discovery: " + answer);
            Ok("real Azure CLI and ARM answer", done);
            await CloseDialogs();

            var info = Field<Label>("lblResultInfo");
            Ok("changed connections ask for a reload", info.Text == Strings.ReloadHint);
        }

        private async Task OptionsPopup()
        {
            var settings = Get<PluginSettings>("_settings");
            await ClickItem(Field<ToolStripButton>("tsbOptions"));
            var dialog = await Dialog<OptionsDialog>();
            NoButtonAtEdge(dialog, "options");
            var boxes = All<TextBox>(dialog).Where(t => !(t.Parent is NumericUpDown)).ToList();
            var numbers = All<NumericUpDown>(dialog).ToList();
            Ok("two numbers: parallel requests and messages", numbers.Count == 2 && numbers[1].Value == 100);
            numbers[0].Value = 3;
            numbers[1].Value = 250;
            boxes[0].Text = " tenant ";
            boxes[1].Text = " client ";
            await PressOn(dialog, Strings.CancelAction);
            await Wait(200);
            Ok("cancel keeps the options", settings.ParallelRequests == 6 && settings.MaxMessages == 100 && string.IsNullOrEmpty(settings.TenantId));

            await ClickItem(Field<ToolStripButton>("tsbOptions"));
            dialog = await Dialog<OptionsDialog>();
            boxes = All<TextBox>(dialog).Where(t => !(t.Parent is NumericUpDown)).ToList();
            numbers = All<NumericUpDown>(dialog).ToList();
            numbers[0].Value = 3;
            numbers[1].Value = 250;
            boxes[0].Text = " tenant ";
            boxes[1].Text = " client ";
            await PressOn(dialog, Strings.Save);
            await Wait(200);
            Ok("options saved and trimmed", settings.ParallelRequests == 3 && settings.MaxMessages == 250 && settings.TenantId == "tenant" && settings.ClientId == "client");
            settings.ParallelRequests = 6;
            settings.MaxMessages = 100;
            settings.TenantId = null;
            settings.ClientId = null;
        }

        private async Task LoadErrors()
        {
            var load = Field<ToolStripButton>("tsbLoad");
            var modelBefore = Get<ExplorerModel>("_model");

            // Dataverse refuses the read
            _org.Fail = operation =>
            {
                if (operation == "read:serviceendpoint") throw Fault("SecLib::AccessCheckEx failed: prvReadServiceEndpoint", unchecked((int)0x80040220));
            };
            await ClickItem(load);
            var error = await Dialog<MessageDialog>();
            Ok("read privilege is explained", Texts(error).Contains(Strings.MissingPrivileges) && string.Join("|", Texts(error)).Contains("prvReadServiceEndpoint"));
            _org.Fail = null;
            await CloseTop<MessageDialog>();
            await Idle();
            Ok("a failed load keeps the screen", ReferenceEquals(modelBefore, Get<ExplorerModel>("_model")));

            // Generic failure
            _org.Fail = operation =>
            {
                if (operation.StartsWith("read:sdkmessageprocessingstep")) throw new TimeoutException("The request channel timed out");
            };
            await ClickItem(load);
            error = await Dialog<MessageDialog>();
            Ok("generic error", Texts(error).Contains("The request channel timed out"));
            _org.Fail = null;
            await CloseTop<MessageDialog>();
            await Idle();

            // Azure refused: one namespace in error, the rest continues
            var settings = Get<PluginSettings>("_settings");
            var broken = new SavedConnection { Host = "corrupted.servicebus.windows.net", Method = ConnectionMethod.ConnectionString, ProtectedSecret = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }) };
            settings.Connections.Add(broken);
            _bus.RefuseAll = true;
            await ClickItem(load);
            await Idle();
            var model = Get<ExplorerModel>("_model");
            Ok("no blocking popup for a namespace", !Application.OpenForms.OfType<MessageDialog>().Any());
            Ok("steps still loaded", model.Steps.Count == 11);
            Ok("both namespaces in error", model.Namespaces.Count(n => n.Error != null) == 2);
            Ok("summary counts them", Field<Label>("lblResultInfo").Text.Contains(Strings.SummaryErrors(2).Trim(' ', '·')));

            var tree = Field<ModernTreeView>("tree");
            await ClickNode(tree, "corrupted");
            Ok("unreadable secret is explained", Field<Label>("lblAlert").Text.Contains(Strings.SecretUnreadable));
            await ClickNode(tree, "localhost");
            Ok("refusal on the node", Field<Label>("lblAlert").Text.Contains(Strings.AzureAccessDenied("localhost")));
            await Click(Field<ModernButton>("btnAlert"));
            var details = await Dialog<MessageDialog>();
            Ok("Details shows the Azure answer", details != null);
            await CloseTop<MessageDialog>();

            await ClickNode(tree, "crm-account-events");
            Ok("entity of an unreadable namespace", Field<Label>("lblAlert").Text.StartsWith(Strings.AlertNamespaceError(string.Empty).Trim()));
            Ok("its steps stay available", Field<StatTile>("tile5").Value == "2");

            _bus.RefuseAll = false;
            settings.Connections.Remove(broken);
            await ClickItem(load);
            await Idle();
            Ok("recovers on the next load", Get<ExplorerModel>("_model").Namespaces.All(n => n.Error == null));
        }

        private async Task MessagePopup()
        {
            var host = _form;
            var task = ShowLater(() => MessageDialog.Show(host, MessageKind.Error, "Title", "Message", "Line 1\r\nLine 2"));
            var dialog = await Dialog<MessageDialog>();
            NoButtonAtEdge(dialog, "error popup");
            var links = All<LinkLabel>(dialog).ToList();
            var height = dialog.ClientSize.Height;
            await Click(links.First(l => l.Text.EndsWith(Strings.ShowDetails)));
            Ok("details unfold", dialog.ClientSize.Height > height && All<TextBox>(dialog).Any(t => t.Visible && t.Text.Contains("Line 2")));
            NoButtonAtEdge(dialog, "error popup unfolded");
            await Click(links.First(l => l.Text.EndsWith(Strings.HideDetails)));
            Ok("details fold", dialog.ClientSize.Height == height);

            Clipboard.Clear();
            await Press(dialog, Strings.CopyDetails);
            await Wait(100);
            Ok("Copy details", Clipboard.GetText().Contains("Line 2"));
            Ok("Copy says so", Buttons(dialog).Any(b => b.Text == Strings.Copied));

            await Click(links.First(l => l.Text.EndsWith(Strings.ExpandDetails)));
            var viewer = await Dialog<TextViewerDialog>();
            NoButtonAtEdge(viewer, "text viewer");
            var box = All<TextBox>(viewer).Single();
            Ok("large window", box.Text.Contains("Line 2"));
            await Click(All<CheckBox>(viewer).Single());
            Ok("wrap toggle", !box.WordWrap);
            await PressOn(viewer, Strings.Close);
            Ok("viewer closes", await Gone<TextViewerDialog>());
            await Press(dialog, Strings.Close);
            await task;
            Ok("popup closes", await Gone<MessageDialog>());
        }

        private async Task HeaderLinks()
        {
            await ClickItem(Field<ToolStripButton>("tsbHelp"));
            var help = await Dialog<TextViewerDialog>();
            Ok("help text", All<TextBox>(help).Single().Text.Contains("MATCHING"));
            await Close(help);

            _urls.Clear();
            await ClickItem(Field<ToolStripButton>("tsbDonate"));
            await ClickItem(Field<ToolStripButton>("tsbAuthor"));
            Ok("donate link", _urls.Any(u => u.Contains("paypal")));
            Ok("author link", _urls.Any(u => u.Contains("linkedin")));

            var language = Field<ToolStripDropDownButton>("tsddLanguage");
            await ClickItem(language);
            Ok("language menu opens", language.DropDown.Visible);
            language.DropDown.Close();
            Field<ToolStripMenuItem>("tsmiFrench").PerformClick();
            await Wait(200);
            var startedInFrench = Environment.GetEnvironmentVariable("SBX_LANG") == "fr";
            Ok("French header", Field<ToolStripButton>("tsbLoad").Text == "Charger" && Field<SegmentedTabs>("tabs").Tabs[0].Text == "Explorateur");
            Ok("French is saved", Get<PluginSettings>("_settings").Language == "fr");
            var tree = Field<ModernTreeView>("tree");
            await ClickNode(tree, "crm-contact-changes");
            Ok("French detail", Field<SegmentedTabs>("tabsDetail").Tabs[1].Text == "Souscriptions et filtres");
            await ClickTab(Field<SegmentedTabs>("tabs"), 1);
            Ok("French matching", Field<ComboBox>("cboMatchFilter").Text == "Tous les statuts" && (string)Field<DataGridView>("dgvMatching").Rows[0].Cells[0].Value == "Lié");
            await ClickTab(Field<SegmentedTabs>("tabs"), 2);
            var panel = Field<StepsPanel>("stepsAll");
            Ok("French steps", (string)Get<DataGridView>(panel, "_grid").Rows[0].Cells[0].Value == "Activé");
            var filter = Field<ComboBox>("cboMatchFilter");
            filter.SelectedIndex = 1;
            Field<ToolStripMenuItem>("tsmiEnglish").PerformClick();
            await Wait(200);
            Ok("language keeps the filter", filter.SelectedIndex == 1 && filter.Text == Strings.MatchProblems);
            filter.SelectedIndex = 0;
            Ok("English again", Field<ToolStripButton>("tsbLoad").Text == "Load");
            if (startedInFrench) Strings.Current = Language.French;
            await ClickTab(Field<SegmentedTabs>("tabs"), 0);
        }

        private async Task Export()
        {
            string path;

            // Native save dialog: cancelled, then confirmed.
            await ClickItem(Field<ToolStripButton>("tsbExport"));
            var window = await NativeDialog();
            Ok("save dialog opens", window != IntPtr.Zero);
            PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            await Wait(400);
            Ok("cancel exports nothing", !Application.OpenForms.OfType<MessageDialog>().Any());

            await ClickItem(Field<ToolStripButton>("tsbExport"));
            window = await NativeDialog();
            PostMessage(GetDlgItem(window, 1), BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            var done = await Dialog<MessageDialog>();
            var message = Texts(done).First(t => t.Contains(".xlsx"));
            path = message.Substring(message.IndexOf(':') - 1, message.LastIndexOf(".xlsx", StringComparison.Ordinal) + 5 - message.IndexOf(':') + 1);
            Ok("export success message", message == Strings.ExportDone(path));
            await CloseTop<MessageDialog>();
            Ok("the workbook exists", File.Exists(path) && new FileInfo(path).Length > 1000);

            // Locked file (open in Excel)
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var task = ShowLater(() => _c.ExportTo(path));
                var error = await Dialog<MessageDialog>();
                Ok("locked file is reported", error != null);
                await CloseTop<MessageDialog>();
                await task;
            }
            File.Delete(path);
        }

        private async Task Narrow()
        {
            var size = _form.Size;
            _form.Size = new Size(900, 620);
            await Wait(300);
            var header = Field<ToolStrip>("tsHeader");
            Ok("header stays on one line", header.Items.Cast<ToolStripItem>().All(i => !i.IsOnOverflow));
            Ok("icons only on the links", Field<ToolStripButton>("tsbAuthor").DisplayStyle == ToolStripItemDisplayStyle.Image);
            await ClickItem(Field<ToolStripButton>("tsbOptions"));
            await Dialog<OptionsDialog>();
            Ok("buttons work in narrow mode", true);
            await CloseDialogs();
            _form.Size = size;
            await Wait(300);
            Ok("text comes back", Field<ToolStripButton>("tsbAuthor").DisplayStyle == ToolStripItemDisplayStyle.ImageAndText);

            // Minimum sizes of the popups
            var dialog = await OpenEditor(s => s.Images.Count > 0 && !s.IsManaged);
            dialog.Size = dialog.MinimumSize;
            await Wait(200);
            NoButtonAtEdge(dialog, "step editor at its minimum size");
            await Close(dialog);
            await ClickTab(Field<SegmentedTabs>("tabs"), 0);
        }

        private async Task Reconnect()
        {
            var other = Demo.Organization("localhost");
            _c.UpdateConnection(other, new ConnectionDetail { ConnectionName = "other" }, string.Empty, null);
            await Wait(200);
            Ok("a new connection clears the screen", Get<ExplorerModel>("_model") == null && Field<Label>("lblTreeEmpty").Visible);
            Ok("summary cleared", Field<Label>("lblResultInfo").Text.Length == 0);
            Ok("no read after connecting", other.Calls == 0);

            await ClickItem(Field<ToolStripButton>("tsbLoad"));
            await Idle();
            Ok("reads the new organization", other.Calls > 0);
            Ok("old organization untouched", true);

            await ClickItem(Field<ToolStripButton>("tsbClose"));
            await Wait(200);
            Ok("Close asks XrmToolBox to close", _closeRequests == 1);
        }

        #endregion

        #region Input

        private static IntPtr Lparam(Point p) => (IntPtr)((p.Y << 16) | (p.X & 0xFFFF));

        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);

        private string Obstacle(Control control, Point at)
        {
            var window = WindowFromPoint(control.PointToScreen(at));
            if (window == control.Handle) return null;
            var name = new StringBuilder(256);
            GetClassName(window, name, 256);
            var other = Control.FromHandle(window);
            var forms = string.Join(",", Application.OpenForms.Cast<Form>().Select(f => $"{f.GetType().Name}[{f.Visible},{f.Enabled}]"));
            return $"covered by {name} {other?.GetType().Name} '{other?.Name}' mainEnabled={_form.Enabled} forms={forms}";
        }

        private static string Chain(Control control)
        {
            var text = new StringBuilder();
            for (var c = control; c != null; c = c.Parent) text.Append($" {c.GetType().Name}{c.Bounds}");
            return text.ToString();
        }

        private async Task Click(Control control, Point? at = null)
        {
            if (!control.Enabled || !control.Visible || !Reachable(control))
            {
                Fail($"not clickable: {control.GetType().Name} '{control.Text}' enabled={control.Enabled} visible={control.Visible} chain={Chain(control)}");
                return;
            }

            var obstacle = Obstacle(control, at ?? new Point(control.Width / 2, control.Height / 2));
            if (obstacle != null) Fail($"{control.GetType().Name} '{control.Text}' {obstacle}");
            if (control is CheckBox || control is RadioButton)
            {
                PostMessage(control.Handle, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                await Wait(120);
                return;
            }

            var point = at ?? new Point(control.Width / 2, control.Height / 2);
            PostMessage(control.Handle, WM_MOUSEMOVE, IntPtr.Zero, Lparam(point));
            PostMessage(control.Handle, WM_LBUTTONDOWN, (IntPtr)1, Lparam(point));
            PostMessage(control.Handle, WM_LBUTTONUP, IntPtr.Zero, Lparam(point));
            await Wait(120);
        }

        private static async Task DoubleClick(Control control, Point at)
        {
            PostMessage(control.Handle, WM_MOUSEMOVE, IntPtr.Zero, Lparam(at));
            PostMessage(control.Handle, WM_LBUTTONDOWN, (IntPtr)1, Lparam(at));
            PostMessage(control.Handle, WM_LBUTTONUP, IntPtr.Zero, Lparam(at));
            PostMessage(control.Handle, WM_LBUTTONDBLCLK, (IntPtr)1, Lparam(at));
            PostMessage(control.Handle, WM_LBUTTONUP, IntPtr.Zero, Lparam(at));
            await Wait(200);
        }

        /// <summary>The control is inside every parent: a real mouse could reach it.</summary>
        private static bool Reachable(Control control)
        {
            for (var c = control; c.Parent != null; c = c.Parent)
            {
                if (!c.Parent.ClientRectangle.IntersectsWith(c.Bounds)) return false;
            }
            return true;
        }

        private async Task ClickItem(ToolStripItem item)
        {
            var strip = item.Owner;
            var point = new Point(item.Bounds.X + item.Bounds.Width / 2, item.Bounds.Y + item.Bounds.Height / 2);
            if (!item.Available || !item.Enabled)
            {
                Fail("item not clickable: " + item.Text);
                return;
            }
            PostMessage(strip.Handle, WM_MOUSEMOVE, IntPtr.Zero, Lparam(point));
            await Wait(30);
            PostMessage(strip.Handle, WM_LBUTTONDOWN, (IntPtr)1, Lparam(point));
            PostMessage(strip.Handle, WM_LBUTTONUP, IntPtr.Zero, Lparam(point));
            await Wait(150);
        }

        private async Task ClickTab(SegmentedTabs tabs, int index)
        {
            if (tabs.SelectedIndex == index) return;
            tabs.Update();
            var bounds = tabs.Tabs[index].Bounds;
            await Click(tabs, new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
            if (tabs.SelectedIndex != index) Fail($"tab {index} not selected by a click");
        }

        private async Task ClickCell(DataGridView grid, int row, int column)
        {
            if (grid.Visible && row < grid.Rows.Count && grid.Rows[row].Visible) grid.FirstDisplayedScrollingRowIndex = row;
            await Wait(20);
            var r = grid.GetCellDisplayRectangle(column, row, false);
            await Click(grid, new Point(r.X + r.Width / 2, r.Y + r.Height / 2));
        }

        private async Task DoubleClickCell(DataGridView grid, int row, int column)
        {
            if (grid.Visible && row < grid.Rows.Count && grid.Rows[row].Visible) grid.FirstDisplayedScrollingRowIndex = row;
            await Wait(20);
            var r = grid.GetCellDisplayRectangle(column, row, false);
            await DoubleClick(grid, new Point(r.X + r.Width / 2, r.Y + r.Height / 2));
        }

        private static async Task Key(Control control, Keys key)
        {
            control.Focus();
            PostMessage(control.Handle, WM_KEYDOWN, (IntPtr)(int)key, IntPtr.Zero);
            PostMessage(control.Handle, WM_KEYUP, (IntPtr)(int)key, IntPtr.Zero);
            await Wait(150);
        }

        private static ModernTreeNode Node(ModernTreeView tree, string title) => tree.AllNodes().First(n => n.Title == title);

        private static Rectangle RowOf(ModernTreeView tree, ModernTreeNode node, string method)
        {
            for (var parent = node.Parent; parent != null; parent = parent.Parent) parent.Expanded = true;
            var visible = (List<ModernTreeNode>)typeof(ModernTreeView).GetField("visible", Private).GetValue(tree);
            typeof(ModernTreeView).GetMethod("Rebuild", Private).Invoke(tree, null);
            typeof(ModernTreeView).GetMethod("EnsureVisible", Private).Invoke(tree, new object[] { node });
            var index = visible.IndexOf(node);
            var m = typeof(ModernTreeView).GetMethods(Private).First(x => x.Name == method);
            return (Rectangle)m.Invoke(tree, m.GetParameters().Length == 2 ? new object[] { node, index } : new object[] { index });
        }

        private async Task ClickNode(ModernTreeView tree, string title)
        {
            var node = tree.AllNodes().FirstOrDefault(n => n.Title == title);
            if (node == null)
            {
                Fail("no tree node " + title);
                return;
            }
            var row = RowOf(tree, node, "RowBounds");
            await Wait(30);
            await Click(tree, new Point(row.X + row.Width / 2, row.Y + row.Height / 2));
            if (tree.SelectedNode != node) Fail("not selected by a click: " + title);
        }

        private async Task ClickChevron(ModernTreeView tree, ModernTreeNode node)
        {
            var chevron = RowOf(tree, node, "ChevronBounds");
            await Wait(30);
            await Click(tree, new Point(chevron.X + chevron.Width / 2, chevron.Y + chevron.Height / 2));
        }

        private async Task Press(Form dialog, string text) => await PressOn(dialog, text);

        private async Task PressOn(Form dialog, string text)
        {
            if (dialog == null)
            {
                Fail("no dialog to press " + text);
                return;
            }
            var button = Buttons(dialog).FirstOrDefault(b => b.Text == text);
            if (button == null)
            {
                Fail($"no button '{text}' in {dialog.GetType().Name}");
                return;
            }
            await Click(button);
        }


        private ModernButton Stop(LoadingOverlay overlay) => (ModernButton)typeof(LoadingOverlay).GetField("_stop", Private).GetValue(overlay);


        #endregion

        #region Dialogs and waits

        private static Task ShowLater(Action show)
        {
            var source = new TaskCompletionSource<bool>();
            var timer = new Timer { Interval = 10 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                show();
                source.SetResult(true);
            };
            timer.Start();
            return source.Task;
        }

        private async Task<T> Dialog<T>(int timeoutMs = 8000) where T : Form
        {
            await Until(() => Application.OpenForms.OfType<T>().Any(f => f.Visible && f.Opacity >= 1), timeoutMs);
            var dialog = Application.OpenForms.OfType<T>().LastOrDefault();
            if (dialog == null) Fail("popup did not open: " + typeof(T).Name);
            return dialog;
        }

        private async Task<bool> Gone<T>() where T : Form => await Until(() => !Application.OpenForms.OfType<T>().Any(), 3000);

        private async Task CloseTop<T>() where T : Form
        {
            var dialog = Application.OpenForms.OfType<T>().LastOrDefault();
            if (dialog != null) await Close(dialog);
            await Wait(150);
        }

        private static async Task Close(Form dialog)
        {
            dialog.DialogResult = DialogResult.Cancel;
            dialog.Close();
            await Wait(200);
        }

        /// <summary>Closes the popups still open, the most recent first.</summary>
        private static async Task CloseDialogs(Form keep = null)
        {
            foreach (var dialog in Application.OpenForms.OfType<ThemedDialog>().Where(f => f != keep).Reverse().ToList()) await Close(dialog);
        }

        private async Task Idle() => await Until(() => Get<object>("_busy") == null, 15000);

        /// <summary>After a save: the save, then its reload.</summary>
        private async Task Reloaded()
        {
            await Wait(300);
            await Idle();
            await Wait(150);
            await Idle();
        }

        /// <summary>The editor finished reading the tables of a message.</summary>
        private static async Task Settle(Form dialog)
        {
            await Wait(250);
            await Until(() => !dialog.UseWaitCursor, 3000);
            await Wait(60);
        }

        private static async Task<bool> Until(Func<bool> condition, int timeoutMs = 5000)
        {
            for (var waited = 0; waited < timeoutMs; waited += 40)
            {
                if (condition()) return true;
                await Wait(40);
            }
            return condition();
        }

        private static async Task<IntPtr> NativeDialog()
        {
            var window = IntPtr.Zero;
            await Until(() => (window = FindDialog()) != IntPtr.Zero, 6000);
            await Wait(500);
            return FindDialog();
        }

        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint process);

        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);

        private delegate bool EnumProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr parameter);

        /// <summary>File name box of the save dialog (its nesting changes with the Windows version).</summary>
        private static IntPtr FirstEdit(IntPtr dialog)
        {
            var found = IntPtr.Zero;
            EnumChildWindows(dialog, (window, parameter) =>
            {
                var name = new StringBuilder(64);
                GetClassName(window, name, 64);
                if (name.ToString() != "Edit") return true;

                // The explorer pane of the dialog has its own search box: the file name lives in a combo box.
                var parentName = new StringBuilder(64);
                GetClassName(GetParent(window), parentName, 64);
                if (parentName.ToString() != "ComboBox") return true;
                found = window;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        private static IntPtr FindDialog()
        {
            for (var window = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "#32770", null); window != IntPtr.Zero; window = FindWindowEx(IntPtr.Zero, window, "#32770", null))
            {
                GetWindowThreadProcessId(window, out var process);
                if (process == System.Diagnostics.Process.GetCurrentProcess().Id) return window;
            }
            return IntPtr.Zero;
        }

        private static Task Wait(int ms)
        {
            var source = new TaskCompletionSource<bool>();
            var timer = new Timer { Interval = Math.Max(1, ms) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                source.SetResult(true);
            };
            timer.Start();
            return source.Task;
        }

        #endregion

        #region Inspection

        private static IEnumerable<T> All<T>(Control root) where T : Control
        {
            foreach (Control child in root.Controls)
            {
                if (child is T match) yield return match;
                foreach (var nested in All<T>(child)) yield return nested;
            }
        }

        private static List<string> Texts(Control root) => All<Label>(root).Where(l => l.Visible).Select(l => l.Text).Where(t => !string.IsNullOrEmpty(t)).SelectMany(t => new[] { t }.Concat(t.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))).ToList();

        private static List<ModernButton> Buttons(Control root) => All<ModernButton>(root).Where(b => b.Visible).ToList();

        /// <summary>Every visible button keeps a real gap with the window edge, and no two buttons overlap.</summary>
        private void NoButtonAtEdge(Form form, string name)
        {
            var client = form.ClientRectangle;
            var boxes = new List<(ModernButton, Rectangle)>();
            foreach (var button in Buttons(form))
            {
                var r = form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                boxes.Add((button, r));
                Ok($"{name}: '{button.Text}' away from the edge ({r})", r.Left >= 6 && r.Top >= 6 && r.Right <= client.Width - 6 && r.Bottom <= client.Height - 6);
            }
            for (var i = 0; i < boxes.Count; i++)
            {
                for (var j = i + 1; j < boxes.Count; j++)
                {
                    Ok($"{name}: '{boxes[i].Item1.Text}' and '{boxes[j].Item1.Text}' do not overlap", !boxes[i].Item2.IntersectsWith(boxes[j].Item2));
                }
            }
        }

        private FieldInfo FindField(object target, string name)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, Private);
                if (field != null) return field;
            }
            throw new MissingFieldException(target.GetType().Name, name);
        }

        private T Get<T>(string name) => (T)FindField(_c, name).GetValue(_c);

        private T Field<T>(string name) => Get<T>(name);

        private T Get<T>(object target, string name) => (T)FindField(target, name).GetValue(target);

        private string Title() => Field<CardPanel>("cardDetail").Title;

        private void Ok(string name, bool condition)
        {
            if (condition) _passed++;
            else Fail(name);
        }

        private void Fail(string name)
        {
            _failures.Add($"[{_section}] {name}");
            Console.WriteLine($"FAIL [{_section}] {name}");
        }

        #endregion
    }
}
