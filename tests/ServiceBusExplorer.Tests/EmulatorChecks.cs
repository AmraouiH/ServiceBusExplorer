using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.Models;
using ServiceBusExplorer.Services;
using ServiceBusExplorer.Services.Azure;

namespace ServiceBusExplorer.Tests
{
    /// <summary>
    /// The real AMQP path against the Microsoft Service Bus emulator (docker): messages sent like Dataverse does,
    /// one dead-lettered by a failing consumer, one abandoned once, then peeked twice by the tool.
    /// Expects the queue « dataverse-queue » and the topic « dataverse-topic » with the subscriptions « all » and « accounts ».
    /// </summary>
    internal static class EmulatorChecks
    {
        public const string DefaultConnectionString = "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";
        private const string Queue = "dataverse-queue";
        private const string Topic = "dataverse-topic";
        private const string EntityProperty = "http://schemas.microsoft.com/xrm/2011/Claims/EntityLogicalName";

        private static int _passed;
        private static readonly List<string> Failures = new List<string>();

        private static void Ok(string name, bool condition)
        {
            if (condition) _passed++;
            else Failures.Add(name);
            Console.WriteLine((condition ? "  ok   " : "  FAIL ") + name);
        }

        public static int Run(string connectionString)
        {
            if (Environment.GetEnvironmentVariable("SBX_TRACE") == "1") AmqpMessageReader.Trace = e => Console.WriteLine("  TRACE " + e);
            var updateAccount = Guid.NewGuid();
            var createContact = Guid.NewGuid();
            var topicAccount = Guid.NewGuid();
            var topicContact = Guid.NewGuid();
            var steps = new[]
            {
                new StepInfo { Id = updateAccount, Name = "Update of account" },
                new StepInfo { Id = createContact, Name = "Create of contact" },
                new StepInfo { Id = topicAccount, Name = "Topic: update of account" },
                new StepInfo { Id = topicContact, Name = "Topic: update of contact" }
            };

            // A message locked by a receiver of the previous run cannot be purged for a minute, but a peek sees it: only this run counts.
            HashSet<string> ours;
            using (var bus = new TestBus(connectionString))
            {
                ours = bus.Sent;
                foreach (var path in new[] { Queue, Queue + "/$DeadLetterQueue", Topic + "/Subscriptions/all", Topic + "/Subscriptions/all/$DeadLetterQueue", Topic + "/Subscriptions/accounts", Topic + "/Subscriptions/accounts/$DeadLetterQueue" })
                {
                    Console.WriteLine($"purged {bus.Purge(path)} from {path}");
                }

                Properties Claims(string table, string message) => new Properties
                {
                    ["http://schemas.microsoft.com/xrm/2011/Claims/Organization"] = "contoso.crm4.dynamics.com",
                    ["http://schemas.microsoft.com/xrm/2011/Claims/User"] = "11111111-2222-3333-4444-555555555555",
                    [EntityProperty] = table,
                    ["http://schemas.microsoft.com/xrm/2011/Claims/RequestName"] = message
                };

                // Queue: the first one will be dead-lettered by a consumer, the second abandoned once.
                bus.Send(Queue, DataverseSamples.Binary(DataverseSamples.Context(updateAccount, "Update of account", "Update", "account", Guid.NewGuid())), "application/msbin1", Claims("account", "Update"));
                bus.Send(Queue, DataverseSamples.Json(DataverseSamples.Context(createContact, "Create of contact", "Create", "contact", Guid.NewGuid())), "application/json", Claims("contact", "Create"));
                bus.Send(Queue, DataverseSamples.Xml(DataverseSamples.Context(updateAccount, "Update of account", "Update", "account", Guid.NewGuid())), "application/xml", Claims("account", "Update"));
                bus.Send(Queue, Bytes.Utf8("plain text from another sender"), "text/plain", null, "manual");
                Ok("dead-letter by a consumer", bus.DeadLetter(Queue, "ListenerFailed", "The Azure Function threw NullReferenceException"));
                Ok("abandon by a consumer", bus.Abandon(Queue));

                bus.Send(Topic, DataverseSamples.Binary(DataverseSamples.Context(topicAccount, "Topic: update of account", "Update", "account", Guid.NewGuid())), "application/msbin1", Claims("account", "Update"));
                bus.Send(Topic, DataverseSamples.Binary(DataverseSamples.Context(topicContact, "Topic: update of contact", "Update", "contact", Guid.NewGuid())), "application/msbin1", Claims("contact", "Update"));
            }

            Thread.Sleep(1000);
            var ns = new SbNamespace { Host = "localhost" };
            var queue = new SbEntity { Kind = SbKind.Queue, Name = Queue, Namespace = ns };
            var topic = new SbEntity { Kind = SbKind.Topic, Name = Topic, Namespace = ns };
            foreach (var name in new[] { "all", "accounts" }) topic.Subscriptions.Add(new SbEntity { Kind = SbKind.Subscription, Name = name, Topic = topic, Namespace = ns });
            ns.Queues.Add(queue);
            ns.Topics.Add(topic);
            var info = ServiceBusConnectionInfo.Parse(connectionString);

            List<MessageBatch> Read(IEnumerable<SbEntity> sources)
            {
                var batches = MessageService.Read(sources, true, true, 100, _ => AmqpMessageReader.WithKey(info), p => Console.WriteLine("  … " + p), CancellationToken.None);
                MessageService.ResolveSteps(batches, steps);
                foreach (var batch in batches.Where(b => b.Error != null)) Console.WriteLine($"  error on {MessageService.PathOf(batch.Source)} dl={batch.DeadLetter}: {batch.Error.Message} {(batch.Error as AzureRequestException)?.Details}");
                return batches;
            }

            var first = Read(new[] { queue });
            var active = first.Single(b => !b.DeadLetter).Messages.Where(m => ours.Contains(m.MessageId)).ToList();
            var dead = first.Single(b => b.DeadLetter).Messages.Where(m => ours.Contains(m.MessageId)).ToList();
            foreach (var m in active.Concat(dead)) Console.WriteLine($"  #{m.SequenceNumber} dl={m.DeadLetter} delivery={m.DeliveryCount} {m.BodyFormat} step={m.Step?.Name ?? "-"} {m.Context?.MessageName} {m.Context?.Table} reason={m.DeadLetterReason} enq={m.EnqueuedOn:o}");
            Ok("no error on the queue", first.All(b => b.Error == null));
            Ok("three messages still active", active.Count == 3);
            Ok("one dead-lettered", dead.Count == 1);
            Ok("dead-letter reason", dead.FirstOrDefault()?.DeadLetterReason == "ListenerFailed");
            Ok("dead-letter description", dead.FirstOrDefault()?.DeadLetterDescription == "The Azure Function threw NullReferenceException");
            Ok("dead-lettered message keeps its step (binary)", dead.FirstOrDefault()?.Step?.Id == updateAccount && dead[0].BodyFormat == MessageDecoder.FormatBinary);
            Ok("JSON message linked to its step", active.Any(m => m.Step?.Id == createContact && m.BodyFormat == MessageDecoder.FormatJson));
            Ok("XML message linked to its step", active.Any(m => m.Step?.Id == updateAccount && m.BodyFormat == MessageDecoder.FormatXml));
            Ok("other sender is not Dataverse", active.Any(m => m.Context == null && m.BodyText == "plain text from another sender" && m.Subject == "manual"));
            Ok("abandoned message counts one delivery", active.Any(m => m.DeliveryCount == 1));
            Ok("sequence numbers", active.Concat(dead).All(m => m.SequenceNumber > 0));
            Ok("enqueued time", active.All(m => m.EnqueuedOn.HasValue && Math.Abs((DateTime.UtcNow - m.EnqueuedOn.Value).TotalMinutes) < 10));
            Ok("content type", active.Any(m => m.ContentType == "application/json"));
            Ok("application properties", active.Any(m => m.Property(EntityProperty) == "contact"));
            Ok("message id", active.All(m => !string.IsNullOrEmpty(m.MessageId)));

            var second = Read(new[] { queue });
            var again = second.SelectMany(b => b.Messages).Where(m => ours.Contains(m.MessageId)).ToList();
            Ok("peek again: nothing was taken", again.Count == 4);
            Ok("peek again: delivery counts did not move", again.OrderBy(m => m.SequenceNumber).Select(m => m.DeliveryCount).SequenceEqual(active.Concat(dead).OrderBy(m => m.SequenceNumber).Select(m => m.DeliveryCount)));

            var limited = MessageService.Read(new[] { queue }, true, false, 2, _ => AmqpMessageReader.WithKey(info), null, CancellationToken.None).Single();
            Ok("limit of 2", limited.Messages.Count == 2 && limited.Truncated);

            var subscriptions = Read(MessageService.SourcesOf(topic));
            var toAll = subscriptions.Single(b => b.Source.Name == "all" && !b.DeadLetter).Messages.Where(m => ours.Contains(m.MessageId)).ToList();
            var toAccounts = subscriptions.Single(b => b.Source.Name == "accounts" && !b.DeadLetter).Messages.Where(m => ours.Contains(m.MessageId)).ToList();
            Ok("topic: no error", subscriptions.All(b => b.Error == null));
            Ok("topic: each subscription has its copy", toAll.Count == 2 && toAll.Select(m => m.Step?.Id).OrderBy(i => i).SequenceEqual(new Guid?[] { topicAccount, topicContact }.OrderBy(i => i)));
            Console.WriteLine($"  accounts subscription: {toAccounts.Count} message(s) ({string.Join(", ", toAccounts.Select(m => m.Context?.Table))})");
            Ok("topic: the SQL filter kept the account only", toAccounts.Count == 1 && toAccounts[0].Step?.Id == topicAccount);

            var refused = MessageService.Read(new[] { queue }, true, true, 10, _ => AmqpMessageReader.WithKey(ServiceBusConnectionInfo.Parse(connectionString.Replace("SAS_KEY_VALUE", "wrong-key"))), null, CancellationToken.None);
            // The emulator accepts any key: the refusal of Azure cannot be checked here.
            Console.WriteLine("  wrong key (the emulator does not check keys): " + string.Join(" | ", refused.Select(b => b.Error == null ? $"{b.Messages.Count} read" : $"{(b.Error as AzureRequestException)?.StatusCode} {b.Error.Message}")));

            var missing = MessageService.Read(new[] { new SbEntity { Kind = SbKind.Queue, Name = "does-not-exist", Namespace = ns } }, true, false, 10, _ => AmqpMessageReader.WithKey(info), null, CancellationToken.None).Single();
            Console.WriteLine($"  missing queue: {(missing.Error as AzureRequestException)?.StatusCode} {missing.Error?.Message} {(missing.Error as AzureRequestException)?.Details}");
            Ok("missing queue is an error, not a crash", missing.Error is AzureRequestException);

            var nowhere = ServiceBusConnectionInfo.Parse("Endpoint=sb://nowhere.invalid;SharedAccessKeyName=a;SharedAccessKey=b;UseDevelopmentEmulator=true");
            var started = DateTime.UtcNow;
            var unreachable = MessageService.Read(new[] { queue }, true, true, 10, _ => AmqpMessageReader.WithKey(nowhere), null, CancellationToken.None);
            Console.WriteLine($"  unreachable: {unreachable[0].Error?.Message} {(unreachable[0].Error as AzureRequestException)?.Details} ({(DateTime.UtcNow - started).TotalSeconds:0.0} s)");
            Ok("unreachable is readable", unreachable.All(b => b.Error is AzureRequestException e && e.Message == Strings.AzureUnreachable("nowhere.invalid")));
            Ok("unreachable is tried once for both sub-queues", (DateTime.UtcNow - started).TotalSeconds < 25);

            Console.WriteLine($"{_passed} emulator checks passed, {Failures.Count} failed");
            foreach (var failure in Failures) Console.WriteLine("FAIL " + failure);
            return Failures.Count == 0 ? 0 : 1;
        }

        private sealed class Properties : Dictionary<string, string>
        {
        }
    }
}
