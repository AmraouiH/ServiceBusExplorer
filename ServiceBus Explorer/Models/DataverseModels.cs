using System;
using System.Collections.Generic;
using System.Linq;

namespace ServiceBusExplorer.Models
{
    /// <summary>Values of serviceendpoint.contract, read from the metadata of a live organization.</summary>
    public static class EndpointContract
    {
        public const int OneWay = 1;
        public const int Queue = 2;
        public const int Rest = 3;
        public const int TwoWay = 4;
        public const int Topic = 5;
        public const int QueuePersistent = 6;
        public const int EventHub = 7;
        public const int Webhook = 8;
        public const int EventGrid = 9;
        public const int ManagedDataLake = 10;
        public const int ContainerStorage = 11;
    }

    public static class StepStage
    {
        public const int PreValidation = 10;
        public const int PreOperation = 20;
        public const int PostOperation = 40;
    }

    public static class StepMode
    {
        public const int Synchronous = 0;
        public const int Asynchronous = 1;
    }

    public static class ImageKind
    {
        public const int Pre = 0;
        public const int Post = 1;
        public const int Both = 2;
    }

    /// <summary>A row of serviceendpoint.</summary>
    public class EndpointInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Contract { get; set; }

        /// <summary>Lower-case host of the namespace, null when the endpoint has none.</summary>
        public string Host { get; set; }

        /// <summary>Queue or topic name, without the surrounding slashes.</summary>
        public string Path { get; set; }

        public string SasKeyName { get; set; }
        public int AuthType { get; set; }
        public int MessageFormat { get; set; }
        public bool IsManaged { get; set; }

        public List<StepInfo> Steps { get; } = new List<StepInfo>();

        public bool IsServiceBus => Contract == EndpointContract.Queue || Contract == EndpointContract.QueuePersistent || Contract == EndpointContract.Topic;

        public SbKind ExpectedKind => Contract == EndpointContract.Topic ? SbKind.Topic : SbKind.Queue;
    }

    /// <summary>A row of sdkmessageprocessingstep bound to a service endpoint.</summary>
    public class StepInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public Guid EndpointId { get; set; }
        public string EndpointName { get; set; }
        public Guid MessageId { get; set; }
        public string Message { get; set; }
        public Guid? FilterId { get; set; }

        /// <summary>Logical name of the table, null when the step runs for every table.</summary>
        public string Table { get; set; }

        public int Stage { get; set; }
        public int Mode { get; set; }
        public int Rank { get; set; }
        public bool Enabled { get; set; }
        public string FilteringAttributes { get; set; }
        public bool DeleteJobOnSuccess { get; set; }
        public bool IsManaged { get; set; }
        public bool IsCustomizable { get; set; } = true;

        public List<ImageInfo> Images { get; set; } = new List<ImageInfo>();

        /// <summary>Queue or topic name, for the lists.</summary>
        public string TargetPath { get; set; }

        public StepInfo Clone()
        {
            var copy = (StepInfo)MemberwiseClone();
            copy.Images = Images.Select(i => i.Clone()).ToList();
            return copy;
        }
    }

    /// <summary>A row of sdkmessageprocessingstepimage. Id is empty until the image is created.</summary>
    public class ImageInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Alias { get; set; }
        public int ImageType { get; set; }

        /// <summary>Comma-separated columns; empty means every column.</summary>
        public string Attributes { get; set; }

        public string MessageProperty { get; set; }

        public bool AllColumns => string.IsNullOrWhiteSpace(Attributes);

        public ImageInfo Clone() => (ImageInfo)MemberwiseClone();
    }

    /// <summary>A column of a table, for the column picker.</summary>
    public class ColumnInfo
    {
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }
        public string Type { get; set; }
    }

    /// <summary>A step before and after an edition.</summary>
    public class StepChange
    {
        public StepInfo Original { get; set; }
        public StepInfo Draft { get; set; }
    }
}
