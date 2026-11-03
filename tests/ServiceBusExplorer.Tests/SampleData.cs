using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace ServiceBusExplorer.Tests
{
    /// <summary>Atom feeds shaped like the answers of the Service Bus management endpoint.</summary>
    internal static class AtomSamples
    {
        private const string Sb = "http://schemas.microsoft.com/netservices/2010/10/servicebus/connect";
        private const string Xsi = "http://www.w3.org/2001/XMLSchema-instance";
        private const string Counts = "http://schemas.microsoft.com/netservices/2011/06/servicebus";

        public static string Feed(params string[] entries) =>
            "<feed xmlns=\"http://www.w3.org/2005/Atom\"><title type=\"text\">x</title>" + string.Concat(entries) + "</feed>";

        private static string Entry(string title, string content) =>
            $"<entry><id>https://ns/{title}</id><title type=\"text\">{title}</title><content type=\"application/xml\">{content}</content></entry>";

        private static string CountDetails(long active, long dead, long scheduled) =>
            $"<CountDetails xmlns:d2p1=\"{Counts}\"><d2p1:ActiveMessageCount>{active}</d2p1:ActiveMessageCount><d2p1:DeadLetterMessageCount>{dead}</d2p1:DeadLetterMessageCount>" +
            $"<d2p1:ScheduledMessageCount>{scheduled}</d2p1:ScheduledMessageCount><d2p1:TransferMessageCount>0</d2p1:TransferMessageCount></CountDetails>";

        public static string Queue(string name, long active, long dead, string status = "Active", string forwardTo = null) => Entry(name,
            $"<QueueDescription xmlns=\"{Sb}\" xmlns:i=\"{Xsi}\"><LockDuration>PT1M</LockDuration><MaxSizeInMegabytes>1024</MaxSizeInMegabytes><RequiresDuplicateDetection>false</RequiresDuplicateDetection>" +
            $"<RequiresSession>true</RequiresSession><DefaultMessageTimeToLive>P14D</DefaultMessageTimeToLive><DeadLetteringOnMessageExpiration>true</DeadLetteringOnMessageExpiration><MaxDeliveryCount>10</MaxDeliveryCount>" +
            $"<SizeInBytes>{active * 2048}</SizeInBytes><MessageCount>{active + dead}</MessageCount><Status>{status}</Status>" + (forwardTo != null ? $"<ForwardTo>{forwardTo}</ForwardTo>" : string.Empty) +
            $"<CreatedAt>2026-03-04T10:11:12.1Z</CreatedAt><UpdatedAt>2026-05-06T10:11:12.1Z</UpdatedAt><AccessedAt>0001-01-01T00:00:00Z</AccessedAt>{CountDetails(active, dead, 2)}<AutoDeleteOnIdle>P10675199DT2H48M5.4775807S</AutoDeleteOnIdle><EnablePartitioning>false</EnablePartitioning></QueueDescription>");

        public static string Topic(string name, int subscriptions) => Entry(name,
            $"<TopicDescription xmlns=\"{Sb}\" xmlns:i=\"{Xsi}\"><DefaultMessageTimeToLive>P7D</DefaultMessageTimeToLive><MaxSizeInMegabytes>2048</MaxSizeInMegabytes><RequiresDuplicateDetection>true</RequiresDuplicateDetection>" +
            $"<SizeInBytes>4096</SizeInBytes><Status>Active</Status><SubscriptionCount>{subscriptions}</SubscriptionCount>{CountDetails(0, 0, 1)}<EnablePartitioning>true</EnablePartitioning></TopicDescription>");

        public static string Subscription(string name, long active, long dead) => Entry(name,
            $"<SubscriptionDescription xmlns=\"{Sb}\" xmlns:i=\"{Xsi}\"><LockDuration>PT30S</LockDuration><RequiresSession>false</RequiresSession><DefaultMessageTimeToLive>P7D</DefaultMessageTimeToLive>" +
            $"<MaxDeliveryCount>5</MaxDeliveryCount><MessageCount>{active + dead}</MessageCount><Status>Active</Status>{CountDetails(active, dead, 0)}</SubscriptionDescription>");

        public static string SqlRule(string name, string expression, string action = null) => Entry(name,
            $"<RuleDescription xmlns=\"{Sb}\" xmlns:i=\"{Xsi}\"><Filter i:type=\"SqlFilter\"><SqlExpression>{expression}</SqlExpression><CompatibilityLevel>20</CompatibilityLevel></Filter>" +
            (action != null ? $"<Action i:type=\"SqlRuleAction\"><SqlExpression>{action}</SqlExpression></Action>" : "<Action i:type=\"EmptyRuleAction\"/>") +
            $"<CreatedAt>2026-03-04T10:11:12Z</CreatedAt><Name>{name}</Name></RuleDescription>");

        public static string TrueRule(string name) => Entry(name,
            $"<RuleDescription xmlns=\"{Sb}\" xmlns:i=\"{Xsi}\"><Filter i:type=\"TrueFilter\"><SqlExpression>1=1</SqlExpression></Filter><Action i:type=\"EmptyRuleAction\"/><Name>{name}</Name></RuleDescription>");

        public static string CorrelationRule(string name, string subject, string key, string value) => Entry(name,
            $"<RuleDescription xmlns=\"{Sb}\" xmlns:i=\"{Xsi}\"><Filter i:type=\"CorrelationFilter\"><Label>{subject}</Label><ContentType>application/json</ContentType>" +
            $"<Properties><KeyValueOfstringanyType><Key>{key}</Key><Value i:type=\"d6p1:string\" xmlns:d6p1=\"http://www.w3.org/2001/XMLSchema\">{value}</Value></KeyValueOfstringanyType></Properties></Filter>" +
            $"<Action i:type=\"EmptyRuleAction\"/><Name>{name}</Name></RuleDescription>");
    }

    /// <summary>Local server answering like a namespace: routes are matched on the path, the SAS header is required.</summary>
    internal sealed class MockServiceBus : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly Dictionary<string, Func<int, string>> _routes = new Dictionary<string, Func<int, string>>(StringComparer.OrdinalIgnoreCase);

        public MockServiceBus()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            new Thread(Serve) { IsBackground = true }.Start();
        }

        public int Port { get; }

        public int Requests;

        public string LastAuthorization;

        public bool RefuseAll { get; set; }

        /// <summary>Delay of each answer, to keep the loading overlay on screen.</summary>
        public int Delay { get; set; }

        public string ConnectionString => $"Endpoint=sb://localhost:{Port}/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=c2VjcmV0;UseDevelopmentEmulator=true";

        /// <summary>JSON answers of Azure Resource Manager: key = path, or path?query for a next page.</summary>
        public Dictionary<string, string> Json { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Url => $"http://localhost:{Port}";

        public void Route(string path, string feed) => _routes[path] = skip => skip == 0 ? feed : AtomSamples.Feed();

        public void RoutePaged(string path, Func<int, string> page) => _routes[path] = page;

        private void Serve()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = _listener.GetContext();
                }
                catch (Exception)
                {
                    return;
                }

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Interlocked.Increment(ref Requests);
                    if (Delay > 0) Thread.Sleep(Delay);
                    LastAuthorization = context.Request.Headers["Authorization"];
                    var path = Uri.UnescapeDataString(context.Request.Url.AbsolutePath);
                    string body;
                    int status;
                    var nextPage = context.Request.Url.AbsolutePath + context.Request.Url.Query;
                    if (LastAuthorization != null && LastAuthorization.StartsWith("Bearer "))
                    {
                        var known = Json.TryGetValue(context.Request.QueryString["page"] != null ? nextPage : path, out var json);
                        status = known ? 200 : 404;
                        body = known ? json : "{\"error\":{\"code\":\"NotFound\",\"message\":\"nothing at " + path + "\"}}";
                    }
                    else if (RefuseAll || LastAuthorization == null || !LastAuthorization.StartsWith("SharedAccessSignature sr="))
                    {
                        status = 401;
                        body = "<Error><Code>401</Code><Detail>Manage claim is required</Detail></Error>";
                    }
                    else if (_routes.TryGetValue(path, out var route))
                    {
                        status = 200;
                        int.TryParse(context.Request.QueryString["$skip"], out var skip);
                        body = route(skip);
                    }
                    else
                    {
                        status = 200;
                        body = AtomSamples.Feed();
                    }

                    var bytes = Encoding.UTF8.GetBytes(body);
                    try
                    {
                        context.Response.StatusCode = status;
                        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                        context.Response.Close();
                    }
                    catch (HttpListenerException)
                    {
                        // The client cancelled the request (Stop button).
                    }
                });
            }
        }

        public void Dispose()
        {
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch (Exception)
            {
                // Already stopped.
            }
        }
    }

    /// <summary>In-memory organization: answers the queries of the tool and records the writes.</summary>
    internal sealed class FakeOrganizationService : IOrganizationService
    {
        public List<Entity> Endpoints { get; } = new List<Entity>();
        public List<Entity> Steps { get; } = new List<Entity>();
        public List<Entity> Images { get; } = new List<Entity>();
        public List<Entity> Updates { get; } = new List<Entity>();
        public List<Entity> Creates { get; } = new List<Entity>();
        public List<Guid> Deletes { get; } = new List<Guid>();
        public Dictionary<Guid, string> Messages { get; } = new Dictionary<Guid, string>();
        public List<Entity> Filters { get; } = new List<Entity>();

        /// <summary>Every call to the organization, to prove what was and was not read.</summary>
        public int Calls;

        /// <summary>Delay of each call, to keep the overlay on screen.</summary>
        public int Delay { get; set; }

        /// <summary>Called with the operation name (read, update, create, delete, metadata) before it runs: it can throw.</summary>
        public Action<string> Fail { get; set; }

        private void Enter(string operation)
        {
            System.Threading.Interlocked.Increment(ref Calls);
            if (Delay > 0) System.Threading.Thread.Sleep(Delay);
            Fail?.Invoke(operation);
        }

        public Guid Create(Entity entity)
        {
            Enter("create");
            Creates.Add(entity);
            var id = Guid.NewGuid();
            entity.Id = id;
            if (entity.LogicalName == "sdkmessageprocessingstepimage") Images.Add(entity);
            return id;
        }

        public void Update(Entity entity)
        {
            Enter("update");
            Updates.Add(entity);
            var target = Steps.Concat(Images).FirstOrDefault(e => e.Id == entity.Id);
            if (target == null) return;
            foreach (var attribute in entity.Attributes) target[attribute.Key] = attribute.Value;
        }

        public void Delete(string entityName, Guid id)
        {
            Enter("delete");
            Deletes.Add(id);
            Images.RemoveAll(i => i.Id == id);
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = (QueryExpression)query;
            Enter("read:" + expression.EntityName);
            IEnumerable<Entity> rows;
            switch (expression.EntityName)
            {
                case "serviceendpoint":
                    rows = Endpoints;
                    break;
                case "sdkmessageprocessingstep":
                    var ids = expression.Criteria.Conditions[0].Values.Cast<Guid>().ToList();
                    rows = Steps.Where(s => ids.Contains(s.GetAttributeValue<EntityReference>("eventhandler").Id)).Select(WithTable);
                    break;
                case "sdkmessageprocessingstepimage":
                    var stepIds = expression.Criteria.Conditions[0].Values.Cast<Guid>().ToList();
                    rows = Images.Where(i => stepIds.Contains(i.GetAttributeValue<EntityReference>("sdkmessageprocessingstepid").Id));
                    break;
                case "sdkmessage":
                    // Like Dataverse: with Distinct, the key is only returned when it is in the column set.
                    var keyRequested = !expression.Distinct || expression.ColumnSet.Columns.Contains("sdkmessageid");
                    rows = Messages.Select(m =>
                    {
                        var row = keyRequested ? new Entity("sdkmessage", m.Key) { ["sdkmessageid"] = m.Key } : new Entity("sdkmessage");
                        row["name"] = m.Value;
                        return row;
                    });
                    break;
                case "sdkmessagefilter":
                    var message = (Guid)expression.Criteria.Conditions[0].Values[0];
                    rows = Filters.Where(f => f.GetAttributeValue<Guid>("message") == message);
                    break;
                default:
                    throw new InvalidOperationException("Unexpected query: " + expression.EntityName);
            }
            return new EntityCollection(rows.ToList());
        }

        private Entity WithTable(Entity step)
        {
            var copy = new Entity(step.LogicalName, step.Id);
            foreach (var attribute in step.Attributes) copy[attribute.Key] = attribute.Value;

            // Dataverse returns the name of a lookup, even when the update only sent its id.
            var message = step.GetAttributeValue<EntityReference>("sdkmessageid");
            copy["sdkmessageid"] = new EntityReference("sdkmessage", message.Id) { Name = Messages[message.Id] };
            var filter = Filters.FirstOrDefault(f => f.Id == step.GetAttributeValue<EntityReference>("sdkmessagefilterid")?.Id);
            if (filter != null) copy["f.primaryobjecttypecode"] = new AliasedValue("sdkmessagefilter", "primaryobjecttypecode", filter["primaryobjecttypecode"]);
            return copy;
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            Enter("metadata");
            if (request is RetrieveEntityRequest entity)
            {
                var metadata = new EntityMetadata { LogicalName = entity.LogicalName };
                var columns = new[] { ("name", "Name"), ("telephone1", "Main Phone"), ("emailaddress1", "Email"), ("accountnumber", "Account Number"), ("creditlimit", "Credit Limit"), ("statuscode", "Status Reason") }
                    .Select(c =>
                    {
                        var attribute = new StringAttributeMetadata { LogicalName = c.Item1, DisplayName = new Label(new LocalizedLabel(c.Item2, 1033), new[] { new LocalizedLabel(c.Item2, 1033) }) };
                        typeof(AttributeMetadata).GetProperty("IsValidForRead").SetValue(attribute, (bool?)true);
                        return (AttributeMetadata)attribute;
                    }).ToArray();
                typeof(EntityMetadata).GetProperty("Attributes").SetValue(metadata, columns);
                var response = new RetrieveEntityResponse();
                response.Results["EntityMetadata"] = metadata;
                return response;
            }
            throw new InvalidOperationException("Unexpected request: " + request.RequestName);
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();

        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();

        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }

    /// <summary>Organization and namespace used by the end-to-end test and the screenshots.</summary>
    internal static class Demo
    {
        public static readonly Guid Update = Guid.NewGuid();
        public static readonly Guid Create = Guid.NewGuid();
        public static readonly Guid Delete = Guid.NewGuid();
        public static readonly Guid Assign = Guid.NewGuid();

        public static void Fill(MockServiceBus bus)
        {
            bus.Route("/$Resources/queues", AtomSamples.Feed(
                AtomSamples.Queue("crm-account-events", 128, 0),
                AtomSamples.Queue("crm-invoice-events", 42, 7),
                AtomSamples.Queue("erp-outbound", 0, 0),
                AtomSamples.Queue("legacy-sync", 3, 0, "Disabled")));
            bus.Route("/$Resources/topics", AtomSamples.Feed(
                AtomSamples.Topic("crm-contact-changes", 3),
                AtomSamples.Topic("crm-opportunity-won", 2),
                AtomSamples.Topic("wrong-kind", 0)));
            bus.Route("/crm-contact-changes/Subscriptions", AtomSamples.Feed(
                AtomSamples.Subscription("marketing-platform", 12, 0),
                AtomSamples.Subscription("data-warehouse", 310, 4),
                AtomSamples.Subscription("support-portal", 0, 0)));
            bus.Route("/crm-contact-changes/Subscriptions/marketing-platform/rules", AtomSamples.Feed(
                AtomSamples.SqlRule("OptedInOnly", "doNotBulkEMail = 0 AND statecode = 0", "SET sys.Label = 'marketing'"),
                AtomSamples.CorrelationRule("ContactCreated", "Create", "entity", "contact")));
            bus.Route("/crm-contact-changes/Subscriptions/data-warehouse/rules", AtomSamples.Feed(AtomSamples.TrueRule("$Default")));
            bus.Route("/crm-contact-changes/Subscriptions/support-portal/rules", AtomSamples.Feed(AtomSamples.SqlRule("VipOnly", "http://schemas.microsoft.com/xrm/2011/Claims/Organization LIKE '%contoso%' AND tier IN ('gold','platinum')")));
            bus.Route("/crm-opportunity-won/Subscriptions", AtomSamples.Feed(AtomSamples.Subscription("sales-analytics", 5, 0), AtomSamples.Subscription("erp-orders", 0, 1)));
            bus.Route("/crm-opportunity-won/Subscriptions/sales-analytics/rules", AtomSamples.Feed(AtomSamples.TrueRule("$Default")));
            bus.Route("/crm-opportunity-won/Subscriptions/erp-orders/rules", AtomSamples.Feed(AtomSamples.SqlRule("LargeDeals", "estimatedvalue > 100000")));
        }

        public static FakeOrganizationService Organization(string host)
        {
            var service = new FakeOrganizationService();
            service.Messages[Update] = "Update";
            service.Messages[Create] = "Create";
            service.Messages[Delete] = "Delete";
            service.Messages[Assign] = "Assign";

            Guid Filter(Guid message, string table)
            {
                var filter = new Entity("sdkmessagefilter", Guid.NewGuid()) { ["message"] = message, ["primaryobjecttypecode"] = table };
                service.Filters.Add(filter);
                return filter.Id;
            }

            foreach (var table in new[] { "account", "contact", "invoice", "opportunity", "lead" })
            {
                foreach (var message in service.Messages.Keys.ToList()) Filter(message, table);
            }

            Guid FilterOf(Guid message, string table) => service.Filters.First(f => f.GetAttributeValue<Guid>("message") == message && (string)f["primaryobjecttypecode"] == table).Id;

            Guid Endpoint(string name, int contract, string address, string path, string description = null)
            {
                var endpoint = new Entity("serviceendpoint", Guid.NewGuid())
                {
                    ["name"] = name,
                    ["contract"] = new OptionSetValue(contract),
                    ["namespaceaddress"] = address,
                    ["path"] = path,
                    ["saskeyname"] = "dataverse-send",
                    ["authtype"] = new OptionSetValue(2),
                    ["messageformat"] = new OptionSetValue(2),
                    ["ismanaged"] = false,
                    ["description"] = description
                };
                service.Endpoints.Add(endpoint);
                return endpoint.Id;
            }

            Guid Step(Guid endpoint, Guid message, string table, int stage, int mode, string filtering, bool enabled = true, bool managed = false, int rank = 1)
            {
                var step = new Entity("sdkmessageprocessingstep", Guid.NewGuid())
                {
                    ["name"] = $"{service.Messages[message]} of {table}",
                    ["eventhandler"] = new EntityReference("serviceendpoint", endpoint),
                    ["sdkmessageid"] = new EntityReference("sdkmessage", message) { Name = service.Messages[message] },
                    ["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", FilterOf(message, table)),
                    ["stage"] = new OptionSetValue(stage),
                    ["mode"] = new OptionSetValue(mode),
                    ["rank"] = rank,
                    ["statecode"] = new OptionSetValue(enabled ? 0 : 1),
                    ["filteringattributes"] = filtering,
                    ["asyncautodelete"] = true,
                    ["ismanaged"] = managed,
                    ["iscustomizable"] = new BooleanManagedProperty(!managed)
                };
                service.Steps.Add(step);
                return step.Id;
            }

            void Image(Guid step, string alias, int type, string attributes)
            {
                service.Images.Add(new Entity("sdkmessageprocessingstepimage", Guid.NewGuid())
                {
                    ["name"] = alias,
                    ["entityalias"] = alias,
                    ["imagetype"] = new OptionSetValue(type),
                    ["attributes"] = attributes,
                    ["messagepropertyname"] = "Target",
                    ["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", step)
                });
            }

            var address = $"sb://{host}/";
            var account = Endpoint("Account events", 2, address, "crm-account-events");
            var invoice = Endpoint("Invoice events", 6, address, "/crm-invoice-events/");
            var contact = Endpoint("Contact changes", 5, address, "CRM-Contact-Changes");
            var won = Endpoint("Opportunity won", 5, address, "crm-opportunity-won");
            Endpoint("ERP outbound (unused)", 2, address, "erp-outbound");
            var missing = Endpoint("Old quotes queue", 2, address, "crm-quote-events");
            var kind = Endpoint("Wrong kind", 2, address, "wrong-kind");
            var legacy = Endpoint("Legacy billing", 5, "sb://contoso-legacy.servicebus.windows.net/", "billing-events");
            Endpoint("Teams webhook", 8, null, null);

            var s1 = Step(account, Update, "account", 40, 1, "name,telephone1,creditlimit");
            Image(s1, "PreImage", 0, "name,telephone1,creditlimit");
            Image(s1, "PostImage", 1, null);
            Image(Step(account, Create, "account", 40, 1, null), "PostImage", 1, "name,accountnumber");
            Step(invoice, Update, "invoice", 40, 1, "statuscode,totalamount");
            Step(contact, Create, "contact", 40, 1, null);
            Image(Step(contact, Update, "contact", 40, 1, "emailaddress1,donotbulkemail,statecode", rank: 2), "PreImage", 0, "emailaddress1");
            Step(contact, Delete, "contact", 40, 0, null, enabled: false);
            Step(contact, Assign, "contact", 40, 1, null, managed: true);
            Step(won, Update, "opportunity", 40, 1, "statuscode");
            Step(missing, Create, "lead", 40, 1, null);
            Step(kind, Update, "lead", 40, 1, "statuscode");
            Step(legacy, Update, "invoice", 40, 1, "paymenttermscode");
            return service;
        }
    }
}
