using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ServiceBusExplorer.Localization;

namespace ServiceBusExplorer.Services.Azure
{
    /// <summary>
    /// A parsed Service Bus connection string, and the SAS token that authenticates the management calls.
    /// </summary>
    public sealed class ServiceBusConnectionInfo
    {
        private const int EmulatorManagementPort = 5300;
        private const int TokenMinutes = 60;

        public string Host { get; private set; }
        public string KeyName { get; private set; }
        public string Key { get; private set; }
        public string SharedAccessSignature { get; private set; }
        public string EntityPath { get; private set; }
        public Uri ManagementUri { get; private set; }

        /// <summary>Local Service Bus emulator: plain HTTP and AMQP, no TLS.</summary>
        public bool IsEmulator { get; private set; }

        public static ServiceBusConnectionInfo Parse(string connectionString)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in (connectionString ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var index = part.IndexOf('=');
                if (index > 0) values[part.Substring(0, index).Trim()] = part.Substring(index + 1).Trim();
            }

            if (!values.TryGetValue("Endpoint", out var endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            {
                throw new FormatException(Strings.InvalidConnectionString);
            }

            values.TryGetValue("SharedAccessKeyName", out var keyName);
            values.TryGetValue("SharedAccessKey", out var key);
            values.TryGetValue("SharedAccessSignature", out var signature);
            values.TryGetValue("EntityPath", out var entityPath);
            values.TryGetValue("UseDevelopmentEmulator", out var emulator);
            var isEmulator = string.Equals(emulator, "true", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(signature) && (string.IsNullOrEmpty(keyName) || string.IsNullOrEmpty(key)))
            {
                throw new FormatException(Strings.ConnectionStringNoKey);
            }

            var builder = new UriBuilder(isEmulator ? "http" : "https", uri.Host);
            if (isEmulator) builder.Port = uri.IsDefaultPort || uri.Port <= 0 ? EmulatorManagementPort : uri.Port;

            return new ServiceBusConnectionInfo
            {
                Host = uri.Host.ToLowerInvariant(),
                KeyName = keyName,
                Key = key,
                SharedAccessSignature = signature,
                EntityPath = entityPath,
                ManagementUri = builder.Uri,
                IsEmulator = isEmulator
            };
        }

        /// <summary>Value of the Authorization header, valid one hour.</summary>
        public string CreateAuthorization() =>
            SharedAccessSignature ?? CreateToken(ManagementUri.AbsoluteUri, KeyName, Key, DateTimeOffset.UtcNow.AddMinutes(TokenMinutes));

        public static string CreateToken(string resource, string keyName, string key, DateTimeOffset expiry)
        {
            var encoded = Uri.EscapeDataString(resource);
            var seconds = expiry.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key)))
            {
                var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(encoded + "\n" + seconds)));
                return $"SharedAccessSignature sr={encoded}&sig={Uri.EscapeDataString(signature)}&se={seconds}&skn={Uri.EscapeDataString(keyName)}";
            }
        }
    }
}
