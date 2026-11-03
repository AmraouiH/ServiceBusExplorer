using System;
using System.ComponentModel.Composition.Hosting;
using System.Diagnostics;
using System.Linq;
using Microsoft.Xrm.Tooling.Connector;
using ServiceBusExplorer.Models;
using ServiceBusExplorer.Services.Dataverse;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace ServiceBusExplorer.Tests
{
    /// <summary>
    /// Checks against the real world: MEF discovery as XrmToolBox does it, and read-only calls on a real organization.
    /// </summary>
    internal static class RealChecks
    {
        public static int Mef()
        {
            using (var catalog = new AssemblyCatalog(typeof(ServiceBusExplorerPlugin).Assembly))
            using (var container = new CompositionContainer(catalog))
            {
                var exports = container.GetExports<IXrmToolBoxPlugin, IPluginMetadata>().ToList();
                Console.WriteLine("exports: " + exports.Count);
                var export = exports.Single();
                Console.WriteLine("name: " + export.Metadata.Name);
                Console.WriteLine("description: " + export.Metadata.Description);
                Console.WriteLine("icons: " + (export.Metadata.SmallImageBase64?.Length > 100) + " " + (export.Metadata.BigImageBase64?.Length > 100));
                Console.WriteLine("extensibility: " + typeof(PluginBase).Assembly.GetName().Version);
                Console.WriteLine("donation: " + (export.Value is IPayPalPlugin));
                var control = export.Value.GetControl();
                Console.WriteLine("control: " + control.GetType().FullName);
                ((IDisposable)control).Dispose();
            }
            return 0;
        }

        /// <summary>Only reads: nothing here writes to the organization.</summary>
        public static int Organization(string url)
        {
            CrmServiceClient.AuthOverrideHook = new AzToken(url);
            var client = new CrmServiceClient(new Uri(url), true);
            if (!client.IsReady)
            {
                Console.WriteLine("not ready: " + client.LastCrmError);
                return 1;
            }

            var service = new EndpointService(client);
            var endpoints = service.LoadEndpoints();
            Console.WriteLine($"endpoints: {endpoints.Count}: " + string.Join(", ", endpoints.Select(e => $"{e.Name} (contract {e.Contract}, host {e.Host ?? "none"}, path '{e.Path}')")));

            var messages = service.LoadMessages();
            Console.WriteLine($"messages: {messages.Count}, Update: {messages.Any(m => m.Value == "Update")}, Create: {messages.Any(m => m.Value == "Create")}");

            var update = messages.First(m => m.Value == "Update").Key;
            var tables = service.LoadTables(update);
            Console.WriteLine($"tables of Update: {tables.Count}, account: {tables.ContainsKey("account")}, all tables: {tables.ContainsKey(string.Empty)}");
            var create = service.LoadTables(messages.First(m => m.Value == "Create").Key);
            Console.WriteLine($"tables of Create: {create.Count}, contact: {create.ContainsKey("contact")}");
            if (!tables.ContainsKey("account") || !create.ContainsKey("contact")) return 1;

            var columns = service.LoadColumns("account");
            Console.WriteLine($"columns of account: {columns.Count}, name: {columns.Any(c => c.LogicalName == "name")}, sample: {columns[0].LogicalName} / {columns[0].DisplayName} / {columns[0].Type}");

            // The org has no Service Bus step: real rows go through the same mapping, using a plug-in type as event handler.
            var query = new Microsoft.Xrm.Sdk.Query.QueryExpression("sdkmessageprocessingstepimage") { ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet("sdkmessageprocessingstepid"), TopCount = 1 };
            var image = client.RetrieveMultiple(query).Entities.First();
            var stepId = image.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>("sdkmessageprocessingstepid").Id;
            var step = client.Retrieve("sdkmessageprocessingstep", stepId, new Microsoft.Xrm.Sdk.Query.ColumnSet("eventhandler"));
            var handler = step.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>("eventhandler");
            var steps = service.LoadSteps(new[] { new EndpointInfo { Id = handler.Id } });
            var sample = steps.First(s => s.Id == stepId);
            Console.WriteLine($"handler type: {handler.LogicalName}, steps read: {steps.Count}");
            Console.WriteLine($"step: {sample.Message} / {sample.Table} / stage {sample.Stage} / mode {sample.Mode} / rank {sample.Rank} / enabled {sample.Enabled} / managed {sample.IsManaged} / customizable {sample.IsCustomizable} / images {sample.Images.Count}");
            var first = sample.Images.First();
            Console.WriteLine($"image: {first.Alias} / type {first.ImageType} / property {first.MessageProperty} / columns {(first.AllColumns ? "all" : first.Attributes)}");
            return 0;
        }

        private sealed class AzToken : IOverrideAuthHookWrapper
        {
            private readonly string _token;

            public AzToken(string url)
            {
                _token = Token(url);
            }

            public string GetAuthToken(Uri connectedUri) => _token;
        }

        private static string Token(string resource)
        {
            var start = new ProcessStartInfo("cmd.exe", $"/c az account get-access-token --resource {resource} --query accessToken -o tsv")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using (var process = Process.Start(start))
            {
                var token = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return token;
            }
        }
    }
}
