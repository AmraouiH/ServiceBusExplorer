using System;
using System.Collections.Generic;
using System.Threading;
using ServiceBusExplorer.Models;

namespace ServiceBusExplorer.Services.Azure
{
    /// <summary>Read-only access to a namespace: the tool never writes to Azure.</summary>
    public interface IServiceBusReader
    {
        string Host { get; }

        void ReadDetails(SbNamespace ns, CancellationToken token);

        List<SbEntity> ReadQueues(CancellationToken token);

        List<SbEntity> ReadTopics(CancellationToken token);

        List<SbEntity> ReadSubscriptions(SbEntity topic, CancellationToken token);

        List<SbRule> ReadRules(SbEntity subscription, CancellationToken token);
    }

    /// <summary>A refused or failed Azure call, with its HTTP status.</summary>
    /// <summary>Reads the messages of a namespace without taking them (peek): nothing is locked, removed or counted as a delivery.</summary>
    public interface IMessageReader : IDisposable
    {
        string Host { get; }

        /// <param name="path">Queue name, or « topic/Subscriptions/subscription ».</param>
        /// <param name="deadLetter">Read the dead-letter sub-queue of the entity.</param>
        /// <param name="max">At most this many messages, oldest first.</param>
        List<SbMessage> Peek(string path, bool deadLetter, int max, CancellationToken token);
    }

    public class AzureRequestException : Exception
    {
        public AzureRequestException(int statusCode, string message, string details = null)
            : base(message)
        {
            StatusCode = statusCode;
            Details = details;
        }

        public int StatusCode { get; }

        public string Details { get; }
    }
}
