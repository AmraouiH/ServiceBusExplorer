using System.Collections.Generic;

namespace ServiceBusExplorer
{
    /// <summary>How the tool reads a namespace.</summary>
    public enum ConnectionMethod
    {
        /// <summary>Shared access key of a connection string (data plane, Manage right needed).</summary>
        ConnectionString,

        /// <summary>Microsoft Entra sign-in in the browser (Azure Resource Manager, Reader role is enough).</summary>
        Browser,

        /// <summary>Token of the local Azure CLI (Azure Resource Manager, Reader role is enough).</summary>
        AzureCli
    }

    /// <summary>A namespace added by the user.</summary>
    public class SavedConnection
    {
        public string Host { get; set; }

        public ConnectionMethod Method { get; set; }

        /// <summary>Connection string encrypted for the current Windows user (DPAPI).</summary>
        public string ProtectedSecret { get; set; }

        /// <summary>Azure resource id of the namespace (Microsoft sign-in).</summary>
        public string ResourceId { get; set; }

        public string TenantId { get; set; }

        public string SubscriptionName { get; set; }
    }

    /// <summary>
    /// Préférences du plugin, persistées par le SettingsManager de XrmToolBox (sérialisation XML).
    /// </summary>
    public class PluginSettings
    {
        /// <summary>Langue de l'interface : « en » (défaut) ou « fr ».</summary>
        public string Language { get; set; } = "en";

        /// <summary>Requêtes Azure envoyées en parallèle pour lire les souscriptions et les filtres (1 à 16).</summary>
        public int ParallelRequests { get; set; } = 6;

        public const int MaxMessagesLimit = 5000;

        /// <summary>Messages lus (peek) par queue ou souscription, et par sous-file active / dead-letter (1 à 5000).</summary>
        public int MaxMessages { get; set; } = 100;

        /// <summary>Tenant Microsoft Entra de la connexion navigateur (vide = tenant du compte).</summary>
        public string TenantId { get; set; }

        /// <summary>Application Entra de la connexion navigateur (vide = client public par défaut).</summary>
        public string ClientId { get; set; }

        public List<SavedConnection> Connections { get; set; } = new List<SavedConnection>();
    }
}
