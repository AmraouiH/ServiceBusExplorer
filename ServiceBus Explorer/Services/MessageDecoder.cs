using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ServiceBusExplorer.Models;

namespace ServiceBusExplorer.Services
{
    /// <summary>
    /// Reads the body of a message. Dataverse sends a RemoteExecutionContext in the format of its endpoint:
    /// JSON (application/json), XML (application/xml) or .NETBinary (binary XML of the DataContractSerializer, application/msbin1).
    /// Its OwningExtension is the step that sent the message.
    /// </summary>
    public static class MessageDecoder
    {
        public const string FormatJson = "JSON";
        public const string FormatXml = "XML";
        public const string FormatBinary = ".NETBinary";
        public const string FormatText = "Text";
        public const string FormatRaw = "Binary";

        private const int MaxTextLength = 2000000;
        private const int HexDumpBytes = 4096;
        private static readonly Regex JsonDate = new Regex(@"^/Date\((-?\d+)([+-]\d{4})?\)/$", RegexOptions.Compiled);

        public static void Decode(SbMessage message)
        {
            var body = message.Body ?? new byte[0];
            message.Context = null;
            message.BodyFormat = null;
            message.BodyText = string.Empty;
            if (body.Length == 0) return;

            var text = Utf8(body);
            var start = text?.TrimStart().FirstOrDefault() ?? '\0';
            var binaryFirst = (message.ContentType ?? string.Empty).IndexOf("msbin", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!binaryFirst && (start == '{' || start == '[') && TryJson(message, text)) return;
            if (!binaryFirst && start == '<' && TryXml(message, text)) return;
            if (TryBinaryXml(message, body)) return;
            if (text != null && IsReadable(text))
            {
                message.BodyFormat = FormatText;
                message.BodyText = Cut(text);
                return;
            }

            message.BodyFormat = FormatRaw;
            message.BodyText = HexDump(body);
        }

        #region Formats

        private static bool TryJson(SbMessage message, string text)
        {
            JToken token;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None })
                {
                    token = JToken.ReadFrom(reader);
                }
            }
            catch (JsonException)
            {
                return false;
            }

            message.BodyFormat = FormatJson;
            message.BodyText = Cut(token.ToString(Newtonsoft.Json.Formatting.Indented));
            if (token is JObject json) message.Context = FromJson(json);
            return true;
        }

        private static bool TryXml(SbMessage message, string text)
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(text);
            }
            catch (XmlException)
            {
                return false;
            }

            message.BodyFormat = FormatXml;
            message.BodyText = Cut(document.ToString());
            message.Context = FromXml(document.Root);
            return true;
        }

        private static bool TryBinaryXml(SbMessage message, byte[] body)
        {
            XDocument document;
            try
            {
                using (var reader = XmlDictionaryReader.CreateBinaryReader(body, 0, body.Length, XmlDictionaryReaderQuotas.Max))
                {
                    document = XDocument.Load(reader);
                }
            }
            catch (Exception error) when (error is XmlException || error is InvalidOperationException || error is FormatException || error is ArgumentException)
            {
                return false;
            }

            if (document.Root == null) return false;
            message.BodyFormat = FormatBinary;
            message.BodyText = Cut(document.ToString());
            message.Context = FromXml(document.Root);
            return true;
        }

        #endregion

        #region Execution context

        private static DataverseContext FromJson(JObject json)
        {
            string Text(JToken token) => token == null || token.Type == JTokenType.Null ? null : token.Type == JTokenType.String ? (string)token : token.ToString(Newtonsoft.Json.Formatting.None);
            JToken Value(string name) => json.Properties().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

            var owner = Value("OwningExtension") as JObject;
            var context = new DataverseContext
            {
                StepId = ToGuid(Text(owner?["Id"])),
                StepName = Text(owner?["Name"]),
                MessageName = Text(Value("MessageName")),
                Table = Text(Value("PrimaryEntityName")),
                RecordId = ToGuid(Text(Value("PrimaryEntityId"))),
                UserId = ToGuid(Text(Value("UserId"))),
                InitiatingUserId = ToGuid(Text(Value("InitiatingUserId"))),
                Organization = Text(Value("OrganizationName")),
                CorrelationId = ToGuid(Text(Value("CorrelationId"))),
                OperationId = ToGuid(Text(Value("OperationId"))),
                Stage = ToInt(Text(Value("Stage"))),
                Mode = ToInt(Text(Value("Mode"))),
                Depth = ToInt(Text(Value("Depth"))),
                OperationCreatedOn = ToDate(Text(Value("OperationCreatedOn")))
            };
            return IsContext(context) ? context : null;
        }

        private static DataverseContext FromXml(XElement root)
        {
            if (root == null) return null;
            XElement Child(XElement parent, string name) => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name);
            string Text(XElement element) => element == null || IsNil(element) ? null : element.Value;

            var owner = Child(root, "OwningExtension");
            var context = new DataverseContext
            {
                StepId = ToGuid(Text(Child(owner, "Id"))),
                StepName = Text(Child(owner, "Name")),
                MessageName = Text(Child(root, "MessageName")),
                Table = Text(Child(root, "PrimaryEntityName")),
                RecordId = ToGuid(Text(Child(root, "PrimaryEntityId"))),
                UserId = ToGuid(Text(Child(root, "UserId"))),
                InitiatingUserId = ToGuid(Text(Child(root, "InitiatingUserId"))),
                Organization = Text(Child(root, "OrganizationName")),
                CorrelationId = ToGuid(Text(Child(root, "CorrelationId"))),
                OperationId = ToGuid(Text(Child(root, "OperationId"))),
                Stage = ToInt(Text(Child(root, "Stage"))),
                Mode = ToInt(Text(Child(root, "Mode"))),
                Depth = ToInt(Text(Child(root, "Depth"))),
                OperationCreatedOn = ToDate(Text(Child(root, "OperationCreatedOn")))
            };
            return IsContext(context) ? context : null;
        }

        private static bool IsNil(XElement element) =>
            element.Attributes().Any(a => a.Name.LocalName == "nil" && string.Equals(a.Value, "true", StringComparison.OrdinalIgnoreCase));

        /// <summary>Any JSON or XML is not an execution context: it needs at least its step or its message.</summary>
        private static bool IsContext(DataverseContext context) => context.StepId.HasValue || !string.IsNullOrEmpty(context.MessageName);

        private static Guid? ToGuid(string value) => Guid.TryParse(value, out var id) && id != Guid.Empty ? id : (Guid?)null;

        private static int? ToInt(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : (int?)null;

        /// <summary>ISO 8601 (XML), or « /Date(milliseconds+offset)/ » (DataContractJsonSerializer).</summary>
        private static DateTime? ToDate(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var json = JsonDate.Match(value);
            if (json.Success) return DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(json.Groups[1].Value, CultureInfo.InvariantCulture)).UtcDateTime;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date) ? date : (DateTime?)null;
        }

        #endregion

        #region Text

        private static string Utf8(byte[] body)
        {
            try
            {
                var text = new UTF8Encoding(false, true).GetString(body);
                return text.Length > 0 && text[0] == '﻿' ? text.Substring(1) : text;
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }

        private static bool IsReadable(string text)
        {
            var sample = text.Length > 2000 ? text.Substring(0, 2000) : text;
            var control = sample.Count(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t');
            return control * 20 < Math.Max(1, sample.Length);
        }

        private static string Cut(string text) => text.Length <= MaxTextLength ? text : text.Substring(0, MaxTextLength) + Environment.NewLine + "…";

        private static string HexDump(byte[] body)
        {
            var builder = new StringBuilder();
            var length = Math.Min(body.Length, HexDumpBytes);
            for (var offset = 0; offset < length; offset += 16)
            {
                builder.Append(offset.ToString("X6", CultureInfo.InvariantCulture)).Append("  ");
                for (var i = 0; i < 16; i++) builder.Append(offset + i < length ? body[offset + i].ToString("X2", CultureInfo.InvariantCulture) + " " : "   ");
                builder.Append(' ');
                for (var i = 0; i < 16 && offset + i < length; i++)
                {
                    var b = body[offset + i];
                    builder.Append(b >= 32 && b < 127 ? (char)b : '.');
                }
                builder.AppendLine();
            }
            if (body.Length > length) builder.Append("… ").Append(body.Length.ToString("N0", CultureInfo.InvariantCulture)).Append(" bytes");
            return builder.ToString();
        }

        #endregion
    }
}
