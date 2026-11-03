using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using ServiceBusExplorer.Models;

namespace ServiceBusExplorer.Services.Azure
{
    /// <summary>Paged GET on Azure Resource Manager, and discovery of the namespaces of an account.</summary>
    public sealed class ArmClient
    {
        /// <summary>Azure Resource Manager (replaced by the tests).</summary>
        internal static string Root = "https://management.azure.com";
        private const string TenantApi = "2022-12-01";
        internal const string ServiceBusApi = "2021-11-01";

        private readonly IArmTokenProvider _tokens;

        public ArmClient(IArmTokenProvider tokens)
        {
            _tokens = tokens;
        }

        public JObject GetObject(string path, string apiVersion, string tenantId, CancellationToken token) =>
            JObject.Parse(AzureHttp.Get(Url(path, apiVersion), _tokens.GetToken(tenantId, TokenResource.Management, token), true, token));

        public List<JObject> GetAll(string path, string apiVersion, string tenantId, CancellationToken token)
        {
            var items = new List<JObject>();
            var next = Url(path, apiVersion);
            while (next != null)
            {
                token.ThrowIfCancellationRequested();
                var page = JObject.Parse(AzureHttp.Get(next, _tokens.GetToken(tenantId, TokenResource.Management, token), true, token));
                if (page["value"] is JArray value) items.AddRange(value.OfType<JObject>());
                var link = (string)page["nextLink"];
                next = string.IsNullOrEmpty(link) ? null : new Uri(link);
            }
            return items;
        }

        private static Uri Url(string path, string apiVersion) => new Uri($"{Root}{path}?api-version={apiVersion}");

        /// <summary>Every namespace the account can read, in all its tenants. A tenant that refuses the token is skipped.</summary>
        public List<DiscoveredNamespace> DiscoverNamespaces(string defaultTenant, Action<string> progress, CancellationToken token)
        {
            var found = new List<DiscoveredNamespace>();
            var tenants = GetAll("/tenants", TenantApi, defaultTenant, token).Select(t => (string)t["tenantId"]).Where(t => t != null).ToList();
            foreach (var tenant in tenants)
            {
                List<JObject> subscriptions;
                try
                {
                    subscriptions = GetAll("/subscriptions", TenantApi, tenant, token);
                }
                catch (AzureRequestException)
                {
                    continue;
                }

                foreach (var subscription in subscriptions)
                {
                    var name = (string)subscription["displayName"];
                    progress?.Invoke(name);
                    List<JObject> namespaces;
                    try
                    {
                        namespaces = GetAll($"/subscriptions/{subscription["subscriptionId"]}/providers/Microsoft.ServiceBus/namespaces", ServiceBusApi, tenant, token);
                    }
                    catch (AzureRequestException)
                    {
                        // Subscription without access, or the Microsoft.ServiceBus provider is not registered on it.
                        continue;
                    }

                    found.AddRange(namespaces.Select(n => new DiscoveredNamespace
                    {
                        Host = ArmParser.HostOf(n),
                        ResourceId = (string)n["id"],
                        TenantId = tenant,
                        SubscriptionName = name,
                        ResourceGroup = ArmParser.Segment((string)n["id"], "resourceGroups"),
                        Location = (string)n["location"],
                        Sku = (string)n.SelectToken("sku.name")
                    }).Where(n => n.Host != null));
                }
            }
            return found.OrderBy(n => n.Host, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>Reads a namespace through Azure Resource Manager: the Reader role is enough, no key is needed.</summary>
    public sealed class ArmServiceBusReader : IServiceBusReader
    {
        private readonly ArmClient _client;
        private readonly string _resourceId;
        private readonly string _tenantId;
        private bool _hasTopics = true;

        public ArmServiceBusReader(IArmTokenProvider tokens, string host, string resourceId, string tenantId)
        {
            _client = new ArmClient(tokens);
            Host = host;
            _resourceId = resourceId;
            _tenantId = tenantId;
        }

        public string Host { get; }

        public void ReadDetails(SbNamespace ns, CancellationToken token)
        {
            ArmParser.FillNamespace(ns, _client.GetObject(_resourceId, ArmClient.ServiceBusApi, _tenantId, token));
            _hasTopics = !string.Equals(ns.Sku, "Basic", StringComparison.OrdinalIgnoreCase);
        }

        public List<SbEntity> ReadQueues(CancellationToken token) => Entities($"{_resourceId}/queues", SbKind.Queue, token);

        // The Basic tier has no topics: the call would be refused.
        public List<SbEntity> ReadTopics(CancellationToken token) => _hasTopics ? Entities($"{_resourceId}/topics", SbKind.Topic, token) : new List<SbEntity>();

        public List<SbEntity> ReadSubscriptions(SbEntity topic, CancellationToken token) =>
            Entities($"{_resourceId}/topics/{Uri.EscapeDataString(topic.Name)}/subscriptions", SbKind.Subscription, token);

        public List<SbRule> ReadRules(SbEntity subscription, CancellationToken token)
        {
            var path = $"{_resourceId}/topics/{Uri.EscapeDataString(subscription.Topic.Name)}/subscriptions/{Uri.EscapeDataString(subscription.Name)}/rules";
            return _client.GetAll(path, ArmClient.ServiceBusApi, _tenantId, token).Select(ArmParser.ToRule).ToList();
        }

        private List<SbEntity> Entities(string path, SbKind kind, CancellationToken token) =>
            _client.GetAll(path, ArmClient.ServiceBusApi, _tenantId, token).Select(r => ArmParser.ToEntity(r, kind)).ToList();
    }
}
