using System;
using System.Security.Cryptography;
using System.Text;

namespace ServiceBusExplorer.Helpers
{
    /// <summary>
    /// Encrypts the connection strings for the current Windows user (DPAPI), so the settings file never holds a key in clear.
    /// </summary>
    internal static class SecretStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ServiceBusExplorer.Connection");

        public static string Protect(string secret) =>
            Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser));

        public static string Unprotect(string value) =>
            Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser));
    }
}
