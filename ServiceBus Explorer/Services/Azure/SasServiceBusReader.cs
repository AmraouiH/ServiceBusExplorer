using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ServiceBusExplorer.Models;

namespace ServiceBusExplorer.Services.Azure
{
    /// <summary>
    /// Reads a namespace through its management endpoint with a shared access key.
    /// </summary>
    public sealed class SasServiceBusReader : IServiceBusReader
    {
        private const string ApiVersion = "2021-05";
        private const int PageSize = 100;

        private readonly ServiceBusConnectionInfo _connection;

        public SasServiceBusReader(ServiceBusConnectionInfo connection)
        {
            _connection = connection;
        }

        public string Host => _connection.Host;

        public void ReadDetails(SbNamespace ns, CancellationToken token)
        {
            // The location and the tier are not exposed to a shared access key.
        }

        public List<SbEntity> ReadQueues(CancellationToken token) => ReadEntities("$Resources/queues", SbKind.Queue, token);

        public List<SbEntity> ReadTopics(CancellationToken token) => ReadEntities("$Resources/topics", SbKind.Topic, token);

        public List<SbEntity> ReadSubscriptions(SbEntity topic, CancellationToken token) =>
            ReadEntities($"{Escape(topic.Name)}/Subscriptions", SbKind.Subscription, token);

        public List<SbRule> ReadRules(SbEntity subscription, CancellationToken token)
        {
            var path = $"{Escape(subscription.Topic.Name)}/Subscriptions/{Uri.EscapeDataString(subscription.Name)}/rules";
            return ReadPages(path, AtomParser.ParseRules, token);
        }

        private List<SbEntity> ReadEntities(string path, SbKind kind, CancellationToken token) =>
            ReadPages(path, feed => AtomParser.ParseEntities(feed, kind), token);

        /// <summary>Entity names can hold slashes: they stay path separators.</summary>
        private static string Escape(string path) => string.Join("/", path.Split('/').Select(Uri.EscapeDataString));

        private List<T> ReadPages<T>(string path, Func<string, List<T>> parse, CancellationToken token)
        {
            var items = new List<T>();
            for (var skip = 0; ; skip += PageSize)
            {
                token.ThrowIfCancellationRequested();
                var uri = new Uri(_connection.ManagementUri, $"{path}?api-version={ApiVersion}&$skip={skip}&$top={PageSize}");
                var feed = AzureHttp.Get(uri, _connection.CreateAuthorization(), false, token);
                items.AddRange(parse(feed));
                if (AtomParser.CountEntries(feed) < PageSize) return items;
            }
        }
    }
}
