using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.Models;

namespace ServiceBusExplorer.Services.Azure
{
    /// <summary>
    /// Reads a whole namespace: queues, topics, then the subscriptions and rules in parallel
    /// (one request per topic and per subscription).
    /// </summary>
    public static class NamespaceLoader
    {
        public static SbNamespace Load(IServiceBusReader reader, int parallelRequests, Action<string> progress, CancellationToken token)
        {
            var ns = new SbNamespace { Host = reader.Host, ReadWithKey = reader is SasServiceBusReader };
            reader.ReadDetails(ns, token);

            progress(Strings.ReadingQueues(ns.Name));
            ns.Queues = reader.ReadQueues(token);
            progress(Strings.ReadingTopics(ns.Name));
            ns.Topics = reader.ReadTopics(token);
            foreach (var entity in ns.Entities) entity.Namespace = ns;

            var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(16, parallelRequests)), CancellationToken = token };
            try
            {
                progress(Strings.ReadingSubscriptions(ns.Name, ns.Topics.Count));
                Parallel.ForEach(ns.Topics, options, topic =>
                {
                    var subscriptions = reader.ReadSubscriptions(topic, token);
                    foreach (var subscription in subscriptions)
                    {
                        subscription.Namespace = ns;
                        subscription.Topic = topic;
                    }
                    topic.Subscriptions.AddRange(subscriptions);
                    topic.SubscriptionCount = subscriptions.Count;
                });

                var all = ns.AllSubscriptions.ToList();
                progress(Strings.ReadingRules(ns.Name, all.Count));
                Parallel.ForEach(all, options, subscription =>
                {
                    var rules = reader.ReadRules(subscription, token);
                    foreach (var rule in rules) rule.Subscription = subscription;
                    subscription.Rules.AddRange(rules);
                });
            }
            catch (AggregateException error)
            {
                // The first cause is the useful one: the same refusal comes back from every parallel call.
                throw error.Flatten().InnerExceptions[0];
            }

            return ns;
        }
    }
}
