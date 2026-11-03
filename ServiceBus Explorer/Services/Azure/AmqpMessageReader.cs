using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Amqp;
using Microsoft.Azure.Amqp.Encoding;
using Microsoft.Azure.Amqp.Framing;
using Microsoft.Azure.Amqp.Sasl;
using Microsoft.Azure.Amqp.Transport;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.Models;

namespace ServiceBusExplorer.Services.Azure
{
    /// <summary>
    /// Peeks messages over AMQP, as the Azure SDK does (com.microsoft:peek-message on the $management node of the entity).
    /// A peek takes no lock: the message stays available to its consumers and its delivery count does not change.
    /// Port 5671 is tried first, then AMQP over WebSockets on 443 for the networks that block it.
    /// </summary>
    public sealed class AmqpMessageReader : IMessageReader
    {
        private const int BatchSize = 100;
        private const int EmulatorPort = 5672;
        private const string SasTokenType = "servicebus.windows.net:sastoken";
        private const string JwtTokenType = "jwt";
        private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);

        /// <summary>Receives the raw AMQP errors before they are translated (diagnostics of the tests).</summary>
        internal static Action<Exception> Trace;

        private readonly Func<string, CancellationToken, CbsToken> _tokens;
        private readonly bool _emulator;
        private readonly HashSet<string> _authorized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private AmqpConnection _connection;
        private AmqpSession _session;
        private AmqpCbsLink _cbs;

        private AmqpMessageReader(string host, bool emulator, Func<string, CancellationToken, CbsToken> tokens)
        {
            Host = host;
            _emulator = emulator;
            _tokens = tokens;
        }

        public string Host { get; }

        /// <summary>Shared access key or signature of a connection string: the Listen right is enough.</summary>
        public static AmqpMessageReader WithKey(ServiceBusConnectionInfo info) => new AmqpMessageReader(info.Host, info.IsEmulator, (audience, token) =>
        {
            var expires = DateTime.UtcNow.AddHours(1);
            var value = info.SharedAccessSignature ?? ServiceBusConnectionInfo.CreateToken(audience, info.KeyName, info.Key, expires);
            return new CbsToken(value, SasTokenType, expires);
        });

        /// <summary>Microsoft Entra token: needs the « Azure Service Bus Data Receiver » role (Reader is not enough).</summary>
        public static AmqpMessageReader WithEntra(string host, IArmTokenProvider provider, string tenantId) => new AmqpMessageReader(host, false, (audience, token) =>
            new CbsToken(provider.GetToken(tenantId, TokenResource.ServiceBus, token), JwtTokenType, DateTime.UtcNow.AddMinutes(30)));

        public List<SbMessage> Peek(string path, bool deadLetter, int max, CancellationToken token)
        {
            var address = deadLetter ? path + "/$DeadLetterQueue" : path;
            var messages = new List<SbMessage>();
            try
            {
                PeekAsync(address, deadLetter, max, messages, token).ConfigureAwait(false).GetAwaiter().GetResult();
                return messages;
            }
            catch (Exception error) when (!(error is AzureRequestException) && !(error is OperationCanceledException && token.IsCancellationRequested))
            {
                Trace?.Invoke(error);

                // A broken connection is not reused by the next entity.
                Close();
                throw Translate(error);
            }
        }

        private async Task PeekAsync(string address, bool deadLetter, int max, List<SbMessage> into, CancellationToken token)
        {
            await OpenAsync(token).ConfigureAwait(false);
            await AuthorizeAsync(address, token).ConfigureAwait(false);

            var properties = new Fields();
            properties.Add(new AmqpSymbol("com.microsoft:entity-type"), "entity-mgmt");
            // A unique name: the broker refuses a second link with the name of one it still knows.
            var link = new RequestResponseAmqpLink("entity-mgmt", "mgmt-" + Guid.NewGuid().ToString("N"), _session, address + "/$management", properties);
            try
            {
                using (var timeout = Timeout(token)) await link.OpenAsync(timeout.Token).ConfigureAwait(false);

                long from = 0;
                while (into.Count < max)
                {
                    token.ThrowIfCancellationRequested();
                    var count = Math.Min(BatchSize, max - into.Count);
                    List<SbMessage> batch;
                    using (var request = PeekRequest(from, count))
                    using (var timeout = Timeout(token))
                    using (var response = await link.RequestAsync(request, timeout.Token).ConfigureAwait(false))
                    {
                        batch = ReadResponse(response, deadLetter);
                    }

                    if (batch.Count == 0) break;
                    into.AddRange(batch);
                    var next = batch.Max(m => m.SequenceNumber) + 1;
                    if (next <= from) break;
                    from = next;
                }
            }
            finally
            {
                await CloseLinkAsync(link).ConfigureAwait(false);
            }
        }

        /// <summary>Detaches the link properly so the session stays usable for the next entity.</summary>
        private static async Task CloseLinkAsync(AmqpObject link)
        {
            try
            {
                if (link.State == AmqpObjectState.Opened) await link.CloseAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                else link.Abort();
            }
            catch (Exception)
            {
                link.Abort();
            }
        }

        private async Task OpenAsync(CancellationToken token)
        {
            if (_connection != null && _connection.State == AmqpObjectState.Opened && _session.State == AmqpObjectState.Opened) return;
            Close();

            var settings = new AmqpSettings();
            var sasl = new SaslTransportProvider();
            sasl.Versions.Add(new AmqpVersion(1, 0, 0));
            sasl.AddHandler(new SaslAnonymousHandler("MSSBCBS"));
            var amqp = new AmqpTransportProvider();
            amqp.Versions.Add(new AmqpVersion(1, 0, 0));
            settings.TransportProviders.Add(sasl);
            settings.TransportProviders.Add(amqp);

            var transport = await ConnectAsync(settings, token).ConfigureAwait(false);
            var connection = new AmqpConnection(transport, settings, new AmqpConnectionSettings { ContainerId = Guid.NewGuid().ToString("N"), HostName = Host });
            try
            {
                using (var timeout = Timeout(token)) await connection.OpenAsync(timeout.Token).ConfigureAwait(false);
                var cbs = new AmqpCbsLink(connection);
                var session = connection.CreateSession(new AmqpSessionSettings());
                using (var timeout = Timeout(token)) await session.OpenAsync(timeout.Token).ConfigureAwait(false);
                _connection = connection;
                _cbs = cbs;
                _session = session;
            }
            catch (Exception)
            {
                connection.Abort();
                throw;
            }
        }

        private async Task<TransportBase> ConnectAsync(AmqpSettings settings, CancellationToken token)
        {
            if (_emulator)
            {
                try
                {
                    return await InitiateAsync(settings, new TcpTransportSettings { Host = Host, Port = EmulatorPort }, token).ConfigureAwait(false);
                }
                catch (Exception error) when (!token.IsCancellationRequested)
                {
                    throw new AzureRequestException(0, Strings.AzureUnreachable(Host), $"AMQP {EmulatorPort}: {error.GetBaseException().Message}");
                }
            }

            Exception direct;
            try
            {
                var tcp = new TcpTransportSettings { Host = Host, Port = AmqpConstants.DefaultSecurePort };
                return await InitiateAsync(settings, new TlsTransportSettings(tcp) { TargetHost = Host }, token).ConfigureAwait(false);
            }
            catch (Exception error) when (!token.IsCancellationRequested)
            {
                direct = error;
            }

            try
            {
                var webSocket = new WebSocketTransportSettings { Uri = new Uri($"wss://{Host}/$servicebus/websocket/") };
                return await InitiateAsync(settings, webSocket, token).ConfigureAwait(false);
            }
            catch (Exception error) when (!token.IsCancellationRequested)
            {
                throw new AzureRequestException(0, Strings.AzureUnreachable(Host), $"AMQP 5671: {direct.GetBaseException().Message}{Environment.NewLine}WebSockets 443: {error.GetBaseException().Message}");
            }
        }

        private static async Task<TransportBase> InitiateAsync(AmqpSettings settings, TransportSettings transport, CancellationToken token)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(ConnectTimeout);
                try
                {
                    return await new AmqpTransportInitiator(settings, transport).ConnectAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new TimeoutException("Timeout");
                }
            }
        }

        /// <summary>Claims-based security: one token per entity, sent before its links are opened.</summary>
        private async Task AuthorizeAsync(string address, CancellationToken token)
        {
            if (_authorized.Contains(address)) return;
            var audience = $"{(_emulator ? "amqp" : "amqps")}://{Host}/{address}";
            var namespaceAddress = new Uri($"{(_emulator ? "amqp" : "amqps")}://{Host}/");
            var provider = new TokenProvider(_tokens(audience, token));
            using (var timeout = Timeout(token))
            {
                await _cbs.SendTokenAsync(provider, namespaceAddress, audience, audience, new[] { "Listen" }, timeout.Token).ConfigureAwait(false);
            }
            _authorized.Add(address);
        }

        private static AmqpMessage PeekRequest(long from, int count)
        {
            var body = new AmqpMap();
            body.Add(new MapKey("from-sequence-number"), from);
            body.Add(new MapKey("message-count"), count);
            var request = AmqpMessage.Create(new AmqpValue { Value = body });
            request.ApplicationProperties.Map["operation"] = "com.microsoft:peek-message";
            request.ApplicationProperties.Map["com.microsoft:server-timeout"] = (uint)OperationTimeout.TotalMilliseconds;
            return request;
        }

        private List<SbMessage> ReadResponse(AmqpMessage response, bool deadLetter)
        {
            var status = 0;
            string description = null;
            if (response.ApplicationProperties != null)
            {
                if (response.ApplicationProperties.Map.TryGetValue("statusCode", out object code)) status = System.Convert.ToInt32(code, CultureInfo.InvariantCulture);
                if (response.ApplicationProperties.Map.TryGetValue("statusDescription", out object text)) description = text as string;
            }

            var messages = new List<SbMessage>();
            if (status == 204) return messages;
            if (status != 200) throw StatusError(status, description);

            if (!(Find(response.ValueBody?.Value as AmqpMap, "messages") is IEnumerable<object> entries)) return messages;
            foreach (var entry in entries.OfType<AmqpMap>())
            {
                var payload = Find(entry, "message");
                var bytes = payload is ArraySegment<byte> segment ? segment : payload is byte[] array ? new ArraySegment<byte>(array) : default;
                if (bytes.Array == null) continue;
                using (var message = AmqpMessage.CreateAmqpStreamMessage(new BufferListStream(new[] { bytes }), true))
                {
                    messages.Add(Convert(message, deadLetter));
                }
            }
            return messages;
        }

        private AzureRequestException StatusError(int status, string description)
        {
            switch (status)
            {
                case 401:
                case 403:
                    return new AzureRequestException(status, Strings.MessagesAccessDenied(Host), description);
                case 404:
                    return new AzureRequestException(status, Strings.AzureNotFound, description);
                default:
                    return new AzureRequestException(status, Strings.MessagesFailed, description);
            }
        }

        /// <summary>Map keys come as strings or symbols depending on the broker.</summary>
        private static object Find(AmqpMap map, string key)
        {
            if (map == null) return null;
            foreach (var pair in map)
            {
                var name = pair.Key.Key is AmqpSymbol symbol ? symbol.Value : pair.Key.Key?.ToString();
                if (name == key) return pair.Value;
            }
            return null;
        }

        internal static SbMessage Convert(AmqpMessage message, bool deadLetter)
        {
            var result = new SbMessage { DeadLetter = deadLetter };
            var sections = message.Sections;

            if ((sections & SectionFlag.Header) != 0 && message.Header != null) result.DeliveryCount = (int)(message.Header.DeliveryCount ?? 0);

            if ((sections & SectionFlag.Properties) != 0 && message.Properties != null)
            {
                var p = message.Properties;
                result.MessageId = p.MessageId?.ToString();
                result.CorrelationId = p.CorrelationId?.ToString();
                result.Subject = p.Subject;
                result.ContentType = p.ContentType.Value;
                result.To = p.To?.ToString();
                result.ReplyTo = p.ReplyTo?.ToString();
                result.SessionId = p.GroupId;
                if (p.AbsoluteExpiryTime.HasValue && p.AbsoluteExpiryTime.Value.Year < 9999) result.ExpiresOn = ToUtc(p.AbsoluteExpiryTime.Value);
            }

            if ((sections & SectionFlag.MessageAnnotations) != 0 && message.MessageAnnotations != null)
            {
                foreach (var pair in message.MessageAnnotations.Map)
                {
                    var name = pair.Key.Key is AmqpSymbol symbol ? symbol.Value : pair.Key.Key?.ToString();
                    switch (name)
                    {
                        case "x-opt-sequence-number":
                            result.SequenceNumber = System.Convert.ToInt64(pair.Value, CultureInfo.InvariantCulture);
                            break;
                        case "x-opt-enqueued-time":
                            if (pair.Value is DateTime enqueued) result.EnqueuedOn = ToUtc(enqueued);
                            break;
                        case "x-opt-scheduled-enqueue-time":
                            if (pair.Value is DateTime scheduled && scheduled.Year > 1) result.ScheduledFor = ToUtc(scheduled);
                            break;
                        case "x-opt-deadletter-source":
                            result.DeadLetterSource = pair.Value as string;
                            break;
                    }
                }
            }

            if ((sections & SectionFlag.ApplicationProperties) != 0 && message.ApplicationProperties != null)
            {
                foreach (var pair in message.ApplicationProperties.Map)
                {
                    var name = pair.Key.Key is AmqpSymbol symbol ? symbol.Value : pair.Key.Key?.ToString();
                    var value = ValueText(pair.Value);
                    if (name == "DeadLetterReason") result.DeadLetterReason = value;
                    else if (name == "DeadLetterErrorDescription") result.DeadLetterDescription = value;
                    else result.Properties.Add(new KeyValuePair<string, string>(name, value));
                }
            }

            result.Body = Body(message, sections);
            return result;
        }

        private static byte[] Body(AmqpMessage message, SectionFlag sections)
        {
            if ((sections & SectionFlag.Data) != 0)
            {
                using (var stream = new MemoryStream())
                {
                    foreach (var data in message.DataBody)
                    {
                        if (data.Value is ArraySegment<byte> segment) stream.Write(segment.Array, segment.Offset, segment.Count);
                        else if (data.Value is byte[] bytes) stream.Write(bytes, 0, bytes.Length);
                    }
                    return stream.ToArray();
                }
            }

            if ((sections & SectionFlag.AmqpValue) != 0)
            {
                var value = message.ValueBody?.Value;
                if (value is ArraySegment<byte> segment) return segment.ToArray();
                if (value is byte[] bytes) return bytes;
                return value == null ? new byte[0] : Encoding.UTF8.GetBytes(ValueText(value));
            }

            if ((sections & SectionFlag.AmqpSequence) != 0)
            {
                var items = message.SequenceBody.SelectMany(s => s.List.Cast<object>()).Select(ValueText);
                return Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, items));
            }
            return new byte[0];
        }

        private static string ValueText(object value)
        {
            switch (value)
            {
                case null:
                    return string.Empty;
                case DateTime date:
                    return ToUtc(date).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " UTC";
                case ArraySegment<byte> segment:
                    return System.Convert.ToBase64String(segment.Array, segment.Offset, segment.Count);
                case byte[] bytes:
                    return System.Convert.ToBase64String(bytes);
                case AmqpSymbol symbol:
                    return symbol.Value;
                case IFormattable formattable:
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                default:
                    return value.ToString();
            }
        }

        private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

        private static CancellationTokenSource Timeout(CancellationToken token)
        {
            var source = CancellationTokenSource.CreateLinkedTokenSource(token);
            source.CancelAfter(OperationTimeout);
            return source;
        }

        private Exception Translate(Exception error)
        {
            var amqp = error as AmqpException ?? error.GetBaseException() as AmqpException;
            if (amqp != null)
            {
                var condition = amqp.Error?.Condition.Value;
                if (condition == "amqp:unauthorized-access") return new AzureRequestException(401, Strings.MessagesAccessDenied(Host), amqp.Message);
                if (condition == "amqp:not-found") return new AzureRequestException(404, Strings.AzureNotFound, amqp.Message);
                return new AzureRequestException(0, Strings.MessagesFailed, $"{condition}: {amqp.Message}");
            }
            if (error is OperationCanceledException || error is TimeoutException) return new AzureRequestException(0, Strings.AzureUnreachable(Host), "Timeout");
            return new AzureRequestException(0, Strings.MessagesFailed, error.GetBaseException().Message);
        }

        private void Close()
        {
            _authorized.Clear();
            _cbs?.Close();
            _connection?.Abort();
            _cbs = null;
            _session = null;
            _connection = null;
        }

        public void Dispose() => Close();

        private sealed class TokenProvider : ICbsTokenProvider
        {
            private readonly CbsToken _token;

            public TokenProvider(CbsToken token)
            {
                _token = token;
            }

            public Task<CbsToken> GetTokenAsync(Uri namespaceAddress, string appliesTo, string[] requiredClaims) => Task.FromResult(_token);
        }
    }
}
