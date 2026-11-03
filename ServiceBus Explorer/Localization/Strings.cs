using System;
using ServiceBusExplorer.Models;

namespace ServiceBusExplorer.Localization
{
    public enum Language
    {
        English,
        French
    }

    /// <summary>
    /// Every user-facing text of the tool, in English and French.
    /// Changing <see cref="Current"/> raises <see cref="LanguageChanged"/> so open screens can refresh.
    /// </summary>
    public static class Strings
    {
        private static Language _current = Language.English;

        public static event EventHandler LanguageChanged;

        public static Language Current
        {
            get => _current;
            set
            {
                if (_current == value) return;
                _current = value;
                LanguageChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static bool IsFrench => _current == Language.French;

        public static string ToCode(Language language) => language == Language.French ? "fr" : "en";

        public static Language FromCode(string code) =>
            string.Equals(code, "fr", StringComparison.OrdinalIgnoreCase) ? Language.French : Language.English;

        private static string T(string english, string french) => IsFrench ? french : english;

        public static string Yes => T("Yes", "Oui");
        public static string No => T("No", "Non");
        public static string EmptyValue => T("(empty)", "(vide)");

        #region Header

        public const string AppTitle = "ServiceBus Explorer";
        public const string EnglishName = "English";
        public const string FrenchName = "Français";
        public const string AuthorName = "Hamza AMRAOUI";

        public static string Close => T("Close", "Fermer");
        public static string CloseTip => T("Close the tool", "Fermer l'outil");
        public static string Load => T("Load", "Charger");
        public static string LoadTip => T("Read the namespaces on Azure and the service endpoints, steps and images of Dataverse", "Lit les namespaces sur Azure et les service endpoints, steps et images de Dataverse");
        public static string Connections => T("Azure connections", "Connexions Azure");
        public static string ConnectionsTip => T("Add or remove the Service Bus namespaces read by the tool", "Ajouter ou retirer les namespaces Service Bus lus par l'outil");
        public static string Export => T("Export", "Exporter");
        public static string ExportTip => T("Excel report: matching, steps, images, Azure entities and filters", "Rapport Excel : matching, steps, images, entités Azure et filtres");
        public static string Options => T("Options", "Options");
        public static string LanguageMenu => T("Language", "Langue");
        public static string Donate => T("Donate", "Faire un don");
        public static string DonateTip => T("Support the tool (PayPal)", "Soutenir l'outil (PayPal)");
        public static string Help => T("Help", "Aide");
        public static string HelpTip => T("How to use the tool", "Comment utiliser l'outil");
        public static string HelpSubtitle => T("Explore Azure Service Bus and manage the Dataverse steps that send to it", "Explorer Azure Service Bus et gérer les steps Dataverse qui lui envoient des messages");
        public static string AuthorTip => T("Developed by Hamza AMRAOUI (LinkedIn)", "Développé par Hamza AMRAOUI (LinkedIn)");

        public static string HelpText => T(
            "1. AZURE CONNECTIONS\r\n" +
            "Add your namespaces with one of the two methods:\r\n" +
            "  - Connection string: needs a namespace-level key with the Manage right. It is encrypted for your Windows account.\r\n" +
            "  - Microsoft account: browser sign-in or Azure CLI. The Reader role on the namespace is enough, no key is needed.\r\n" +
            "Reading the messages needs a little more: the Listen right for a key (Manage includes it), or the « Azure Service Bus Data Receiver » role for a Microsoft account.\r\n\r\n" +
            "2. LOAD\r\n" +
            "Nothing is read until you click Load. The tool reads the namespaces on Azure, then the Dataverse service endpoints, their steps and their images. " +
            "Without any Azure connection the steps are still displayed: the namespaces used by Dataverse appear greyed out.\r\n\r\n" +
            "3. EXPLORER\r\n" +
            "The tree shows the queues, topics, subscriptions and rules with their message counts. Select an item to see the Dataverse steps that send to it, " +
            "the subscriptions and filters, and the properties. A subscription shows the steps of its topic, because they feed it.\r\n\r\n" +
            "4. MATCHING\r\n" +
            "Every endpoint and every Azure entity, with a status:\r\n" +
            "  - Linked: the endpoint targets an existing entity and has steps.\r\n" +
            "  - No steps: the endpoint exists, but nothing uses it.\r\n" +
            "  - Missing on Azure: the namespace is connected but the queue or topic does not exist.\r\n" +
            "  - Wrong type: for example, an endpoint of type Topic pointing at a queue.\r\n" +
            "  - Not connected: the namespace is not read by the tool.\r\n" +
            "  - No endpoint: an Azure entity that nothing in Dataverse targets.\r\n\r\n" +
            "5. STEPS\r\n" +
            "Edit opens the trigger (message, table, stage, mode, rank, filtering columns) and the images. Every change is reviewed in a summary before it is written. " +
            "Steps can also be enabled and disabled in bulk. Managed steps that are not customizable are read-only.\r\n\r\n" +
            "6. MESSAGES\r\n" +
            "The Messages tab of a queue, topic or subscription, and the Messages button of the steps, peek the active messages waiting (the dead-letter queue is not read). " +
            "Each message is linked to the step that sent it (the OwningExtension of its Dataverse context), whatever the format of the endpoint: JSON, XML or .NETBinary. " +
            "A peek takes nothing: the messages are not locked, not removed, and their delivery count does not change. A message already taken by a consumer is no longer visible. " +
            "A topic keeps no message: its subscriptions do, each with its own copy. The number read per queue is set in the options.\r\n\r\n" +
            "The tool only reads Azure: it never changes a queue, a topic, a filter or a message. It only writes the steps and images in Dataverse.",
            "1. CONNEXIONS AZURE\r\n" +
            "Ajoutez vos namespaces avec l'une des deux méthodes :\r\n" +
            "  - Connection string : il faut une clé au niveau du namespace avec le droit Manage. Elle est chiffrée pour votre compte Windows.\r\n" +
            "  - Compte Microsoft : connexion navigateur ou Azure CLI. Le rôle Reader sur le namespace suffit, aucune clé n'est nécessaire.\r\n" +
            "Lire les messages demande un peu plus : le droit Listen pour une clé (Manage l'inclut), ou le rôle « Azure Service Bus Data Receiver » pour un compte Microsoft.\r\n\r\n" +
            "2. CHARGER\r\n" +
            "Rien n'est lu tant que vous ne cliquez pas sur Charger. L'outil lit les namespaces sur Azure, puis les service endpoints Dataverse, leurs steps et leurs images. " +
            "Sans connexion Azure, les steps restent affichés : les namespaces utilisés par Dataverse apparaissent grisés.\r\n\r\n" +
            "3. EXPLORATEUR\r\n" +
            "L'arbre montre les queues, topics, souscriptions et règles avec leurs compteurs de messages. Sélectionnez un élément pour voir les steps Dataverse qui lui envoient des messages, " +
            "les souscriptions et filtres, et les propriétés. Une souscription affiche les steps de son topic, puisque ce sont eux qui l'alimentent.\r\n\r\n" +
            "4. MATCHING\r\n" +
            "Chaque endpoint et chaque entité Azure, avec un statut :\r\n" +
            "  - Lié : l'endpoint cible une entité existante et possède des steps.\r\n" +
            "  - Sans step : l'endpoint existe mais rien ne l'utilise.\r\n" +
            "  - Absent d'Azure : le namespace est connecté mais la queue ou le topic n'existe pas.\r\n" +
            "  - Mauvais type : par exemple un endpoint de type Topic qui pointe vers une queue.\r\n" +
            "  - Non connecté : le namespace n'est pas lu par l'outil.\r\n" +
            "  - Sans endpoint : une entité Azure qu'aucun endpoint Dataverse ne cible.\r\n\r\n" +
            "5. STEPS\r\n" +
            "Modifier ouvre le trigger (message, table, stage, mode, rang, colonnes de filtrage) et les images. Chaque changement est récapitulé avant d'être écrit. " +
            "Les steps peuvent aussi être activés et désactivés en masse. Les steps managés non personnalisables sont en lecture seule.\r\n\r\n" +
            "6. MESSAGES\r\n" +
            "L'onglet Messages d'une queue, d'un topic ou d'une souscription, et le bouton Messages des steps, lisent les messages actifs en attente (la dead-letter queue n'est pas lue). " +
            "Chaque message est rattaché au step qui l'a envoyé (l'OwningExtension de son contexte Dataverse), quel que soit le format de l'endpoint : JSON, XML ou .NETBinary. " +
            "La lecture ne prend rien : les messages ne sont ni verrouillés ni retirés, et leur nombre de livraisons ne change pas. Un message déjà pris par un consommateur n'est plus visible. " +
            "Un topic ne garde aucun message : ce sont ses souscriptions qui les gardent, chacune avec sa copie. Le nombre lu par file se règle dans les options.\r\n\r\n" +
            "L'outil ne fait que lire Azure : il ne modifie jamais une queue, un topic, un filtre ou un message. Il n'écrit que les steps et les images dans Dataverse.");

        #endregion

        #region Dialogs shared with the other tools

        public static string ShowDetails => T("Show details", "Afficher les détails");
        public static string HideDetails => T("Hide details", "Masquer les détails");
        public static string ExpandDetails => T("Open in a large window", "Ouvrir dans une grande fenêtre");
        public static string DetailsViewerTitle => T("Technical details", "Détails techniques");
        public static string CopyDetails => T("Copy details", "Copier les détails");
        public static string Copied => T("Copied", "Copié");
        public static string WordWrap => T("Word wrap", "Retour à la ligne");
        public static string SomethingWentWrong => T("Something went wrong", "Une erreur est survenue");
        public static string MissingPrivileges => T("Missing privileges", "Privilèges manquants");
        public static string PrivilegeIntro => T("Your security role does not allow this operation.", "Votre rôle de sécurité n'autorise pas cette opération.");
        public static string MissingPrivilege(string name) => T($"Missing privilege: {name}", $"Privilège manquant : {name}");
        public static string PrivilegeSearch => T("Reading needs Read access to Service Endpoint, Sdk Message Processing Step and Sdk Message Processing Step Image.", "La lecture nécessite le droit Lecture sur Service Endpoint, Sdk Message Processing Step et Sdk Message Processing Step Image.");
        public static string PrivilegeRestore => T("Editing needs Write access to the steps, and Create / Delete access to the step images.", "La modification nécessite le droit Écriture sur les steps, et Création / Suppression sur les images.");
        public static string PrivilegeAskAdmin => T("Ask your system administrator to add them to your role.", "Demandez à votre administrateur système de les ajouter à votre rôle.");

        #endregion

        #region Loading

        public static string Stop => T("Stop", "Arrêter");
        public static string Stopping => T("Stopping…", "Arrêt en cours…");
        public static string ElapsedFormat => T("Elapsed: {0}", "Écoulé : {0}");
        public static string LongRunning => T("This is taking a while: you can stop it.", "C'est un peu long : vous pouvez arrêter.");
        public static string TaskStopped => T("Stopped.", "Arrêté.");
        public static string Loading => T("Loading", "Chargement");
        public static string Saving => T("Saving", "Enregistrement");
        public static string PreparingEditor => T("Preparing the editor", "Préparation de l'éditeur");
        public static string ReadingEndpoints => T("Reading the service endpoints…", "Lecture des service endpoints…");
        public static string ReadingSteps => T("Reading the steps and images…", "Lecture des steps et des images…");
        public static string ReadingNamespace(string name) => T($"Reading {name}…", $"Lecture de {name}…");
        public static string ReadingQueues(string name) => T($"{name}: reading the queues…", $"{name} : lecture des queues…");
        public static string ReadingTopics(string name) => T($"{name}: reading the topics…", $"{name} : lecture des topics…");
        public static string ReadingSubscriptions(string name, int topics) => T($"{name}: reading the subscriptions of {topics} topic(s)…", $"{name} : lecture des souscriptions de {topics} topic(s)…");
        public static string ReadingRules(string name, int subscriptions) => T($"{name}: reading the filters of {subscriptions} subscription(s)…", $"{name} : lecture des filtres de {subscriptions} souscription(s)…");
        public static string WritingStep(string name) => T($"Saving {name}…", $"Enregistrement de {name}…");
        public static string SigningIn => T("Waiting for the sign-in…", "En attente de la connexion…");

        #endregion

        #region Azure

        public static string InvalidConnectionString => T("This is not a Service Bus connection string: Endpoint=sb://… is missing.", "Ce n'est pas une connection string Service Bus : Endpoint=sb://… est absent.");
        public static string ConnectionStringNoKey => T("The connection string has no shared access key.", "La connection string ne contient pas de clé d'accès partagé.");
        public static string ConnectionStringEntityScoped => T("This connection string is limited to one entity (EntityPath). Use a namespace-level key with the Manage right, or sign in with a Microsoft account.", "Cette connection string est limitée à une entité (EntityPath). Utilisez une clé au niveau du namespace avec le droit Manage, ou connectez-vous avec un compte Microsoft.");
        public static string SecretUnreadable => T("This saved connection string was encrypted by another Windows account or computer. Remove it and add it again.", "Cette connection string a été chiffrée par un autre compte Windows ou un autre poste. Retirez-la puis ajoutez-la de nouveau.");
        public static string AzureUnreachable(string host) => T($"{host} cannot be reached.", $"{host} est injoignable.");
        public static string AzureAccessDenied(string host) => T($"Access to {host} was refused. A connection string needs a namespace-level key with the Manage right; a Microsoft account needs at least the Reader role.", $"L'accès à {host} a été refusé. Une connection string nécessite une clé au niveau du namespace avec le droit Manage ; un compte Microsoft nécessite au moins le rôle Reader.");
        public static string AzureNotFound => T("Azure does not know this resource.", "Azure ne connaît pas cette ressource.");
        public static string AzureThrottled => T("Azure is limiting the requests. Wait a little, or lower the parallel requests in the options.", "Azure limite les requêtes. Patientez un peu, ou réduisez les requêtes parallèles dans les options.");
        public static string AzureFailed(int status) => T($"Azure answered with an error (HTTP {status}).", $"Azure a répondu par une erreur (HTTP {status}).");
        public static string AzureCliMissing => T("The Azure CLI is not installed, or not on the PATH.", "Azure CLI n'est pas installé, ou absent du PATH.");
        public static string AzureCliFailed => T("The Azure CLI could not give a token. Run « az login » in a terminal, then try again.", "Azure CLI n'a pas pu fournir de jeton. Lancez « az login » dans un terminal, puis réessayez.");
        public static string SignInTimedOut => T("The sign-in did not finish in time.", "La connexion n'a pas abouti à temps.");
        public static string SignInFailed => T("The sign-in failed.", "La connexion a échoué.");
        public static string SignInBrowserDone => T("Sign-in finished. You can close this window and go back to XrmToolBox.", "Connexion terminée. Vous pouvez fermer cette fenêtre et revenir à XrmToolBox.");

        #endregion

        #region Tabs and explorer

        public static string TabExplorer => T("Explorer", "Explorateur");
        public static string TabMatching => T("Matching", "Matching");
        public static string TabSteps => T("Steps", "Steps");
        public static string TreeTitle => T("Azure Service Bus", "Azure Service Bus");
        public static string TreeSubtitle => T("Namespaces, queues, topics", "Namespaces, queues, topics");
        public static string TreeFilterCue => T("Filter by name…", "Filtrer par nom…");
        public static string OnlyWithSteps => T("Only items with Dataverse steps", "Uniquement les éléments avec des steps Dataverse");
        public static string AddNamespaceTip => T("Add a namespace", "Ajouter un namespace");
        public static string TreeEmpty => T("Add a namespace with Azure connections, then click Load.\r\n\r\nWithout a connection, Load still shows the namespaces used by the Dataverse steps.", "Ajoutez un namespace avec Connexions Azure, puis cliquez sur Charger.\r\n\r\nSans connexion, Charger affiche quand même les namespaces utilisés par les steps Dataverse.");
        public static string TreeNoMatch => T("Nothing matches this filter.", "Rien ne correspond à ce filtre.");
        public static string DetailEmptyTitle => T("Select an item", "Sélectionnez un élément");
        public static string DetailEmpty => T("Pick a namespace, a queue, a topic or a subscription in the tree to see its Dataverse steps, its filters and its properties.", "Choisissez un namespace, une queue, un topic ou une souscription dans l'arbre pour voir ses steps Dataverse, ses filtres et ses propriétés.");
        public static string GroupQueues => T("Queues", "Queues");
        public static string GroupTopics => T("Topics", "Topics");
        public static string KindQueue => T("Queue", "Queue");
        public static string KindTopic => T("Topic", "Topic");
        public static string KindSubscription => T("Subscription", "Souscription");
        public static string KindLabel(SbKind kind) => kind == SbKind.Queue ? KindQueue : kind == SbKind.Topic ? KindTopic : KindSubscription;
        public static string NamespaceCaption(int queues, int topics) => T($"{queues} queue(s) · {topics} topic(s)", $"{queues} queue(s) · {topics} topic(s)");
        public static string QueueCaption(long active, long dead) => dead > 0 ? T($"{active:N0} active · {dead:N0} dead-letter", $"{active:N0} actifs · {dead:N0} dead-letter") : T($"{active:N0} active", $"{active:N0} actifs");
        public static string TopicCaption(int subscriptions) => T($"{subscriptions} subscription(s)", $"{subscriptions} souscription(s)");
        public static string SubscriptionCaption(int rules, long active, long dead) => (rules + " " + T("filter(s)", "filtre(s)")) + " · " + QueueCaption(active, dead);
        public static string PillSteps(int count) => count + " step" + (count > 1 ? "s" : string.Empty);
        public static string PillDeadLetter => "DLQ";
        public static string PillNotConnected => T("Not connected", "Non connecté");
        public static string PillError => T("Error", "Erreur");
        public static string PillMissing => T("Missing", "Absent");
        public static string PillDisabled => T("Disabled", "Désactivé");

        public static string TileActive => T("Active messages", "Messages actifs");
        public static string TileDeadLetter => T("Dead-letter", "Dead-letter");
        public static string TileScheduled => T("Scheduled", "Planifiés");
        public static string TileSize => T("Size used", "Taille utilisée");
        public static string TileSteps => T("Dataverse steps", "Steps Dataverse");
        public static string TileSubscriptions => T("Subscriptions", "Souscriptions");
        public static string TileQueues => T("Queues", "Queues");
        public static string TileTopics => T("Topics", "Topics");

        public static string DetailSteps => T("Dataverse steps", "Steps Dataverse");
        public static string DetailFilters => T("Subscriptions and filters", "Souscriptions et filtres");
        public static string DetailFiltersOnly => T("Filters", "Filtres");
        public static string DetailProperties => T("Properties", "Propriétés");
        public static string SubscriptionStepsHint(string topic) => T($"These steps publish to the topic {topic}, which feeds this subscription.", $"Ces steps publient sur le topic {topic}, qui alimente cette souscription.");
        public static string AlertGhostNamespace(string host) => T($"{host} is used by Dataverse but is not connected: its queues and topics cannot be compared with Azure.", $"{host} est utilisé par Dataverse mais n'est pas connecté : ses queues et topics ne peuvent pas être comparés avec Azure.");
        public static string AlertGhostEntity => T("This entity does not exist on Azure: the steps below will fail when they run.", "Cette entité n'existe pas sur Azure : les steps ci-dessous échoueront à l'exécution.");
        public static string AlertNamespaceError(string message) => T($"This namespace could not be read. {message}", $"Ce namespace n'a pas pu être lu. {message}");
        public static string AlertEntityDisabled(string status) => T($"This entity is not active on Azure (status: {status}).", $"Cette entité n'est pas active sur Azure (statut : {status}).");
        public static string ConnectNamespace => T("Connect", "Connecter");
        public static string ShowError => T("Details", "Détails");

        public static string ColSubscription => T("Subscription", "Souscription");
        public static string ColActive => T("Active", "Actifs");
        public static string ColDeadLetter => T("Dead-letter", "Dead-letter");
        public static string ColFilters => T("Filters", "Filtres");
        public static string ColForwardTo => T("Forward to", "Transfert vers");
        public static string ColStatus => T("Status", "Statut");
        public static string ColRule => T("Rule", "Règle");
        public static string ColType => T("Type", "Type");
        public static string ColExpression => T("Filter", "Filtre");
        public static string ColAction => T("Action", "Action");
        public static string FilterSql => T("SQL", "SQL");
        public static string FilterCorrelation => T("Correlation", "Corrélation");
        public static string FilterAll => T("All messages", "Tous les messages");
        public static string FilterNone => T("No message", "Aucun message");
        public static string FilterKindLabel(RuleFilterKind kind) => kind == RuleFilterKind.Sql ? FilterSql : kind == RuleFilterKind.Correlation ? FilterCorrelation : kind == RuleFilterKind.True ? FilterAll : FilterNone;
        public static string SelectedFilter => T("Selected filter", "Filtre sélectionné");
        public static string CopyExpression => T("Copy", "Copier");
        public static string NoFilterSelected => T("Select a rule to read its full filter.", "Sélectionnez une règle pour lire son filtre complet.");
        public static string RuleAction => T("Action", "Action");

        public static string PropHost => T("Host", "Hôte");
        public static string PropLocation => T("Location", "Région");
        public static string PropTier => T("Tier", "Niveau");
        public static string PropStatus => T("Status", "Statut");
        public static string PropAzureSubscription => T("Azure subscription", "Abonnement Azure");
        public static string PropAccess => T("Read with", "Lu avec");
        public static string PropAccessKey => T("Connection string", "Connection string");
        public static string PropAccessMicrosoft => T("Microsoft account", "Compte Microsoft");
        public static string PropType => T("Type", "Type");
        public static string PropPath => T("Path", "Chemin");
        public static string PropTotal => T("Total messages", "Total des messages");
        public static string PropTransfer => T("Transfer messages", "Messages en transfert");
        public static string PropMaxSize => T("Maximum size", "Taille maximale");
        public static string PropLock => T("Lock duration", "Durée de verrouillage");
        public static string PropTtl => T("Default message time to live", "Durée de vie par défaut des messages");
        public static string PropAutoDelete => T("Auto delete on idle", "Suppression auto. si inactif");
        public static string PropMaxDelivery => T("Maximum delivery count", "Nombre maximal de livraisons");
        public static string PropSessions => T("Sessions", "Sessions");
        public static string PropDuplicate => T("Duplicate detection", "Détection des doublons");
        public static string PropDeadLetterExpiry => T("Dead-letter on expiration", "Dead-letter à l'expiration");
        public static string PropPartitioned => T("Partitioned", "Partitionné");
        public static string PropForwardTo => T("Forward to", "Transfert vers");
        public static string PropForwardDead => T("Forward dead-letter to", "Transfert du dead-letter vers");
        public static string PropCreated => T("Created on", "Créé le");
        public static string PropUpdated => T("Updated on", "Modifié le");
        public static string PropAccessed => T("Last accessed on", "Dernier accès le");
        public static string PropEndpoints => T("Dataverse endpoints", "Endpoints Dataverse");
        public static string Unlimited => T("Unlimited", "Illimité");
        public static string Days(double value) => T($"{value:0.##} day(s)", $"{value:0.##} jour(s)");
        public static string Hours(double value) => T($"{value:0.##} h", $"{value:0.##} h");
        public static string Minutes(double value) => T($"{value:0.##} min", $"{value:0.##} min");
        public static string Seconds(double value) => T($"{value:0.##} s", $"{value:0.##} s");

        #endregion

        #region Matching

        public static string MatchTitle => T("Dataverse ↔ Azure", "Dataverse ↔ Azure");
        public static string MatchSubtitle => T("Double-click a line to open it in the explorer", "Double-cliquez sur une ligne pour l'ouvrir dans l'explorateur");
        public static string MatchSearchCue => T("Search an endpoint or a path…", "Rechercher un endpoint ou un chemin…");
        public static string MatchAll => T("All statuses", "Tous les statuts");
        public static string MatchProblems => T("To check", "À vérifier");
        public static string MatchCount(int visible, int total) => T($"{visible} of {total} line(s)", $"{visible} sur {total} ligne(s)");
        public static string OpenInExplorer => T("Open in the explorer", "Ouvrir dans l'explorateur");
        public static string TileEndpoints => T("Endpoints", "Endpoints");
        public static string TileLinked => T("Linked", "Liés");
        public static string TileProblems => T("To check", "À vérifier");
        public static string TileNotConnected => T("Not connected", "Non connectés");
        public static string TileUnused => T("Azure without endpoint", "Azure sans endpoint");
        public static string TileAllSteps => T("Steps", "Steps");
        public static string TileEnabledSteps => T("Enabled steps", "Steps actifs");
        public static string ColMatch => T("Status", "Statut");
        public static string ColEndpoint => T("Endpoint", "Endpoint");
        public static string ColContract => T("Type", "Type");
        public static string ColNamespace => T("Namespace", "Namespace");
        public static string ColPath => T("Queue / topic", "Queue / topic");
        public static string ColSteps => T("Steps", "Steps");
        public static string ColImages => T("Images", "Images");
        public static string ColMessages => T("Messages", "Messages");

        public static string StatusLabel(MatchStatus status)
        {
            switch (status)
            {
                case MatchStatus.Linked: return T("Linked", "Lié");
                case MatchStatus.NoSteps: return T("No steps", "Sans step");
                case MatchStatus.MissingOnAzure: return T("Missing on Azure", "Absent d'Azure");
                case MatchStatus.TypeMismatch: return T("Wrong type", "Mauvais type");
                case MatchStatus.NotConnected: return T("Not connected", "Non connecté");
                case MatchStatus.NoEndpoint: return T("No endpoint", "Sans endpoint");
                default: return T("Other type", "Autre type");
            }
        }

        public static string ContractLabel(int contract)
        {
            switch (contract)
            {
                case EndpointContract.OneWay: return "OneWay";
                case EndpointContract.Queue: return "Queue";
                case EndpointContract.Rest: return "REST";
                case EndpointContract.TwoWay: return "TwoWay";
                case EndpointContract.Topic: return "Topic";
                case EndpointContract.QueuePersistent: return T("Queue (persistent)", "Queue (persistante)");
                case EndpointContract.EventHub: return "Event Hub";
                case EndpointContract.Webhook: return "Webhook";
                case EndpointContract.EventGrid: return "Event Grid";
                case EndpointContract.ManagedDataLake: return "Managed Data Lake";
                case EndpointContract.ContainerStorage: return "Container Storage";
                default: return contract.ToString();
            }
        }

        #endregion

        #region Steps

        public static string StepsTitle => T("Steps sending to Azure", "Steps qui envoient vers Azure");
        public static string StepsSubtitle => T("Double-click a step to edit its trigger and images", "Double-cliquez sur un step pour modifier son trigger et ses images");
        public static string StepsSearchCue => T("Search a table, a message, a column…", "Rechercher une table, un message, une colonne…");
        public static string StepCount(int visible, int total) => T($"{visible} of {total} step(s)", $"{visible} sur {total} step(s)");
        public static string NoSteps => T("No Dataverse step sends to this item.", "Aucun step Dataverse n'envoie vers cet élément.");
        public static string Edit => T("Edit", "Modifier");
        public static string EditStepTip => T("Edit the trigger and the images of the selected step", "Modifier le trigger et les images du step sélectionné");
        public static string Enable => T("Enable", "Activer");
        public static string Disable => T("Disable", "Désactiver");
        public static string Enabled => T("Enabled", "Activé");
        public static string Disabled => T("Disabled", "Désactivé");
        public static string Managed => T("Managed", "Managé");
        public static string ColState => T("State", "État");
        public static string ColMessage => T("Message", "Message");
        public static string ColTable => T("Table", "Table");
        public static string ColStage => T("Stage", "Stage");
        public static string ColMode => T("Mode", "Mode");
        public static string ColRank => T("Rank", "Rang");
        public static string ColFiltering => T("Filtering columns", "Colonnes de filtrage");
        public static string ColTarget => T("Queue / topic", "Queue / topic");
        public static string ColName => T("Name", "Nom");
        public static string ColAlias => T("Alias", "Alias");
        public static string ColColumns => T("Columns", "Colonnes");
        public static string ImagesOfStep(string step) => T($"Images of {step}", $"Images de {step}");
        public static string ImagesNoStep => T("Images of the selected step", "Images du step sélectionné");
        public static string AllTables => T("All tables", "Toutes les tables");
        public static string AllColumns => T("All columns", "Toutes les colonnes");
        public static string AllColumnsTrigger => T("All columns (any update)", "Toutes les colonnes (toute modification)");

        public static string StageLabel(int stage)
        {
            switch (stage)
            {
                case StepStage.PreValidation: return T("Pre-validation", "Pré-validation");
                case StepStage.PreOperation: return T("Pre-operation", "Pré-opération");
                case StepStage.PostOperation: return T("Post-operation", "Post-opération");
                default: return stage.ToString();
            }
        }

        public static string ModeLabel(int mode) => mode == StepMode.Asynchronous ? T("Asynchronous", "Asynchrone") : T("Synchronous", "Synchrone");

        public static string ImageTypeLabel(int type) => type == ImageKind.Pre ? "Pre" : type == ImageKind.Post ? "Post" : T("Pre + Post", "Pre + Post");

        public static string ConfirmBulkTitle(bool enable) => enable ? T("Enable the steps", "Activer les steps") : T("Disable the steps", "Désactiver les steps");
        public static string ConfirmBulkMessage(int count, bool enable) => enable
            ? T($"{count} step(s) will send messages to Azure again.", $"{count} step(s) enverront de nouveau des messages vers Azure.")
            : T($"{count} step(s) will stop sending messages to Azure until they are enabled again.", $"{count} step(s) n'enverront plus de messages vers Azure jusqu'à leur réactivation.");
        public static string ConfirmBulkQuestion => T("Apply this change in Dataverse?", "Appliquer ce changement dans Dataverse ?");
        public static string ApplyAction => T("Apply", "Appliquer");
        public static string CancelAction => T("Cancel", "Annuler");
        public static string BulkSkipped(int count) => T($"{count} managed step(s) cannot be changed and are left out.", $"{count} step(s) managé(s) non modifiable(s) sont ignorés.");
        public static string NothingToChange => T("All the selected steps are already in this state, or are read-only.", "Tous les steps sélectionnés sont déjà dans cet état, ou sont en lecture seule.");
        public static string BulkDone(int count) => T($"{count} step(s) updated.", $"{count} step(s) mis à jour.");
        public static string BulkFailed(int done, int total) => T($"{done} of {total} step(s) updated before the error.", $"{done} step(s) sur {total} mis à jour avant l'erreur.");
        public static string StepSaved(string name) => T($"{name} was saved.", $"{name} a été enregistré.");

        #endregion

        #region Step editor

        public static string EditorTitle => T("Edit the step", "Modifier le step");
        public static string TriggerTitle => T("Trigger", "Trigger");
        public static string TriggerSubtitle => T("What sends the message to Azure", "Ce qui envoie le message vers Azure");
        public static string ImagesTitle => T("Images", "Images");
        public static string ImagesSubtitle => T("Record data added to the message", "Données de l'enregistrement ajoutées au message");
        public static string FieldMessage => T("Message", "Message");
        public static string FieldTable => T("Table", "Table");
        public static string FieldStage => T("Stage", "Stage");
        public static string FieldMode => T("Mode", "Mode");
        public static string FieldRank => T("Rank", "Rang");
        public static string FieldFiltering => T("Filtering columns", "Colonnes de filtrage");
        public static string FieldDeleteJob => T("Delete the system job on success", "Supprimer le job système en cas de succès");
        public static string FieldName => T("Name", "Nom");
        public static string FieldDescription => T("Description", "Description");
        public static string SelectColumns => T("Select…", "Choisir…");
        public static string ClearColumns => T("Clear", "Effacer");
        public static string FilteringOnlyUpdate => T("Only the Update message has filtering columns.", "Seul le message Update possède des colonnes de filtrage.");
        public static string Add => T("Add", "Ajouter");
        public static string Remove => T("Remove", "Retirer");
        public static string Save => T("Save", "Enregistrer");
        public static string ImagesHint => T("Images travel inside the message: keep only the columns the receiver needs. A Service Bus message is limited to 256 KB on the Standard tier.", "Les images voyagent dans le message : ne gardez que les colonnes utiles au destinataire. Un message Service Bus est limité à 256 Ko en niveau Standard.");
        public static string ReadOnlyStep => T("This step is managed and not customizable: it is read-only.", "Ce step est managé et non personnalisable : il est en lecture seule.");
        public static string ManagedStepWarning => T("This step comes from a managed solution: the change creates an unmanaged layer above it.", "Ce step vient d'une solution managée : le changement crée une couche non gérée au-dessus.");
        public static string ReviewTitle => T("Review the changes", "Vérifier les changements");
        public static string ReviewMessage(string step) => T($"These changes will be written to {step}:", $"Ces changements seront écrits sur {step} :");
        public static string ReviewQuestion => T("Save them in Dataverse?", "Les enregistrer dans Dataverse ?");
        public static string NoChanges => T("Nothing to save: the step has not changed.", "Rien à enregistrer : le step n'a pas changé.");
        public static string ChangeImageAdded(string image) => T($"Image added: {image}", $"Image ajoutée : {image}");
        public static string ChangeImageUpdated(string before, string after) => T($"Image changed: {before}  →  {after}", $"Image modifiée : {before}  →  {after}");
        public static string ChangeImageRemoved(string image) => T($"Image removed: {image}", $"Image retirée : {image}");
        public static string TableRequired => T("This message needs a table.", "Ce message nécessite une table.");

        public static string RuleMessageRequired => T("Choose a message.", "Choisissez un message.");
        public static string RuleAsyncPostOnly => T("An asynchronous step only runs in Post-operation.", "Un step asynchrone ne s'exécute qu'en Post-opération.");
        public static string RuleRank => T("The rank must be 1 or more.", "Le rang doit être supérieur ou égal à 1.");
        public static string RuleFilteringUpdateOnly => T("Filtering columns only exist on the Update message.", "Les colonnes de filtrage n'existent que sur le message Update.");
        public static string RuleNoImages(string message) => T($"The {message} message has no images: remove them first.", $"Le message {message} n'a pas d'images : retirez-les d'abord.");
        public static string RuleImageAlias(string name) => T($"The image {name} needs an alias.", $"L'image {name} doit avoir un alias.");
        public static string RuleImageType(string image, string type, string message, string stage) => T($"The image {image}: a {type} image is not available on {message} in {stage}.", $"Image {image} : une image {type} n'est pas disponible sur {message} en {stage}.");
        public static string RuleImageDuplicate(string alias) => T($"Two images use the alias {alias}.", $"Deux images utilisent l'alias {alias}.");

        public static string ImageEditorAdd => T("Add an image", "Ajouter une image");
        public static string ImageEditorEdit => T("Edit the image", "Modifier l'image");
        public static string FieldImageType => T("Type", "Type");
        public static string FieldAlias => T("Alias", "Alias");
        public static string FieldColumns => T("Columns", "Colonnes");
        public static string ColumnsAll => T("All columns", "Toutes les colonnes");
        public static string ColumnsSelected => T("Selected columns", "Colonnes choisies");
        public static string AllColumnsWarning => T("All columns can make the message heavy: prefer the columns the receiver needs.", "Toutes les colonnes peuvent alourdir le message : préférez celles dont le destinataire a besoin.");
        public static string ImageNeedsColumns => T("Select at least one column, or choose All columns.", "Choisissez au moins une colonne, ou Toutes les colonnes.");
        public static string ImageNeedsAlias => T("Enter an alias.", "Saisissez un alias.");
        public static string Ok => T("OK", "OK");

        public static string PickerTitle => T("Select the columns", "Choisir les colonnes");
        public static string PickerSubtitle(string table) => table;
        public static string PickerSearchCue => T("Search a column…", "Rechercher une colonne…");
        public static string PickerOnlySelected => T("Selected only", "Uniquement la sélection");
        public static string PickerLoading => T("Reading the columns…", "Lecture des colonnes…");
        public static string PickerCount(int count) => T($"{count} column(s) selected", $"{count} colonne(s) sélectionnée(s)");
        public static string ColDisplayName => T("Display name", "Nom d'affichage");
        public static string ColLogicalName => T("Logical name", "Nom logique");
        public static string ColColumnType => T("Type", "Type");

        #endregion

        #region Connections

        public static string ConnectionsTitle => T("Azure connections", "Connexions Azure");
        public static string ConnectionsSubtitle => T("The namespaces read by the tool. Azure is only read, never changed.", "Les namespaces lus par l'outil. Azure est uniquement lu, jamais modifié.");
        public static string TabConnected => T("Connected", "Connectés");
        public static string TabConnectionString => T("Connection string", "Connection string");
        public static string TabMicrosoft => T("Microsoft account", "Compte Microsoft");
        public static string ColMethod => T("Method", "Méthode");
        public static string ColDetail => T("Detail", "Détail");
        public static string MethodKey => T("Connection string", "Connection string");
        public static string MethodBrowser => T("Microsoft account", "Compte Microsoft");
        public static string MethodCli => "Azure CLI";
        public static string MethodLabel(ConnectionMethod method) => method == ConnectionMethod.ConnectionString ? MethodKey : method == ConnectionMethod.AzureCli ? MethodCli : MethodBrowser;
        public static string KeyStored => T("Encrypted for this Windows account", "Chiffrée pour ce compte Windows");
        public static string NeededByDataverse(string hosts) => T($"Used by Dataverse and not connected: {hosts}", $"Utilisés par Dataverse et non connectés : {hosts}");
        public static string ConnectHost(string host) => T($"Paste the connection string of {host}.", $"Collez la connection string de {host}.");
        public static string ConnectionStringLabel => T("Connection string of the namespace", "Connection string du namespace");
        public static string ConnectionStringHint => T("Azure portal ▸ your namespace ▸ Shared access policies ▸ RootManageSharedAccessKey (or another policy with the Manage right). The string is tested, then encrypted for your Windows account (DPAPI). The tool only uses it to read.", "Portail Azure ▸ votre namespace ▸ Stratégies d'accès partagé ▸ RootManageSharedAccessKey (ou une autre stratégie avec le droit Manage). La chaîne est testée, puis chiffrée pour votre compte Windows (DPAPI). L'outil ne s'en sert que pour lire.");
        public static string TestAndAdd => T("Test and add", "Tester et ajouter");
        public static string SignInBrowser => T("Sign in with the browser", "Se connecter avec le navigateur");
        public static string UseAzureCli => T("Use the Azure CLI", "Utiliser Azure CLI");
        public static string MicrosoftHint => T("No key needed: the Reader role on the namespace is enough. Tokens stay in memory and are never saved.", "Aucune clé nécessaire : le rôle Reader sur le namespace suffit. Les jetons restent en mémoire et ne sont jamais enregistrés.");
        public static string AddSelected => T("Add the selected namespaces", "Ajouter les namespaces cochés");
        public static string Searching => T("Looking for your namespaces…", "Recherche de vos namespaces…");
        public static string SearchingIn(string subscription) => T($"Reading {subscription}…", $"Lecture de {subscription}…");
        public static string TestingConnection => T("Testing the connection…", "Test de la connexion…");
        public static string FoundNamespaces(int count) => T($"{count} namespace(s) found. Tick those to add.", $"{count} namespace(s) trouvé(s). Cochez ceux à ajouter.");
        public static string NoNamespaceFound => T("No namespace found with this account.", "Aucun namespace trouvé avec ce compte.");
        public static string ConnectionAdded(string host) => T($"{host} was added. Click Load to read it.", $"{host} a été ajouté. Cliquez sur Charger pour le lire.");
        public static string ConnectionsAdded(int count) => T($"{count} namespace(s) added. Click Load to read them.", $"{count} namespace(s) ajouté(s). Cliquez sur Charger pour les lire.");
        public static string ColSubscriptionAzure => T("Azure subscription", "Abonnement Azure");
        public static string ColResourceGroup => T("Resource group", "Groupe de ressources");
        public static string ColTier => T("Tier", "Niveau");
        public static string ColHost => T("Namespace", "Namespace");
        public static string ReloadHint => T("Connections changed: click Load to refresh.", "Connexions modifiées : cliquez sur Charger pour actualiser.");

        #endregion

        #region Options

        public static string OptionsTitle => T("Options", "Options");
        public static string OptionsSubtitle => T("Settings of ServiceBus Explorer", "Paramètres de ServiceBus Explorer");
        public static string OptionParallel => T("Parallel Azure requests", "Requêtes Azure parallèles");
        public static string OptionParallelHint => T("Used to read the subscriptions and filters. Lower it if Azure limits the requests.", "Sert à lire les souscriptions et les filtres. Réduisez si Azure limite les requêtes.");
        public static string OptionMaxMessages => T("Active messages read per queue", "Messages actifs lus par file");
        public static string OptionMaxMessagesHint => T("Oldest first, for each queue or subscription (the dead-letter queue is not read). Peeking takes nothing: the messages stay where they are.", "Les plus anciens d'abord, pour chaque queue ou souscription (la dead-letter queue n'est pas lue). La lecture ne prend rien : les messages restent où ils sont.");
        public static string OptionTenant => T("Microsoft Entra tenant (optional)", "Tenant Microsoft Entra (facultatif)");
        public static string OptionTenantHint => T("Leave empty to use the tenant of your account.", "Laissez vide pour utiliser le tenant de votre compte.");
        public static string OptionClient => T("Application (client) ID (optional)", "ID d'application (client) (facultatif)");
        public static string OptionClientHint => T("Leave empty for the public client of the Azure CLI. If your tenant blocks it, register a public client with the redirect URI http://localhost and the Azure Service Management permission.", "Laissez vide pour le client public d'Azure CLI. Si votre tenant le bloque, enregistrez un client public avec l'URI de redirection http://localhost et la permission Azure Service Management.");

        #endregion

        #region Messages

        public static string DetailMessages => T("Messages", "Messages");
        public static string MessagesButton => T("Messages", "Messages");
        public static string MessagesButtonTip => T("Peek the active messages sent by the selected step(s) (the dead-letter queue is not read)", "Lire les messages actifs envoyés par le ou les steps sélectionnés (la dead-letter queue n'est pas lue)");
        public static string ReadMessages => T("Read active messages", "Lire les messages actifs");
        public static string ReadMessagesTip => T("Peek the active messages waiting: nothing is locked or removed, and their delivery count does not change. The dead-letter queue is not read.", "Lit les messages actifs en attente : rien n'est verrouillé ni retiré, et leur nombre de livraisons ne change pas. La dead-letter queue n'est pas lue.");
        public static string ReadingMessagesTitle => T("Reading the active messages", "Lecture des messages actifs");
        public static string ReadingMessagesShort => T("Reading…", "Lecture…");
        public static string ReadingMessages(string path) => T($"Reading the active messages of {path}…", $"Lecture des messages actifs de {path}…");
        public static string OnlyStepMessages => T("Only this step", "Seulement ce step");
        public static string OnlyStepsMessages => T("Only these steps", "Seulement ces steps");
        public static string MessagesSearchCue => T("Search step, table, record, ID, body…", "Rechercher step, table, ligne, ID, contenu…");
        public static string PeekHint => T("Active messages only: the dead-letter queue is not read. Nothing is locked or removed, and the delivery count does not change.", "Messages actifs uniquement : la dead-letter queue n'est pas lue. Rien n'est verrouillé ni retiré, et le nombre de livraisons ne change pas.");

        public static string MessagesIntro(string name, int sources) => sources > 1
            ? T($"Click « {ReadMessages} » to see the active messages waiting in the {sources} subscriptions of {name}.", $"Cliquez sur « {ReadMessages} » pour voir les messages actifs en attente dans les {sources} souscriptions de {name}.")
            : T($"Click « {ReadMessages} » to see the active messages waiting in {name}.", $"Cliquez sur « {ReadMessages} » pour voir les messages actifs en attente dans {name}.");

        public static string MessagesUnavailable => T("This entity was not read on Azure: its messages cannot be peeked. Connect its namespace, then click Load.", "Cette entité n'a pas été lue sur Azure : ses messages ne peuvent pas être lus. Connectez son namespace, puis cliquez sur Charger.");
        public static string MessagesNone => T("No active message is waiting. Consumers may already have taken them: a peek only sees what is still in the queue.", "Aucun message actif en attente. Les consommateurs les ont peut-être déjà pris : la lecture ne voit que ce qui est encore dans la file.");
        public static string MessagesNoneForStep(int read) => T($"None of the {read} active message(s) read was sent by this step. Uncheck « Only this step » to see them.", $"Aucun des {read} message(s) actif(s) lus n'a été envoyé par ce step. Décochez « Seulement ce step » pour les voir.");
        public static string MessagesNoMatch => T("No message matches the search.", "Aucun message ne correspond à la recherche.");
        public static string MessagesAllFailed => T("The messages could not be read: see the details above.", "Les messages n'ont pas pu être lus : voir les détails ci-dessus.");
        public static string MessagesCount(int shown, int total) => shown == total
            ? T($"{total:N0} active message(s)", $"{total:N0} message(s) actif(s)")
            : T($"{shown:N0} of {total:N0} active message(s)", $"{shown:N0} sur {total:N0} message(s) actif(s)");
        public static string MessagesTruncated(int max) => T($" · limit of {max:N0} per queue reached (Options)", $" · limite de {max:N0} par file atteinte (Options)");
        public static string MessagesErrors(int count, string first) => T($"{count} queue(s) could not be read, starting with {first}.", $"{count} file(s) n'ont pas pu être lues, à commencer par {first}.");
        public static string MessagesErrorsTitle => T("Messages not read", "Messages non lus");
        public static string MessagesAccessDenied(string host) => T($"Reading the messages of {host} was refused. A connection string needs the Listen right; a Microsoft account needs the « Azure Service Bus Data Receiver » role (Reader and Owner are not enough).", $"La lecture des messages de {host} a été refusée. Une connection string nécessite le droit Listen ; un compte Microsoft nécessite le rôle « Azure Service Bus Data Receiver » (Reader et Owner ne suffisent pas).");
        public static string MessagesFailed => T("Service Bus refused to give the messages.", "Service Bus a refusé de donner les messages.");

        public static string StepMessagesTitle(int steps) => steps > 1 ? T("Messages of the steps", "Messages des steps") : T("Messages of the step", "Messages du step");
        public static string StepMessagesSubtitle(int steps, int targets) => T($"{steps} steps · {targets} queue(s) or topic(s)", $"{steps} steps · {targets} queue(s) ou topic(s)");
        public static string StepMessagesUnavailable(string path) => T($"The messages of {path} cannot be read: its namespace is not connected, or the queue or topic does not exist on Azure.", $"Les messages de {path} ne peuvent pas être lus : son namespace n'est pas connecté, ou la queue ou le topic n'existe pas sur Azure.");

        public static string ColSequence => T("Seq.", "N°");
        public static string ColEnqueued => T("Enqueued", "Mis en file");
        public static string ColDelivery => T("Deliveries", "Livraisons");
        public static string ColSource => T("Read from", "Lu dans");
        public static string UnknownStep => T("step not loaded", "step non chargé");
        public static string NotDataverseMessage => T("(not a Dataverse message)", "(pas un message Dataverse)");

        public static string BodyTitle(string format, int bytes) => format == null
            ? T("Body", "Contenu")
            : T($"Body · {format} · {Helpers.Format.Size(bytes)}", $"Contenu · {format} · {Helpers.Format.Size(bytes)}");
        public static string CopyBody => T("Copy", "Copier");
        public static string OpenBody => T("Open", "Ouvrir");

        public static string SectionMessage => T("Message", "Message");
        public static string SectionDataverse => T("Dataverse context", "Contexte Dataverse");
        public static string SectionApplicationProperties => T("Application properties", "Propriétés applicatives");
        public static string PropSource => T("Read from", "Lu dans");
        public static string PropSequence => T("Sequence number", "Numéro de séquence");
        public static string PropMessageId => T("Message ID", "ID du message");
        public static string PropEnqueued => T("Enqueued", "Mis en file");
        public static string PropScheduled => T("Scheduled for", "Planifié pour");
        public static string PropExpires => T("Expires", "Expire");
        public static string PropDeliveryCount => T("Delivery count", "Nombre de livraisons");
        public static string PropContentType => T("Content type", "Type de contenu");
        public static string PropSubject => T("Subject (label)", "Sujet (label)");
        public static string PropCorrelation => T("Correlation ID", "ID de corrélation");
        public static string PropSession => T("Session ID", "ID de session");
        public static string PropTo => T("To", "À");
        public static string PropReplyTo => T("Reply to", "Répondre à");
        public static string PropStepId => T("Step ID", "ID du step");
        public static string PropRecord => T("Record ID", "ID de la ligne");
        public static string PropDepth => T("Depth", "Profondeur");
        public static string PropUser => T("User", "Utilisateur");
        public static string PropInitiatingUser => T("Initiating user", "Utilisateur initiateur");
        public static string PropOrganization => T("Organization", "Organisation");
        public static string PropOperationCorrelation => T("Operation correlation ID", "ID de corrélation de l'opération");
        public static string PropOperationCreated => T("Operation created on", "Opération créée le");

        #endregion

        #region Status and export

        public static string Summary(int namespaces, int endpoints, int steps) => T($"{namespaces} namespace(s) · {endpoints} endpoint(s) · {steps} step(s)", $"{namespaces} namespace(s) · {endpoints} endpoint(s) · {steps} step(s)");
        public static string SummaryErrors(int count) => T($" · {count} namespace(s) in error", $" · {count} namespace(s) en erreur");
        public static string LoadedAt(string time) => T($" · read at {time}", $" · lu à {time}");
        public static string ExportFilter => T("Excel workbook (*.xlsx)|*.xlsx", "Classeur Excel (*.xlsx)|*.xlsx");
        public static string ExportDoneTitle => T("Report exported", "Rapport exporté");
        public static string ExportDone(string path) => T($"The report was saved to {path}.", $"Le rapport a été enregistré dans {path}.");
        public static string LoadFirstTitle => T("Nothing to export", "Rien à exporter");
        public static string LoadFirst => T("Click Load first.", "Cliquez d'abord sur Charger.");
        public static string SheetMatching => T("Matching", "Matching");
        public static string SheetSteps => T("Steps", "Steps");
        public static string SheetImages => T("Images", "Images");
        public static string SheetEntities => T("Queues and topics", "Queues et topics");
        public static string SheetSubscriptions => T("Subscriptions", "Souscriptions");
        public static string SheetRules => T("Filters", "Filtres");
        public static string ColStep => T("Step", "Step");
        public static string ColTopic => T("Topic", "Topic");
        public static string ColScheduled => T("Scheduled", "Planifiés");
        public static string ColSize => T("Size (bytes)", "Taille (octets)");
        public static string ColEnabledSteps => T("Enabled steps", "Steps actifs");

        #endregion
    }
}
