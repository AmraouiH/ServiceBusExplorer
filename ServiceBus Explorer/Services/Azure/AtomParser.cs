using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using ServiceBusExplorer.Models;

namespace ServiceBusExplorer.Services.Azure
{
    /// <summary>
    /// Reads the Atom feeds of the Service Bus management endpoint. Elements are matched by local name:
    /// the children of CountDetails live in another XML namespace than their parent.
    /// </summary>
    public static class AtomParser
    {
        private static readonly XName TypeAttribute = XName.Get("type", "http://www.w3.org/2001/XMLSchema-instance");

        public static List<SbEntity> ParseEntities(string feed, SbKind kind) =>
            Entries(feed).Select(e => ToEntity(e, kind)).Where(e => e != null).ToList();

        public static List<SbRule> ParseRules(string feed) =>
            Entries(feed).Select(ToRule).Where(r => r != null).ToList();

        /// <summary>Number of entries of a feed page, to know when the paging ends.</summary>
        public static int CountEntries(string feed) => Entries(feed).Count();

        private static IEnumerable<XElement> Entries(string feed)
        {
            var root = XDocument.Parse(feed).Root;
            if (root == null) return Enumerable.Empty<XElement>();
            return root.Elements().Where(e => e.Name.LocalName == "entry");
        }

        private static XElement Description(XElement entry, string name) =>
            Child(entry, "content")?.Elements().FirstOrDefault(e => e.Name.LocalName == name);

        private static XElement Child(XElement parent, string name) => parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name);

        private static string Text(XElement parent, string name)
        {
            var value = Child(parent, name)?.Value;
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static long Long(XElement parent, string name) =>
            long.TryParse(Text(parent, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

        private static bool Bool(XElement parent, string name) => string.Equals(Text(parent, name), "true", StringComparison.OrdinalIgnoreCase);

        private static DateTime? Date(XElement parent, string name)
        {
            if (!DateTime.TryParse(Text(parent, name), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value)) return null;
            return value.Year < 1900 ? (DateTime?)null : value;
        }

        private static SbEntity ToEntity(XElement entry, SbKind kind)
        {
            var d = Description(entry, kind == SbKind.Queue ? "QueueDescription" : kind == SbKind.Topic ? "TopicDescription" : "SubscriptionDescription");
            if (d == null) return null;

            var counts = Child(d, "CountDetails");
            var entity = new SbEntity
            {
                Kind = kind,
                Name = Text(entry, "title"),
                Status = Text(d, "Status"),
                TotalMessages = Long(d, "MessageCount"),
                ActiveMessages = Long(counts, "ActiveMessageCount"),
                DeadLetterMessages = Long(counts, "DeadLetterMessageCount"),
                ScheduledMessages = Long(counts, "ScheduledMessageCount"),
                TransferMessages = Long(counts, "TransferMessageCount"),
                SizeBytes = Long(d, "SizeInBytes"),
                MaxSizeMegabytes = Long(d, "MaxSizeInMegabytes"),
                SubscriptionCount = (int)Long(d, "SubscriptionCount"),
                LockDuration = Text(d, "LockDuration"),
                DefaultTimeToLive = Text(d, "DefaultMessageTimeToLive"),
                AutoDeleteOnIdle = Text(d, "AutoDeleteOnIdle"),
                MaxDeliveryCount = (int)Long(d, "MaxDeliveryCount"),
                RequiresSession = Bool(d, "RequiresSession"),
                RequiresDuplicateDetection = Bool(d, "RequiresDuplicateDetection"),
                DeadLetterOnExpiration = Bool(d, "DeadLetteringOnMessageExpiration"),
                Partitioned = Bool(d, "EnablePartitioning"),
                ForwardTo = Text(d, "ForwardTo"),
                ForwardDeadLetteredTo = Text(d, "ForwardDeadLetteredMessagesTo"),
                CreatedOn = Date(d, "CreatedAt"),
                UpdatedOn = Date(d, "UpdatedAt"),
                LastAccessedOn = Date(d, "AccessedAt")
            };
            return entity.Name == null ? null : entity;
        }

        private static SbRule ToRule(XElement entry)
        {
            var d = Description(entry, "RuleDescription");
            if (d == null) return null;

            var filter = Child(d, "Filter");
            var action = Child(d, "Action");
            var rule = new SbRule { Name = Text(d, "Name") ?? Text(entry, "title") };

            switch ((string)filter?.Attribute(TypeAttribute))
            {
                case "CorrelationFilter":
                    rule.FilterKind = RuleFilterKind.Correlation;
                    rule.Expression = Correlation(filter);
                    break;
                case "TrueFilter":
                    rule.FilterKind = RuleFilterKind.True;
                    rule.Expression = "1=1";
                    break;
                case "FalseFilter":
                    rule.FilterKind = RuleFilterKind.False;
                    rule.Expression = "1=0";
                    break;
                default:
                    rule.FilterKind = RuleFilterKind.Sql;
                    rule.Expression = Text(filter, "SqlExpression");
                    break;
            }

            if ((string)action?.Attribute(TypeAttribute) == "SqlRuleAction") rule.ActionExpression = Text(action, "SqlExpression");
            return rule;
        }

        private static string Correlation(XElement filter)
        {
            var text = new StringBuilder();
            foreach (var name in new[] { "CorrelationId", "MessageId", "To", "ReplyTo", "Label", "SessionId", "ReplyToSessionId", "ContentType" })
            {
                var value = Text(filter, name);
                if (value != null) AppendCondition(text, name == "Label" ? "Subject" : name, value);
            }

            var properties = Child(filter, "Properties");
            if (properties != null)
            {
                foreach (var pair in properties.Elements().Where(e => e.Name.LocalName == "KeyValueOfstringanyType"))
                {
                    AppendCondition(text, Text(pair, "Key"), Text(pair, "Value"));
                }
            }
            return text.ToString();
        }

        internal static void AppendCondition(StringBuilder text, string name, string value)
        {
            if (text.Length > 0) text.Append(" AND ");
            text.Append(name).Append(" = '").Append(value).Append('\'');
        }
    }
}
