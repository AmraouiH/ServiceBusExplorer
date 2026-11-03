using System;
using System.Security.Cryptography;
using ServiceBusExplorer.Helpers;
using ServiceBusExplorer.Localization;

namespace ServiceBusExplorer.Services.Azure
{
    /// <summary>Builds the readers of a saved connection.</summary>
    public static class ConnectionFactory
    {
        /// <summary>Builds the message reader (replaced by the tests).</summary>
        internal static Func<SavedConnection, PluginSettings, IMessageReader> MessageReaderFactory = CreateAmqpReader;

        public static IServiceBusReader CreateReader(SavedConnection connection, PluginSettings settings)
        {
            switch (connection.Method)
            {
                case ConnectionMethod.ConnectionString:
                    return new SasServiceBusReader(ServiceBusConnectionInfo.Parse(Secret(connection)));

                case ConnectionMethod.AzureCli:
                    return new ArmServiceBusReader(new AzureCliTokenProvider(), connection.Host, connection.ResourceId, connection.TenantId);

                default:
                    return new ArmServiceBusReader(new BrowserTokenProvider(settings.ClientId, settings.TenantId), connection.Host, connection.ResourceId, connection.TenantId);
            }
        }

        /// <summary>Reader of the messages, with the same credentials as the namespace.</summary>
        public static IMessageReader CreateMessageReader(SavedConnection connection, PluginSettings settings) => MessageReaderFactory(connection, settings);

        private static IMessageReader CreateAmqpReader(SavedConnection connection, PluginSettings settings)
        {
            switch (connection.Method)
            {
                case ConnectionMethod.ConnectionString:
                    return AmqpMessageReader.WithKey(ServiceBusConnectionInfo.Parse(Secret(connection)));

                case ConnectionMethod.AzureCli:
                    return AmqpMessageReader.WithEntra(connection.Host, new AzureCliTokenProvider(), connection.TenantId);

                default:
                    return AmqpMessageReader.WithEntra(connection.Host, new BrowserTokenProvider(settings.ClientId, settings.TenantId), connection.TenantId);
            }
        }

        private static string Secret(SavedConnection connection)
        {
            try
            {
                return SecretStore.Unprotect(connection.ProtectedSecret);
            }
            catch (CryptographicException)
            {
                // Settings copied from another Windows account or computer.
                throw new AzureRequestException(0, Strings.SecretUnreadable);
            }
        }
    }
}
