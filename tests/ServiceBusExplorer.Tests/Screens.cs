using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using ServiceBusExplorer.Forms;
using ServiceBusExplorer.Helpers;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.Models;
using ServiceBusExplorer.Services;
using ServiceBusExplorer.Services.Azure;
using ServiceBusExplorer.Services.Dataverse;
using ServiceBusExplorer.UI.Controls;
using Timer = System.Windows.Forms.Timer;

namespace ServiceBusExplorer.Tests
{
    /// <summary>
    /// Drives the real control against the mock namespace and the in-memory organization, and saves a PNG of each screen.
    /// The sample names are shown on a public-looking host instead of localhost.
    /// </summary>
    internal static class Screens
    {
        private const string DemoHost = "contoso-crm.servicebus.windows.net";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static string _folder;
        private static readonly List<string> Log = new List<string>();

        public static int Run(string folder)
        {
            _folder = folder;
            Directory.CreateDirectory(folder);
            Application.EnableVisualStyles();

            using (var bus = new MockServiceBus())
            {
                Demo.Fill(bus);
                var organization = Demo.Organization("localhost");
                var control = new ServiceBusExplorerControl { Dock = DockStyle.Fill };
                var form = new Form { Width = 1420, Height = 900, StartPosition = FormStartPosition.Manual, Location = new Point(0, 0), Text = "ServiceBus Explorer" };
                form.Controls.Add(control);
                form.Shown += (s, e) => Script(form, control, organization, bus);
                Application.Run(form);
            }

            foreach (var line in Log) Console.WriteLine(line);
            return Log.Any(l => l.StartsWith("FAIL")) ? 1 : 0;
        }

        private static async void Script(Form form, ServiceBusExplorerControl control, FakeOrganizationService organization, MockServiceBus bus)
        {
            try
            {
                control.UpdateConnection(organization, new ConnectionDetail { ConnectionName = "demo" }, string.Empty, null);
                Set(control, "_settings", new PluginSettings
                {
                    Connections = new List<SavedConnection> { new SavedConnection { Host = "localhost", Method = ConnectionMethod.ConnectionString, ProtectedSecret = SecretStore.Protect(bus.ConnectionString) } }
                });

                await Wait(300);
                Shot(form, "01-empty");

                // Stop during a load: the screen is given back and nothing is half displayed.
                bus.Delay = 700;
                control.LoadAll();
                await Wait(900);
                ScreenShot(form, "00-loading");
                Note("overlay visible while loading", Field<LoadingOverlay>(control, "overlay").Visible);
                Call(control, "StopBusyTask");
                await WaitUntil(() => Get<object>(control, "_busy") == null, 6000);
                Note("stop gives the screen back", Get<object>(control, "_busy") == null && !Field<LoadingOverlay>(control, "overlay").Visible);
                Note("stop leaves no partial result", Get<object>(control, "_model") == null);
                bus.Delay = 0;

                control.LoadAll();
                await WaitUntil(() => Get<object>(control, "_busy") == null, 20000);
                Note("load finished", Get<object>(control, "_model") != null);

                // Same data under a readable host name.
                var azure = Get<List<SbNamespace>>(control, "_azure");
                var model = Get<ExplorerModel>(control, "_model");
                Note("namespace read through HTTP", azure.Single().Queues.Count(q => !q.IsGhost) == 4 && azure.Single().Error == null);
                azure.Single().Host = DemoHost;
                foreach (var endpoint in model.Endpoints.Where(e => e.Host == "localhost")) endpoint.Host = DemoHost;
                Set(control, "_model", MatchingService.Build(azure, model.Endpoints, model.Steps));
                Call(control, "RefreshAll");
                await Wait(200);

                var tree = Field<ModernTreeView>(control, "tree");
                Select(tree, "crm-contact-changes");
                await Wait(200);
                Shot(form, "02-explorer-topic");

                Field<SegmentedTabs>(control, "tabsDetail").SelectedIndex = 1;
                var subscriptions = Field<DataGridView>(control, "dgvSubscriptions");
                subscriptions.Rows[1].Selected = true;
                await Wait(200);
                var rules = Field<DataGridView>(control, "dgvRules");
                if (rules.Rows.Count > 0) rules.Rows[0].Selected = true;
                await Wait(200);
                Shot(form, "03-explorer-filters");

                Field<SegmentedTabs>(control, "tabsDetail").SelectedIndex = 2;
                Select(tree, "crm-invoice-events");
                await Wait(200);
                Shot(form, "04-explorer-queue-properties");

                Field<SegmentedTabs>(control, "tabsDetail").SelectedIndex = 0;
                Select(tree, "billing-events");
                await Wait(200);
                Shot(form, "05-explorer-ghost");

                Field<SegmentedTabs>(control, "tabs").SelectedIndex = 1;
                await Wait(300);
                Shot(form, "06-matching");
                var matching = Field<DataGridView>(control, "dgvMatching");
                Note("matching lists the rows", matching.Rows.Count >= 9);

                Field<SegmentedTabs>(control, "tabs").SelectedIndex = 2;
                await Wait(300);
                Shot(form, "07-steps");

                // Language switch on the live screen.
                Strings.Current = Language.French;
                await Wait(300);
                Field<SegmentedTabs>(control, "tabs").SelectedIndex = 0;
                await Wait(300);
                Shot(form, "08-explorer-french");
                Strings.Current = Language.English;
                await Wait(200);

                await MessageScreens(form, control);
                await Dialogs(form, control, organization);
                await Flows(form, control, organization, bus);

                control.Dispose();
                form.Close();
            }
            catch (Exception error)
            {
                Log.Add("FAIL script: " + error);
                form.Close();
            }
        }

        /// <summary>Messages of a queue (active and dead-letter), of a topic, and of one step, from an in-memory reader.</summary>
        private static async System.Threading.Tasks.Task MessageScreens(Form form, ServiceBusExplorerControl control)
        {
            var model = Get<ExplorerModel>(control, "_model");
            StepInfo Step(string name) => model.Steps.First(s => s.Name == name);
            byte[] Binary(StepInfo step) => DataverseSamples.Binary(DataverseSamples.Context(step.Id, step.Name, step.Message, step.Table, Guid.NewGuid()));
            byte[] Json(StepInfo step) => DataverseSamples.Json(DataverseSamples.Context(step.Id, step.Name, step.Message, step.Table, Guid.NewGuid()));

            var reader = new FakeMessageReader();
            reader.Add("crm-account-events", Binary(Step("Update of account")), "application/msbin1");
            reader.Add("crm-account-events", Binary(Step("Update of account")), "application/msbin1");
            reader.Add("crm-account-events", Json(Step("Create of account")), "application/json", 1);
            reader.Add("crm-account-events", Binary(Step("Update of account")), "application/msbin1", 2);
            reader.Add("crm-account-events/$DeadLetterQueue", Binary(Step("Update of account")), "application/msbin1", 10, "MaxDeliveryCountExceeded", "Message could not be consumed after 10 delivery attempts.");
            reader.Add("crm-account-events/$DeadLetterQueue", Json(Step("Create of account")), "application/json", 1, "ListenerFailed", "ERP rejected the account: missing VAT number");
            reader.Add("crm-contact-changes/Subscriptions/data-warehouse", Json(Step("Update of contact")), "application/json");
            reader.Add("crm-contact-changes/Subscriptions/data-warehouse", Json(Step("Create of contact")), "application/json");
            reader.Add("crm-contact-changes/Subscriptions/data-warehouse/$DeadLetterQueue", Json(Step("Update of contact")), "application/json", 10, "MaxDeliveryCountExceeded", "Message could not be consumed after 10 delivery attempts.");
            reader.Add("crm-contact-changes/Subscriptions/marketing-platform", Json(Step("Create of contact")), "application/json");
            var previous = ConnectionFactory.MessageReaderFactory;
            ConnectionFactory.MessageReaderFactory = (connection, settings) => reader;
            foreach (var connection in Get<PluginSettings>(control, "_settings").Connections) connection.Host = DemoHost;

            try
            {
                var tree = Field<ModernTreeView>(control, "tree");
                var tabs = Field<SegmentedTabs>(control, "tabsDetail");
                var panel = Field<MessagesPanel>(control, "messagesExplorer");
                Field<SegmentedTabs>(control, "tabs").SelectedIndex = 0;

                Select(tree, "crm-account-events");
                tabs.SelectedIndex = 3;
                await Wait(200);
                Shot(form, "21-messages-before-read");

                Call(control, "ReadExplorerMessages");
                await WaitUntil(() => Get<object>(control, "_busy") == null, 8000);
                await Wait(200);
                var grid = Field<DataGridView>(panel, "_grid");
                Note("active messages only", grid.Rows.Count == 4);
                var json = grid.Rows.Cast<DataGridViewRow>().First(r => ((SbMessage)r.Tag).BodyFormat == MessageDecoder.FormatJson);
                grid.ClearSelection();
                json.Selected = true;
                await Wait(200);
                Shot(form, "22-messages-queue");

                Select(tree, "crm-contact-changes");
                tabs.SelectedIndex = 3;
                Call(control, "ReadExplorerMessages");
                await WaitUntil(() => Get<object>(control, "_busy") == null, 8000);
                await Wait(200);
                Note("topic active messages read", grid.Rows.Count == 3);
                Shot(form, "23-messages-topic");

                var step = Step("Update of account");
                await ShowDialogAction(() => Call(control, "ShowStepMessages", new List<StepInfo> { step }), "24-messages-step");
                tabs.SelectedIndex = 0;
            }
            finally
            {
                ConnectionFactory.MessageReaderFactory = previous;
            }
        }

        private static async System.Threading.Tasks.Task Dialogs(Form form, ServiceBusExplorerControl control, FakeOrganizationService organization)
        {
            var service = Get<EndpointService>(control, "_endpoints");
            var model = Get<ExplorerModel>(control, "_model");
            var step = model.Steps.First(s => s.Images.Count == 2);
            var messages = service.LoadMessages();
            var tables = service.LoadTables(step.MessageId);

            await ShowDialog(new StepEditorDialog(step, messages, tables, service), "09-step-editor", null);

            var image = step.Images[0].Clone();
            await ShowDialog(new ImageEditorDialog(image, StepRules.AllowedImageTypes(step.Message, step.Stage), service, step.Table), "10-image-editor", null);

            await ShowDialog(new ColumnPickerDialog(service, "account", "name,telephone1"), "11-column-picker", null);

            var settings = Get<PluginSettings>(control, "_settings");
            await ShowDialog(new ConnectionsDialog(settings, new List<string> { "contoso-legacy.servicebus.windows.net" }, null), "12-connections", dialog =>
            {
                Log.Add("connections tabs ok");
            });
            await ShowDialog(new OptionsDialog(settings), "13-options", null);

            var managed = model.Steps.First(s => s.IsManaged);
            await ShowDialog(new StepEditorDialog(managed, messages, service.LoadTables(managed.MessageId), service), "14-step-readonly", null);

            // Invalid edit: the Save button stays disabled and the rule is displayed.
            var invalid = new StepEditorDialog(step, messages, tables, service);
            await ShowDialog(invalid, "15-step-invalid", dialog =>
            {
                var stage = Field<ComboBox>(dialog, "_stage");
                stage.SelectedIndex = 1;
                Note("async on pre-operation cannot be saved", !Field<ModernButton>(dialog, "_save").Enabled);
            });

            await ShowDialogAction(() => MessageDialog.Show(form, MessageKind.Warning, Strings.ReviewTitle, Strings.ReviewMessage(step.Name) + Environment.NewLine + Environment.NewLine + "•  " + Strings.FieldRank + " : 1  →  3", "Sample technical details"), "17-review");
        }

        /// <summary>The real paths: buttons, popups with their backdrop, writes, reload.</summary>
        private static async System.Threading.Tasks.Task Flows(Form form, ServiceBusExplorerControl control, FakeOrganizationService organization, MockServiceBus bus)
        {
            var model = Get<ExplorerModel>(control, "_model");
            var step = model.Steps.First(s => s.Images.Count == 2);
            var enabledBefore = step.Enabled;
            organization.Updates.Clear();

            // Edit: rank + one new image, review popup, save, reload.
            var script = Poll(() =>
            {
                var editor = Application.OpenForms.OfType<StepEditorDialog>().FirstOrDefault();
                if (editor == null) return false;
                Field<NumericUpDown>(editor, "_rank").Value = 7;
                editor.BeginInvoke(new Action(() => Field<ModernButton>(editor, "_save").PerformClick()));
                return true;
            }, () =>
            {
                var review = Application.OpenForms.OfType<MessageDialog>().FirstOrDefault();
                if (review == null) return false;
                Shot(review, "18-review-real");
                review.DialogResult = DialogResult.Yes;
                review.Close();
                return true;
            });
            Call(control, "EditStep", step);
            await script;
            await WaitUntil(() => Get<object>(control, "_busy") == null && organization.Updates.Count > 0, 8000);
            await Wait(400);
            await WaitUntil(() => Get<object>(control, "_busy") == null, 8000);
            Note("edit wrote the rank only", organization.Updates.Count == 1 && organization.Updates[0].Attributes.Count == 1 && (int)organization.Updates[0]["rank"] == 7);
            var reloaded = Get<ExplorerModel>(control, "_model").Steps.First(s => s.Id == step.Id);
            Note("screen shows the saved value", reloaded.Rank == 7);
            Note("azure not called on reload", Get<List<SbNamespace>>(control, "_azure").Single().Host == DemoHost);

            // Bulk disable, with its confirmation.
            organization.Updates.Clear();
            var targets = Get<ExplorerModel>(control, "_model").Steps.Where(s => s.Enabled).Take(2).ToList();
            var confirm = Poll(() =>
            {
                var ask = Application.OpenForms.OfType<MessageDialog>().FirstOrDefault();
                if (ask == null) return false;
                Shot(ask, "19-bulk-confirm");
                ask.DialogResult = DialogResult.Yes;
                ask.Close();
                return true;
            });
            Call(control, "SetStepsEnabled", targets, false);
            await confirm;
            await WaitUntil(() => Get<object>(control, "_busy") == null && organization.Updates.Count >= 2, 8000);
            await Wait(400);
            await WaitUntil(() => Get<object>(control, "_busy") == null, 8000);
            Note("bulk disable wrote both steps", organization.Updates.Count == 2);
            var ids = targets.Select(t => t.Id).ToList();
            Note("bulk disable is displayed", Get<ExplorerModel>(control, "_model").Steps.Where(s => ids.Contains(s.Id)).All(s => !s.Enabled));

            // An unreadable namespace does not stop the rest.
            bus.RefuseAll = true;
            control.LoadAll();
            await WaitUntil(() => Get<object>(control, "_busy") == null, 8000);
            var failed = Get<List<SbNamespace>>(control, "_azure").Single();
            Note("refused namespace is an error on its node", failed.Error != null);
            Note("steps stay available when Azure refuses", Get<ExplorerModel>(control, "_model").Steps.Count == 11);
            Field<SegmentedTabs>(control, "tabs").SelectedIndex = 0;
            var tree = Field<ModernTreeView>(control, "tree");
            tree.ExpandAll();
            tree.SelectedNode = tree.AllNodes().First();
            await Wait(300);
            Shot(form, "20-namespace-refused");
            bus.RefuseAll = false;
        }

        /// <summary>Runs each step as soon as it reports that its popup was there.</summary>
        private static System.Threading.Tasks.Task Poll(params Func<bool>[] steps)
        {
            var source = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var index = 0;
            var ticks = 0;
            var timer = new Timer { Interval = 300 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                try
                {
                    if (steps[index]()) index++;
                }
                catch (Exception error)
                {
                    Log.Add("FAIL poll: " + error);
                    index = steps.Length;
                }
                if (index >= steps.Length || ++ticks > 60)
                {
                    if (index < steps.Length) Log.Add("FAIL poll timed out at step " + index);
                    timer.Dispose();
                    source.TrySetResult(true);
                    return;
                }
                timer.Start();
            };
            timer.Start();
            return source.Task;
        }
        private static async System.Threading.Tasks.Task ShowDialog(ThemedDialog dialog, string name, Action<ThemedDialog> act)
        {
            var timer = new Timer { Interval = 700 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                try
                {
                    act?.Invoke(dialog);
                    Application.DoEvents();
                    Shot(dialog, name);
                }
                catch (Exception error)
                {
                    Log.Add($"FAIL {name}: {error}");
                }
                dialog.DialogResult = DialogResult.Cancel;
                dialog.Close();
            };
            timer.Start();
            await System.Threading.Tasks.Task.Yield();
            dialog.ShowDialog();
            dialog.Dispose();
            await Wait(100);
        }

        private static async System.Threading.Tasks.Task ShowDialogAction(Action show, string name)
        {
            var timer = new Timer { Interval = 700 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                var dialog = Application.OpenForms.OfType<ThemedDialog>().LastOrDefault();
                if (dialog == null) return;
                Shot(dialog, name);
                dialog.Close();
            };
            timer.Start();
            await System.Threading.Tasks.Task.Yield();
            show();
            await Wait(100);
        }

        private static void Select(ModernTreeView tree, string title)
        {
            tree.ExpandAll();
            var node = tree.AllNodes().FirstOrDefault(n => n.Title == title);
            Note("tree has " + title, node != null);
            tree.SelectedNode = node;
        }

        private static void Shot(Form form, string name)
        {
            using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                bitmap.Save(Path.Combine(_folder, name + ".png"), ImageFormat.Png);
            }
            Console.WriteLine("saved " + name);
        }

        /// <summary>Copy of the real screen: DrawToBitmap does not render the overlay above its siblings.</summary>
        private static void ScreenShot(Form form, string name)
        {
            form.TopMost = true;
            form.Activate();
            Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(form.Location, Point.Empty, form.Size);
                bitmap.Save(Path.Combine(_folder, name + ".png"), ImageFormat.Png);
            }
            form.TopMost = false;
        }

        private static void Note(string name, bool ok)
        {
            Console.WriteLine((ok ? "ok   " : "FAIL ") + name);
            if (!ok) Log.Add("FAIL " + name);
        }

        private static System.Threading.Tasks.Task Wait(int ms)
        {
            var source = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var timer = new Timer { Interval = ms };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                source.SetResult(true);
            };
            timer.Start();
            return source.Task;
        }

        private static async System.Threading.Tasks.Task WaitUntil(Func<bool> condition, int timeoutMs)
        {
            var waited = 0;
            while (!condition() && waited < timeoutMs)
            {
                await Wait(100);
                waited += 100;
            }
        }

        private static FieldInfo FindField(object target, string name)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, Private);
                if (field != null) return field;
            }
            throw new MissingFieldException(target.GetType().Name, name);
        }

        private static T Get<T>(object target, string name) => (T)FindField(target, name).GetValue(target);

        private static T Field<T>(object target, string name) => Get<T>(target, name);

        private static void Set(object target, string name, object value) => FindField(target, name).SetValue(target, value);

        private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    }
}
