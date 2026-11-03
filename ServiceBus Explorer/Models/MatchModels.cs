using System.Collections.Generic;
using System.Linq;

namespace ServiceBusExplorer.Models
{
    public enum MatchStatus
    {
        /// <summary>The endpoint targets an existing entity and at least one step uses it.</summary>
        Linked,

        /// <summary>The endpoint targets an existing entity but no step uses it.</summary>
        NoSteps,

        /// <summary>The namespace is connected but the entity does not exist.</summary>
        MissingOnAzure,

        /// <summary>The entity exists with another type than the endpoint (queue against topic).</summary>
        TypeMismatch,

        /// <summary>The namespace of the endpoint is not connected (or could not be read).</summary>
        NotConnected,

        /// <summary>Azure entity that no Dataverse endpoint targets.</summary>
        NoEndpoint,

        /// <summary>Relay, webhook, event hub...: not a queue or a topic.</summary>
        OtherType
    }

    /// <summary>A line of the matching page.</summary>
    public class MatchRow
    {
        public MatchStatus Status { get; set; }
        public EndpointInfo Endpoint { get; set; }
        public SbEntity Entity { get; set; }
        public string Host { get; set; }
        public string Path { get; set; }

        public int StepCount => Endpoint?.Steps.Count ?? 0;

        public int EnabledStepCount => Endpoint?.Steps.Count(s => s.Enabled) ?? 0;

        public int ImageCount => Endpoint?.Steps.Sum(s => s.Images.Count) ?? 0;
    }

    /// <summary>Result of a load: what the three pages display.</summary>
    public class ExplorerModel
    {
        public List<SbNamespace> Namespaces { get; set; } = new List<SbNamespace>();
        public List<EndpointInfo> Endpoints { get; set; } = new List<EndpointInfo>();
        public List<StepInfo> Steps { get; set; } = new List<StepInfo>();
        public List<MatchRow> Rows { get; set; } = new List<MatchRow>();
    }
}
