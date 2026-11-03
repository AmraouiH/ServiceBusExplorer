using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Azure.Amqp;
using Microsoft.Azure.Amqp.Encoding;
using Microsoft.Azure.Amqp.Framing;
using Microsoft.Azure.Amqp.Sasl;
using Microsoft.Azure.Amqp.Transport;
using Microsoft.Xrm.Sdk;
using ServiceBusExplorer.Services.Azure;

namespace ServiceBusExplorer.Tests
{
    /// <summary>Bodies as Dataverse sends them: a RemoteExecutionContext serialized by the SDK in the three endpoint formats.</summary>
    internal static class DataverseSamples
    {
        public static readonly DateTime CreatedOn = new DateTime(2026, 11, 3, 9, 30, 15, DateTimeKind.Utc);

        public static RemoteExecutionContext Context(Guid step, string stepName, string message, string table, Guid record)
        {
            var context = new RemoteExecutionContext
            {
                MessageName = message,
                PrimaryEntityName = table,
                PrimaryEntityId = record,
                Stage = 40,
                Mode = 1,
                Depth = 1,
                UserId = new Guid("11111111-2222-3333-4444-555555555555"),
                InitiatingUserId = new Guid("11111111-2222-3333-4444-555555555555"),
                OrganizationName = "contoso",
                OrganizationId = Guid.NewGuid(),
                CorrelationId = new Guid("99999999-8888-7777-6666-555555555555"),
                OperationId = Guid.NewGuid(),
                OperationCreatedOn = CreatedOn,
                OwningExtension = new EntityReference("sdkmessageprocessingstep", step) { Name = stepName }
            };
            context.InputParameters["Target"] = new Entity(table, record) { ["name"] = "Contoso Ltd", ["creditlimit"] = new Money(1500m) };
            return context;
        }

        /// <summary>.NETBinary: DataContractSerializer over the binary XML writer (application/msbin1).</summary>
        public static byte[] Binary(RemoteExecutionContext context)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = XmlDictionaryWriter.CreateBinaryWriter(stream))
                {
                    new DataContractSerializer(typeof(RemoteExecutionContext)).WriteObject(writer, context);
                }
                return stream.ToArray();
            }
        }

        public static byte[] Xml(RemoteExecutionContext context)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractSerializer(typeof(RemoteExecutionContext)).WriteObject(stream, context);
                return stream.ToArray();
            }
        }

        public static byte[] Json(RemoteExecutionContext context)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(RemoteExecutionContext)).WriteObject(stream, context);
                return stream.ToArray();
            }
        }
    }

    /// <summary>
    /// Sends, dead-letters, abandons and purges messages on a namespace (the local emulator in the tests), the way a
    /// sender and a failing consumer would. Only used by the tests: the tool itself never writes to Service Bus.
    /// </summary>
    internal sealed class TestBus : IDisposable
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
        private readonly ServiceBusConnectionInfo _info;
        private readonly AmqpConnection _connection;
        private readonly AmqpSession _session;
        private readonly AmqpCbsLink _cbs;

        public TestBus(string connectionString)
        {
            _info = ServiceBusConnectionInfo.Parse(connectionString);
            var settings = new AmqpSettings();
            var sasl = new SaslTransportProvider();
            sasl.Versions.Add(new AmqpVersion(1, 0, 0));
            sasl.AddHandler(new SaslAnonymousHandler("MSSBCBS"));
            var amqp = new AmqpTransportProvider();
            amqp.Versions.Add(new AmqpVersion(1, 0, 0));
            settings.TransportProviders.Add(sasl);
            settings.TransportProviders.Add(amqp);
            var transport = new AmqpTransportInitiator(settings, new TcpTransportSettings { Host = _info.Host, Port = 5672 }).ConnectTaskAsync(Timeout).GetAwaiter().GetResult();
            _connection = new AmqpConnection(transport, settings, new AmqpConnectionSettings { ContainerId = Guid.NewGuid().ToString("N"), HostName = _info.Host });
            _connection.Open(Timeout);
            _cbs = new AmqpCbsLink(_connection);
            _session = _connection.CreateSession(new AmqpSessionSettings());
            _session.Open(Timeout);
        }

        private void Authorize(string path)
        {
            var audience = $"amqp://{_info.Host}/{path}";
            var expires = DateTime.UtcNow.AddHours(1);
            var token = new CbsToken(ServiceBusConnectionInfo.CreateToken(audience, _info.KeyName, _info.Key, expires), "servicebus.windows.net:sastoken", expires);
            _cbs.SendTokenAsync(new FixedTokenProvider(token), new Uri($"amqp://{_info.Host}/"), audience, audience, new[] { "Send", "Listen" }, Timeout).GetAwaiter().GetResult();
        }

        /// <summary>Ids of the messages sent by this instance.</summary>
        public readonly HashSet<string> Sent = new HashSet<string>();

        public void Send(string path, byte[] body, string contentType, IDictionary<string, string> properties = null, string subject = null)
        {
            Authorize(path);
            var settings = new AmqpLinkSettings { LinkName = "send-" + Guid.NewGuid().ToString("N"), Role = false, Source = new Source(), Target = new Target { Address = path }, SettleType = SettleMode.SettleOnSend };
            var link = new SendingAmqpLink(settings);
            link.AttachTo(_session);
            link.Open(Timeout);
            try
            {
                var message = AmqpMessage.Create(new Data { Value = new ArraySegment<byte>(body) });
                var id = Guid.NewGuid().ToString();
                Sent.Add(id);
                message.Properties.MessageId = id;
                message.Properties.ContentType = contentType;
                message.Properties.Subject = subject;
                foreach (var property in properties ?? new Dictionary<string, string>()) message.ApplicationProperties.Map[property.Key] = property.Value;
                link.SendMessageAsync(message, new ArraySegment<byte>(Guid.NewGuid().ToByteArray()), AmqpConstants.NullBinary, Timeout).GetAwaiter().GetResult();
            }
            finally
            {
                link.Close(Timeout);
            }
        }

        /// <summary>Takes the first message sent by this instance with a lock, then settles it with the outcome (older ones are completed).</summary>
        public bool Settle(string path, Func<Outcome> outcome)
        {
            var link = Receiver(path, SettleMode.SettleOnDispose);
            try
            {
                while (true)
                {
                    var message = link.ReceiveMessageAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                    if (message == null) return false;
                    var ours = Sent.Contains(message.Properties?.MessageId?.ToString() ?? string.Empty);
                    link.DisposeMessageAsync(message.DeliveryTag, ours ? outcome() : new Accepted(), false, Timeout).GetAwaiter().GetResult();
                    if (ours) return true;
                }
            }
            finally
            {
                link.Close(Timeout);
            }
        }

        public bool DeadLetter(string path, string reason, string description) => Settle(path, () =>
        {
            var error = new Error { Condition = new AmqpSymbol("com.microsoft:dead-letter"), Info = new Fields() };
            error.Info.Add(new AmqpSymbol("DeadLetterReason"), reason);
            error.Info.Add(new AmqpSymbol("DeadLetterErrorDescription"), description);
            return new Rejected { Error = error };
        });

        /// <summary>A consumer that failed once: the message comes back with a delivery count of 1.</summary>
        public bool Abandon(string path) => Settle(path, () => new Modified { DeliveryFailed = true });

        /// <summary>Removes everything (receive and delete), so each run starts from an empty entity.</summary>
        public int Purge(string path)
        {
            // Lock then complete each message: receive-and-delete leaves some behind on the emulator.
            var link = Receiver(path, SettleMode.SettleOnDispose);
            var count = 0;
            try
            {
                while (true)
                {
                    var message = link.ReceiveMessageAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                    if (message == null) return count;
                    link.DisposeMessageAsync(message.DeliveryTag, new Accepted(), false, Timeout).GetAwaiter().GetResult();
                    message.Dispose();
                    count++;
                }
            }
            finally
            {
                link.Close(Timeout);
            }
        }

        private ReceivingAmqpLink Receiver(string path, SettleMode mode, uint credit = 1)
        {
            Authorize(path);
            var settings = new AmqpLinkSettings
            {
                LinkName = "receive-" + Guid.NewGuid().ToString("N"),
                Role = true,
                Source = new Source { Address = path },
                Target = new Target(),
                SettleType = mode,
                TotalLinkCredit = credit,
                AutoSendFlow = true
            };
            var link = new ReceivingAmqpLink(settings);
            link.AttachTo(_session);
            link.Open(Timeout);
            return link;
        }

        public void Dispose() => _connection.Abort();

        private sealed class FixedTokenProvider : ICbsTokenProvider
        {
            private readonly CbsToken _token;

            public FixedTokenProvider(CbsToken token)
            {
                _token = token;
            }

            public Task<CbsToken> GetTokenAsync(Uri namespaceAddress, string appliesTo, string[] requiredClaims) => Task.FromResult(_token);
        }
    }

    /// <summary>Message reader of the UI tests and the logic tests: answers from memory.</summary>
    internal sealed class FakeMessageReader : IMessageReader
    {
        public readonly Dictionary<string, List<Models.SbMessage>> Queues = new Dictionary<string, List<Models.SbMessage>>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Calls = new List<string>();
        public Exception Error;
        public int Delay;
        public bool Disposed;

        public string Host { get; set; } = "localhost";

        public List<Models.SbMessage> Peek(string path, bool deadLetter, int max, CancellationToken token)
        {
            var address = deadLetter ? path + "/$DeadLetterQueue" : path;
            Calls.Add(address);
            for (var waited = 0; waited < Delay; waited += 50)
            {
                token.ThrowIfCancellationRequested();
                Thread.Sleep(50);
            }
            token.ThrowIfCancellationRequested();
            if (Error != null) throw Error;
            if (!Queues.TryGetValue(address, out var messages)) return new List<Models.SbMessage>();
            var copies = new List<Models.SbMessage>();
            foreach (var message in messages.GetRange(0, Math.Min(max, messages.Count)))
            {
                var copy = new Models.SbMessage
                {
                    SequenceNumber = message.SequenceNumber,
                    MessageId = message.MessageId,
                    ContentType = message.ContentType,
                    EnqueuedOn = message.EnqueuedOn,
                    DeliveryCount = message.DeliveryCount,
                    DeadLetterReason = message.DeadLetterReason,
                    DeadLetterDescription = message.DeadLetterDescription,
                    Body = message.Body
                };
                copy.Properties.AddRange(message.Properties);
                copies.Add(copy);
            }
            return copies;
        }

        public void Add(string address, byte[] body, string contentType, int delivery = 0, string reason = null, string description = null)
        {
            if (!Queues.TryGetValue(address, out var list)) Queues[address] = list = new List<Models.SbMessage>();
            var message = new Models.SbMessage
            {
                SequenceNumber = list.Count + 1,
                MessageId = Guid.NewGuid().ToString(),
                ContentType = contentType,
                EnqueuedOn = DateTime.UtcNow.AddMinutes(-list.Count - 1),
                DeliveryCount = delivery,
                DeadLetterReason = reason,
                DeadLetterDescription = description,
                Body = body
            };
            message.Properties.Add(new KeyValuePair<string, string>("http://schemas.microsoft.com/xrm/2011/Claims/Organization", "contoso.crm4.dynamics.com"));
            list.Add(message);
        }

        public void Dispose() => Disposed = true;
    }

    internal static class Bytes
    {
        public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
    }
}
