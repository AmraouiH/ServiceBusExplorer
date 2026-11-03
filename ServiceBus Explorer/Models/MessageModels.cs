using System;
using System.Collections.Generic;

namespace ServiceBusExplorer.Models
{
    /// <summary>
    /// A message peeked from a queue or a subscription: read without lock, so it stays where it is
    /// and its delivery count does not move.
    /// </summary>
    public class SbMessage
    {
        /// <summary>Queue or subscription it was read from.</summary>
        public SbEntity Source { get; set; }

        /// <summary>Read from the dead-letter sub-queue.</summary>
        public bool DeadLetter { get; set; }

        public long SequenceNumber { get; set; }
        public string MessageId { get; set; }
        public string CorrelationId { get; set; }
        public string Subject { get; set; }
        public string ContentType { get; set; }
        public string To { get; set; }
        public string ReplyTo { get; set; }
        public string SessionId { get; set; }
        public DateTime? EnqueuedOn { get; set; }
        public DateTime? ExpiresOn { get; set; }
        public DateTime? ScheduledFor { get; set; }
        public int DeliveryCount { get; set; }

        public string DeadLetterReason { get; set; }
        public string DeadLetterDescription { get; set; }

        /// <summary>Entity the message was dead-lettered from, when it was forwarded.</summary>
        public string DeadLetterSource { get; set; }

        /// <summary>Application properties (Dataverse sets the organization, the user, the table and the message).</summary>
        public List<KeyValuePair<string, string>> Properties { get; } = new List<KeyValuePair<string, string>>();

        public byte[] Body { get; set; }

        /// <summary>Decoded body, null when it is not a Dataverse execution context.</summary>
        public DataverseContext Context { get; set; }

        /// <summary>Format of the body: JSON, XML, .NETBinary, text or binary.</summary>
        public string BodyFormat { get; set; }

        /// <summary>Readable body (indented JSON or XML, text, or the start of a binary dump).</summary>
        public string BodyText { get; set; }

        /// <summary>Step that sent the message, found from the context, null when unknown.</summary>
        public StepInfo Step { get; set; }

        public string Property(string name)
        {
            foreach (var property in Properties)
            {
                if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
            }
            return null;
        }
    }

    /// <summary>What a Dataverse message says about the operation that produced it (RemoteExecutionContext).</summary>
    public class DataverseContext
    {
        /// <summary>OwningExtension: the sdkmessageprocessingstep that sent the message.</summary>
        public Guid? StepId { get; set; }

        public string StepName { get; set; }
        public string MessageName { get; set; }
        public string Table { get; set; }
        public Guid? RecordId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? InitiatingUserId { get; set; }
        public string Organization { get; set; }
        public Guid? CorrelationId { get; set; }
        public Guid? OperationId { get; set; }
        public int? Stage { get; set; }
        public int? Mode { get; set; }
        public int? Depth { get; set; }
        public DateTime? OperationCreatedOn { get; set; }
    }

    /// <summary>The messages read from one queue or subscription, active or dead-lettered.</summary>
    public class MessageBatch
    {
        public SbEntity Source { get; set; }
        public bool DeadLetter { get; set; }
        public List<SbMessage> Messages { get; set; } = new List<SbMessage>();

        /// <summary>More messages are waiting than the limit read.</summary>
        public bool Truncated { get; set; }

        /// <summary>The entity could not be read: the other batches are still shown.</summary>
        public Exception Error { get; set; }
    }
}
