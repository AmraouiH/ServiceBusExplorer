using System;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;
using ServiceBusExplorer.Models;

namespace ServiceBusExplorer.Services.Azure
{
    /// <summary>Maps the Azure Resource Manager resources of Microsoft.ServiceBus to the models of the tool.</summary>
    public static class ArmParser
    {
        public static SbEntity ToEntity(JObject resource, SbKind kind)
        {
            var p = resource["properties"] as JObject ?? new JObject();
            var counts = p["countDetails"];
            return new SbEntity
            {
                Kind = kind,
                Name = (string)resource["name"],
                Status = (string)p["status"],
                TotalMessages = (long?)p["messageCount"] ?? 0,
                ActiveMessages = (long?)counts?["activeMessageCount"] ?? 0,
                DeadLetterMessages = (long?)counts?["deadLetterMessageCount"] ?? 0,
                ScheduledMessages = (long?)counts?["scheduledMessageCount"] ?? 0,
                TransferMessages = (long?)counts?["transferMessageCount"] ?? 0,
                SizeBytes = (long?)p["sizeInBytes"] ?? 0,
                MaxSizeMegabytes = (long?)p["maxSizeInMegabytes"] ?? 0,
                SubscriptionCount = (int?)p["subscriptionCount"] ?? 0,
                LockDuration = (string)p["lockDuration"],
                DefaultTimeToLive = (string)p["defaultMessageTimeToLive"],
                AutoDeleteOnIdle = (string)p["autoDeleteOnIdle"],
                MaxDeliveryCount = (int?)p["maxDeliveryCount"] ?? 0,
                RequiresSession = (bool?)p["requiresSession"] ?? false,
                RequiresDuplicateDetection = (bool?)p["requiresDuplicateDetection"] ?? false,
                DeadLetterOnExpiration = (bool?)p["deadLetteringOnMessageExpiration"] ?? false,
                Partitioned = (bool?)p["enablePartitioning"] ?? false,
                ForwardTo = NullIfEmpty((string)p["forwardTo"]),
                ForwardDeadLetteredTo = NullIfEmpty((string)p["forwardDeadLetteredMessagesTo"]),
                CreatedOn = Date(p["createdAt"]),
                UpdatedOn = Date(p["updatedAt"]),
                LastAccessedOn = Date(p["accessedAt"])
            };
        }

        public static SbRule ToRule(JObject resource)
        {
            var p = resource["properties"] as JObject ?? new JObject();
            var rule = new SbRule { Name = (string)resource["name"], ActionExpression = NullIfEmpty((string)p.SelectToken("action.sqlExpression")) };

            if (string.Equals((string)p["filterType"], "CorrelationFilter", StringComparison.OrdinalIgnoreCase))
            {
                rule.FilterKind = RuleFilterKind.Correlation;
                rule.Expression = Correlation(p["correlationFilter"]);
            }
            else
            {
                rule.Expression = (string)p.SelectToken("sqlFilter.sqlExpression");
                rule.FilterKind = rule.Expression == "1=1" ? RuleFilterKind.True : rule.Expression == "1=0" ? RuleFilterKind.False : RuleFilterKind.Sql;
            }
            return rule;
        }

        public static void FillNamespace(SbNamespace ns, JObject resource)
        {
            ns.Location = (string)resource["location"];
            ns.Sku = (string)resource.SelectToken("sku.name");
            ns.Status = (string)resource.SelectToken("properties.status");
            ns.ResourceId = (string)resource["id"];
        }

        public static string HostOf(JObject resource)
        {
            var endpoint = (string)resource.SelectToken("properties.serviceBusEndpoint");
            return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : null;
        }

        /// <summary>Segment of a resource id, for example « resourceGroups ».</summary>
        public static string Segment(string resourceId, string name)
        {
            var parts = (resourceId ?? string.Empty).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var index = Array.FindIndex(parts, p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < parts.Length ? parts[index + 1] : null;
        }

        private static string Correlation(JToken filter)
        {
            var text = new StringBuilder();
            if (filter == null) return string.Empty;
            foreach (var field in new[] { "correlationId", "messageId", "to", "replyTo", "label", "sessionId", "replyToSessionId", "contentType" })
            {
                var value = NullIfEmpty((string)filter[field]);
                if (value != null) AtomParser.AppendCondition(text, field == "label" ? "Subject" : char.ToUpperInvariant(field[0]) + field.Substring(1), value);
            }

            if (filter["properties"] is JObject properties)
            {
                foreach (var property in properties.Properties()) AtomParser.AppendCondition(text, property.Name, property.Value.ToString());
            }
            return text.ToString();
        }

        private static DateTime? Date(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null) return null;
            DateTime? date = null;
            if (value.Type == JTokenType.Date) date = ((DateTime)value).ToUniversalTime();
            else if (DateTime.TryParse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)) date = parsed;
            return date?.Year < 1900 ? null : date;
        }

        private static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
    }
}
