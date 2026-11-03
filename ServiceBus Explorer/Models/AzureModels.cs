using System;
using System.Collections.Generic;
using System.Linq;

namespace ServiceBusExplorer.Models
{
    public enum SbKind
    {
        Queue,
        Topic,
        Subscription
    }

    public enum RuleFilterKind
    {
        Sql,
        Correlation,
        True,
        False
    }

    /// <summary>A Service Bus namespace, as read from Azure or, when it is a ghost, only known from Dataverse.</summary>
    public class SbNamespace
    {
        public string Host { get; set; }
        public string Location { get; set; }
        public string Sku { get; set; }
        public string Status { get; set; }
        public string ResourceId { get; set; }
        public string SubscriptionName { get; set; }
        public bool ReadWithKey { get; set; }

        /// <summary>Referenced by a Dataverse endpoint but not connected in the tool.</summary>
        public bool IsGhost { get; set; }

        /// <summary>The namespace could not be read.</summary>
        public Exception Error { get; set; }

        public List<SbEntity> Queues { get; set; } = new List<SbEntity>();
        public List<SbEntity> Topics { get; set; } = new List<SbEntity>();

        public string Name => (Host ?? string.Empty).Split('.')[0];

        public IEnumerable<SbEntity> Entities => Queues.Concat(Topics);

        public IEnumerable<SbEntity> AllSubscriptions => Topics.SelectMany(t => t.Subscriptions);
    }

    /// <summary>A queue, a topic or a subscription.</summary>
    public class SbEntity
    {
        public SbKind Kind { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
        public SbNamespace Namespace { get; set; }
        public SbEntity Topic { get; set; }

        /// <summary>Entity missing on Azure (or namespace not connected), only known from a Dataverse endpoint.</summary>
        public bool IsGhost { get; set; }

        public long ActiveMessages { get; set; }
        public long DeadLetterMessages { get; set; }
        public long ScheduledMessages { get; set; }
        public long TransferMessages { get; set; }
        public long TotalMessages { get; set; }
        public long SizeBytes { get; set; }
        public long MaxSizeMegabytes { get; set; }
        public int SubscriptionCount { get; set; }

        public string LockDuration { get; set; }
        public string DefaultTimeToLive { get; set; }
        public string AutoDeleteOnIdle { get; set; }
        public int MaxDeliveryCount { get; set; }
        public bool RequiresSession { get; set; }
        public bool RequiresDuplicateDetection { get; set; }
        public bool DeadLetterOnExpiration { get; set; }
        public bool Partitioned { get; set; }
        public string ForwardTo { get; set; }
        public string ForwardDeadLetteredTo { get; set; }
        public DateTime? CreatedOn { get; set; }
        public DateTime? UpdatedOn { get; set; }
        public DateTime? LastAccessedOn { get; set; }

        public List<SbEntity> Subscriptions { get; } = new List<SbEntity>();
        public List<SbRule> Rules { get; } = new List<SbRule>();

        /// <summary>Dataverse endpoints that send to this entity (filled by the matching).</summary>
        public List<EndpointInfo> Endpoints { get; } = new List<EndpointInfo>();

        public bool IsActive => string.IsNullOrEmpty(Status) || string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase);

        /// <summary>Queue or topic that receives the messages of the steps (a subscription is fed by its topic).</summary>
        public SbEntity Target => Kind == SbKind.Subscription ? Topic : this;

        public IEnumerable<StepInfo> Steps => Target.Endpoints.SelectMany(e => e.Steps);
    }

    /// <summary>A subscription rule: its filter and its optional action.</summary>
    public class SbRule
    {
        public string Name { get; set; }
        public RuleFilterKind FilterKind { get; set; }

        /// <summary>SQL expression, or the conditions of a correlation filter.</summary>
        public string Expression { get; set; }

        public string ActionExpression { get; set; }

        /// <summary>Set by the namespace loader.</summary>
        public SbEntity Subscription { get; set; }
    }

    /// <summary>A namespace found with a Microsoft sign-in.</summary>
    public class DiscoveredNamespace
    {
        public string Host { get; set; }
        public string ResourceId { get; set; }
        public string TenantId { get; set; }
        public string SubscriptionName { get; set; }
        public string ResourceGroup { get; set; }
        public string Location { get; set; }
        public string Sku { get; set; }
    }
}
