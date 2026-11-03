using System.ComponentModel.Composition;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace ServiceBusExplorer
{
    /// <summary>
    /// Point d'entrée MEF du plugin « ServiceBus Explorer » pour XrmToolBox.
    /// </summary>
    [Export(typeof(IXrmToolBoxPlugin)),
        ExportMetadata("Name", "ServiceBus Explorer"),
        ExportMetadata("Description", "Explore Azure Service Bus queues, topics, subscriptions and filters, match them with the Dataverse service endpoints, edit the triggers and images of their steps, and peek the active messages each step sent. English / French."),
        // "AH" icon of the author's tools, 32x32
        ExportMetadata("SmallImageBase64", PluginIcons.Small),
        // "AH" icon, 80x80
        ExportMetadata("BigImageBase64", PluginIcons.Big),
        // Same tile as Entity Fields Analyser
        ExportMetadata("BackgroundColor", "orange"),
        ExportMetadata("PrimaryFontColor", "white"),
        ExportMetadata("SecondaryFontColor", "Black")]
    public class ServiceBusExplorerPlugin : PluginBase, IPayPalPlugin
    {
        /// <summary>Donation through the XrmToolBox PayPal button (same account as Entity Fields Analyser).</summary>
        public string DonationDescription => "ServiceBus Explorer";

        public string EmailAccount => "hamzamraoui11@gmail.com";

        public override IXrmToolBoxPluginControl GetControl()
        {
            return new ServiceBusExplorerControl();
        }
    }
}
