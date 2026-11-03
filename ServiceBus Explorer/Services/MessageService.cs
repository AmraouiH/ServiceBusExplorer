using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.Models;
using ServiceBusExplorer.Services.Azure;

namespace ServiceBusExplorer.Services
{
    /// <summary>
    /// Peeks the messages of queues and subscriptions, decodes them and finds the step that sent each one.
    /// A topic keeps no message: its subscriptions do, each with its own copy.
    /// </summary>
    public static class MessageService
    {
        /// <summary>Queues and subscriptions that hold the messages of an entity of the tree (none when it was not read on Azure).</summary>
        public static List<SbEntity> SourcesOf(SbEntity entity)
        {
            if (entity == null || entity.IsGhost || entity.Namespace == null || entity.Namespace.IsGhost || entity.Namespace.Error != null) return new List<SbEntity>();
            if (entity.Kind != SbKind.Topic) return new List<SbEntity> { entity };
            return entity.Subscriptions.Where(s => !s.IsGhost).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Queue or topic the endpoint of the step sends to, null when the endpoint matches nothing.</summary>
        public static SbEntity TargetOf(StepInfo step, ExplorerModel model) =>
            model?.Namespaces.SelectMany(n => n.Entities).FirstOrDefault(e => e.Endpoints.Any(x => x.Id == step.EndpointId));

        /// <summary>Path of the entity in the namespace, as AMQP addresses it.</summary>
        public static string PathOf(SbEntity entity) => entity.Kind == SbKind.Subscription ? $"{entity.Topic.Name}/Subscriptions/{entity.Name}" : entity.Name;

        /// <param name="openReader">Reader of a namespace, disposed after its entities are read.</param>
        /// <param name="max">Messages read per queue or subscription and per sub-queue.</param>
        public static List<MessageBatch> Read(IEnumerable<SbEntity> sources, bool active, bool deadLetter, int max,
            Func<SbNamespace, IMessageReader> openReader, Action<string> progress, CancellationToken token)
        {
            var batches = new List<MessageBatch>();
            foreach (var group in sources.GroupBy(s => s.Namespace))
            {
                var wanted = group.SelectMany(s => Queues(active, deadLetter).Select(dl => new MessageBatch { Source = s, DeadLetter = dl })).ToList();
                batches.AddRange(wanted);

                Exception fatal = null;
                IMessageReader reader = null;
                try
                {
                    reader = openReader(group.Key);
                }
                catch (Exception error) when (!(error is OperationCanceledException))
                {
                    fatal = error;
                }

                using (reader)
                {
                    foreach (var batch in wanted)
                    {
                        token.ThrowIfCancellationRequested();
                        if (fatal != null)
                        {
                            batch.Error = fatal;
                            continue;
                        }

                        var path = PathOf(batch.Source);
                        progress?.Invoke(Strings.ReadingMessages(batch.DeadLetter ? path + " · DLQ" : path));
                        try
                        {
                            var messages = reader.Peek(path, batch.DeadLetter, max + 1, token);
                            batch.Truncated = messages.Count > max;
                            batch.Messages = messages.Take(max).ToList();
                            foreach (var message in batch.Messages)
                            {
                                message.Source = batch.Source;
                                message.DeadLetter = batch.DeadLetter;
                                MessageDecoder.Decode(message);
                            }
                        }
                        catch (AzureRequestException error)
                        {
                            batch.Error = error;

                            // Refused or unreachable: the other entities of the namespace would fail the same way, after the same wait.
                            if (error.StatusCode == 401 || error.StatusCode == 403 || error.Message == Strings.AzureUnreachable(reader.Host)) fatal = error;
                        }
                    }
                }
            }
            return batches;
        }

        /// <summary>Links each message to the step named by its context.</summary>
        public static void ResolveSteps(IEnumerable<MessageBatch> batches, IEnumerable<StepInfo> steps)
        {
            var byId = new Dictionary<Guid, StepInfo>();
            foreach (var step in steps ?? Enumerable.Empty<StepInfo>()) byId[step.Id] = step;
            foreach (var message in batches.SelectMany(b => b.Messages))
            {
                var id = message.Context?.StepId;
                message.Step = id.HasValue && byId.TryGetValue(id.Value, out var step) ? step : null;
            }
        }

        private static IEnumerable<bool> Queues(bool active, bool deadLetter)
        {
            if (active) yield return false;
            if (deadLetter) yield return true;
        }
    }
}
