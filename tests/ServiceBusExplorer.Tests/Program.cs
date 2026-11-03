using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json.Linq;
using ServiceBusExplorer.Helpers;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.Models;
using ServiceBusExplorer.Services;
using ServiceBusExplorer.Services.Azure;
using ServiceBusExplorer.Services.Dataverse;

namespace ServiceBusExplorer.Tests
{
    /// <summary>
    /// Logic tests (run with no argument), or « --screens folder » to drive the real UI and capture it.
    /// </summary>
    internal static class Program
    {
        private static int _passed;
        private static readonly List<string> Failures = new List<string>();

        private static void Check(string name, bool condition)
        {
            if (condition) _passed++;
            else Failures.Add(name);
        }

        private static void Same(string name, object expected, object actual) => Check($"{name}: expected <{expected}>, got <{actual}>", Equals(expected, actual));

        private static void Throws<T>(string name, Action action) where T : Exception
        {
            try
            {
                action();
                Failures.Add(name + ": no exception");
            }
            catch (T)
            {
                _passed++;
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--screens") return Screens.Run(args[1]);
            if (args.Length == 1 && args[0] == "--ui") return UiTests.Run();
            if (args.Length == 1 && args[0] == "--mef") return RealChecks.Mef();
            if (args.Length == 2 && args[0] == "--real") return RealChecks.Organization(args[1]);
            if (args.Length >= 1 && args[0] == "--emulator") return EmulatorChecks.Run(args.Length > 1 ? args[1] : EmulatorChecks.DefaultConnectionString);

            Run(Atom);
            Run(Arm);
            Run(Connection);
            Run(Hosts);
            Run(Matching);
            Run(Rules);
            Run(Formats);
            Run(Secrets);
            Run(EndToEnd);
            Run(Refused);
            Run(Paging);
            Run(SaveOnlyChanges);
            Run(SaveImages);
            Run(Export);
            Run(ArmReading);
            Run(Decoding);
            Run(MessageReading);

            Console.WriteLine($"{_passed} checks passed, {Failures.Count} failed");
            foreach (var failure in Failures) Console.WriteLine("FAIL " + failure);
            return Failures.Count == 0 ? 0 : 1;
        }

        private static void Run(Action test)
        {
            try
            {
                test();
            }
            catch (Exception error)
            {
                Failures.Add($"{test.Method.Name} crashed: {error}");
            }
        }

        private static void Atom()
        {
            var queue = AtomParser.ParseEntities(AtomSamples.Feed(AtomSamples.Queue("q1", 5, 2, forwardTo: "next")), SbKind.Queue).Single();
            Same("name", "q1", queue.Name);
            Same("active is read through the other XML namespace", 5L, queue.ActiveMessages);
            Same("dead-letter", 2L, queue.DeadLetterMessages);
            Same("scheduled", 2L, queue.ScheduledMessages);
            Same("forward", "next", queue.ForwardTo);
            Check("session", queue.RequiresSession);
            Same("0001 date is empty", null, queue.LastAccessedOn);
            Same("created is UTC", 2026, queue.CreatedOn?.Year);

            var topic = AtomParser.ParseEntities(AtomSamples.Feed(AtomSamples.Topic("t1", 3)), SbKind.Topic).Single();
            Same("subscription count", 3, topic.SubscriptionCount);
            Check("partitioned", topic.Partitioned);

            var rules = AtomParser.ParseRules(AtomSamples.Feed(AtomSamples.SqlRule("a", "x = 1", "SET y = 2"), AtomSamples.TrueRule("$Default"), AtomSamples.CorrelationRule("c", "Create", "entity", "contact")));
            Same("rule count", 3, rules.Count);
            Same("sql", RuleFilterKind.Sql, rules[0].FilterKind);
            Same("sql expression", "x = 1", rules[0].Expression);
            Same("sql action", "SET y = 2", rules[0].ActionExpression);
            Same("true filter", RuleFilterKind.True, rules[1].FilterKind);
            Same("empty action", null, rules[1].ActionExpression);
            Same("correlation", RuleFilterKind.Correlation, rules[2].FilterKind);
            Same("correlation conditions", "Subject = 'Create' AND ContentType = 'application/json' AND entity = 'contact'", rules[2].Expression);
        }

        private static void Arm()
        {
            // Sample of the Rules - List By Subscriptions page.
            var sample = JObject.Parse("{\"name\":\"sdk-Rules-6571\",\"type\":\"Microsoft.ServiceBus/Namespaces/Topics/Subscriptions/Rules\",\"properties\":{\"action\":{},\"filterType\":\"SqlFilter\",\"sqlFilter\":{\"compatibilityLevel\":20,\"sqlExpression\":\"1=1\"}}}");
            var rule = ArmParser.ToRule(sample);
            Same("arm rule", "sdk-Rules-6571", rule.Name);
            Same("arm true", RuleFilterKind.True, rule.FilterKind);

            var correlation = ArmParser.ToRule(JObject.Parse("{\"name\":\"c\",\"properties\":{\"filterType\":\"CorrelationFilter\",\"correlationFilter\":{\"label\":\"Create\",\"correlationId\":\"42\",\"properties\":{\"entity\":\"contact\"}}}}"));
            Same("arm correlation", "CorrelationId = '42' AND Subject = 'Create' AND entity = 'contact'", correlation.Expression);

            var queue = ArmParser.ToEntity(JObject.Parse("{\"name\":\"q\",\"properties\":{\"status\":\"Active\",\"messageCount\":7,\"sizeInBytes\":10,\"countDetails\":{\"activeMessageCount\":5,\"deadLetterMessageCount\":2},\"lockDuration\":\"PT1M\",\"createdAt\":\"2026-01-02T03:04:05Z\",\"accessedAt\":\"0001-01-01T00:00:00\",\"forwardTo\":\"\"}}"), SbKind.Queue);
            Same("arm active", 5L, queue.ActiveMessages);
            Same("arm dead", 2L, queue.DeadLetterMessages);
            Same("arm created", 2026, queue.CreatedOn?.Year);
            Same("arm empty date", null, queue.LastAccessedOn);
            Same("arm empty forward", null, queue.ForwardTo);

            Same("segment", "rg1", ArmParser.Segment("/subscriptions/1/resourceGroups/rg1/providers/x", "resourcegroups"));
            Same("host", "ns.servicebus.windows.net", ArmParser.HostOf(JObject.Parse("{\"properties\":{\"serviceBusEndpoint\":\"https://NS.servicebus.windows.net:443/\"}}")));
        }

        private static void Connection()
        {
            var info = ServiceBusConnectionInfo.Parse("Endpoint=sb://NS.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=abc=def=");
            Same("host", "ns.servicebus.windows.net", info.Host);
            Same("key keeps its equal signs", "abc=def=", info.Key);
            Same("https", "https://ns.servicebus.windows.net/", info.ManagementUri.AbsoluteUri);
            Same("entity path", "q1", ServiceBusConnectionInfo.Parse("Endpoint=sb://n.servicebus.windows.net/;SharedAccessKeyName=a;SharedAccessKey=b;EntityPath=q1").EntityPath);
            Same("emulator port", "http://localhost:5300/", ServiceBusConnectionInfo.Parse("Endpoint=sb://localhost;SharedAccessKeyName=a;SharedAccessKey=b;UseDevelopmentEmulator=true").ManagementUri.AbsoluteUri);
            Throws<FormatException>("no endpoint", () => ServiceBusConnectionInfo.Parse("hello"));
            Throws<FormatException>("no key", () => ServiceBusConnectionInfo.Parse("Endpoint=sb://n.servicebus.windows.net/;SharedAccessKeyName=a"));

            // Vector computed independently (Python hmac/sha256/base64).
            var token = ServiceBusConnectionInfo.CreateToken("https://ns.servicebus.windows.net/", "Root", "abc", DateTimeOffset.FromUnixTimeSeconds(1700000000));
            Same("sas token", "SharedAccessSignature sr=https%3A%2F%2Fns.servicebus.windows.net%2F&sig=0h9w6ZwzGVEBfLeg4Que0uobKoXFmHnfJrX4aq3RN2U%3D&se=1700000000&skn=Root", token);
        }

        private static void Hosts()
        {
            Same("address", "ns.servicebus.windows.net", EndpointService.NormalizeHost("sb://NS.servicebus.windows.net/", null));
            Same("https address", "ns.servicebus.windows.net", EndpointService.NormalizeHost("https://ns.servicebus.windows.net:443/", null));
            Same("no scheme", "ns.servicebus.windows.net", EndpointService.NormalizeHost("ns.servicebus.windows.net", null));
            Same("namespace name", "ns.servicebus.windows.net", EndpointService.NormalizeHost(null, "NS"));
            Same("full name", "ns.servicebus.usgovcloudapi.net", EndpointService.NormalizeHost(" ", "ns.servicebus.usgovcloudapi.net"));
            Same("blank (Managed Data Lake row of a real org)", null, EndpointService.NormalizeHost(null, " "));
        }

        private static EndpointInfo Endpoint(string name, int contract, string host, string path) =>
            new EndpointInfo { Id = Guid.NewGuid(), Name = name, Contract = contract, Host = host, Path = path };

        private static StepInfo StepOn(EndpointInfo endpoint) => new StepInfo { Id = Guid.NewGuid(), EndpointId = endpoint.Id, Name = "s", Enabled = true };

        private static void Matching()
        {
            var ns = new SbNamespace { Host = "a.servicebus.windows.net" };
            var queue = new SbEntity { Kind = SbKind.Queue, Name = "Q1", Namespace = ns };
            var unused = new SbEntity { Kind = SbKind.Queue, Name = "unused", Namespace = ns };
            var topic = new SbEntity { Kind = SbKind.Topic, Name = "t1", Namespace = ns };
            ns.Queues.AddRange(new[] { queue, unused });
            ns.Topics.Add(topic);
            var failed = new SbNamespace { Host = "broken.servicebus.windows.net", Error = new Exception("401") };

            var linked = Endpoint("linked", EndpointContract.Queue, "A.servicebus.windows.net".ToLowerInvariant(), "q1");
            var noSteps = Endpoint("nosteps", EndpointContract.Topic, ns.Host, "t1");
            var missing = Endpoint("missing", EndpointContract.Queue, ns.Host, "gone");
            var kind = Endpoint("kind", EndpointContract.Topic, ns.Host, "q1");
            var ghost = Endpoint("ghost", EndpointContract.Topic, "other.servicebus.windows.net", "x");
            var broken = Endpoint("broken", EndpointContract.Queue, failed.Host, "y");
            var webhook = Endpoint("hook", EndpointContract.Webhook, null, null);
            var endpoints = new List<EndpointInfo> { linked, noSteps, missing, kind, ghost, broken, webhook };
            var steps = new List<StepInfo> { StepOn(linked), StepOn(kind), StepOn(ghost) };

            var model = MatchingService.Build(new[] { ns, failed }, endpoints, steps);
            MatchStatus Status(string name) => model.Rows.Single(r => r.Endpoint?.Name == name).Status;
            Same("case-insensitive path", MatchStatus.Linked, Status("linked"));
            Same("no steps", MatchStatus.NoSteps, Status("nosteps"));
            Same("missing", MatchStatus.MissingOnAzure, Status("missing"));
            Same("kind", MatchStatus.TypeMismatch, Status("kind"));
            Same("not connected", MatchStatus.NotConnected, Status("ghost"));
            Same("unreadable namespace", MatchStatus.NotConnected, Status("broken"));
            Same("webhook", MatchStatus.OtherType, Status("hook"));
            Same("azure without endpoint", "unused", model.Rows.Single(r => r.Status == MatchStatus.NoEndpoint).Entity.Name);
            Check("two endpoints on one entity", queue.Endpoints.Count == 2);
            Check("ghost namespace created", model.Namespaces.Any(n => n.IsGhost && n.Host == "other.servicebus.windows.net"));
            Check("ghost entity on connected namespace", ns.Queues.Any(q => q.IsGhost && q.Name == "gone"));
            Check("ghost keeps the steps visible", model.Namespaces.First(n => n.IsGhost).Topics[0].Steps.Count() == 1);

            // Second pass after a save: no duplicate rows, ghosts or endpoints.
            var again = MatchingService.Build(model.Namespaces, endpoints, steps);
            Same("rebuild keeps the rows", model.Rows.Count, again.Rows.Count);
            Same("rebuild keeps one ghost", 1, ns.Queues.Count(q => q.IsGhost));
            Same("rebuild clears the links", 2, queue.Endpoints.Count);
            Same("rebuild keeps the steps", 1, linked.Steps.Count);

            var subscription = new SbEntity { Kind = SbKind.Subscription, Name = "s", Topic = topic, Namespace = ns };
            Same("subscription shows its topic's steps", topic.Steps.Count(), subscription.Steps.Count());
        }

        private static StepInfo Valid() => new StepInfo { Message = "Update", Table = "account", Stage = StepStage.PostOperation, Mode = StepMode.Asynchronous, Rank = 1 };

        private static void Rules()
        {
            Check("valid", StepRules.Validate(Valid()).Count == 0);

            var step = Valid();
            step.Stage = StepStage.PreOperation;
            Check("async is post only", StepRules.Validate(step).Contains(Strings.RuleAsyncPostOnly));

            step = Valid();
            step.Message = "Create";
            step.FilteringAttributes = "name";
            Check("filtering is Update only", StepRules.Validate(step).Contains(Strings.RuleFilteringUpdateOnly));

            step = Valid();
            step.Message = "Create";
            step.Images.Add(new ImageInfo { Alias = "a", ImageType = ImageKind.Pre });
            Check("no pre image on Create", StepRules.Validate(step).Count == 1);

            step = Valid();
            step.Message = "Delete";
            step.Images.Add(new ImageInfo { Alias = "a", ImageType = ImageKind.Post });
            Check("no post image on Delete", StepRules.Validate(step).Count == 1);

            step = Valid();
            step.Mode = StepMode.Synchronous;
            step.Stage = StepStage.PreOperation;
            step.Images.Add(new ImageInfo { Alias = "a", ImageType = ImageKind.Post });
            Check("no post image before the operation", StepRules.Validate(step).Count == 1);

            step = Valid();
            step.Message = "RetrieveMultiple";
            step.Images.Add(new ImageInfo { Alias = "a" });
            Same("no images on this message", 1, StepRules.Validate(step).Count);

            step = Valid();
            step.Images.Add(new ImageInfo { Alias = "A" });
            step.Images.Add(new ImageInfo { Alias = "a" });
            Check("duplicate alias", StepRules.Validate(step).Count == 1);

            Same("create property", "id", StepRules.ImageProperty("Create"));
            Same("update property", "Target", StepRules.ImageProperty("Update"));
            Same("setstate property", "entityMoniker", StepRules.ImageProperty("SetState"));
            Same("allowed on Update post", 3, StepRules.AllowedImageTypes("Update", StepStage.PostOperation).Count);
            Same("columns", "a,b", StepRules.NormalizeColumns(" B, a ,,b"));

            var original = Valid();
            original.Images.Add(new ImageInfo { Id = Guid.NewGuid(), Alias = "old", Name = "old", ImageType = ImageKind.Pre });
            var draft = original.Clone();
            Same("no change", 0, StepRules.Describe(new StepChange { Original = original, Draft = draft }).Count);
            draft.FilteringAttributes = "name";
            draft.Images.Clear();
            draft.Images.Add(new ImageInfo { Alias = "new", Name = "new" });
            Same("filtering, image added, image removed", 3, StepRules.Describe(new StepChange { Original = original, Draft = draft }).Count);
        }

        private static void Formats()
        {
            Same("infinite", Strings.Unlimited, Format.Duration("P10675199DT2H48M5.4775807S"));
            Same("days", Strings.Days(14), Format.Duration("P14D"));
            Same("minutes", Strings.Minutes(1), Format.Duration("PT1M"));
            Same("garbage", "soon", Format.Duration("soon"));
            Same("size", "2 KB", Format.Size(2048));
        }

        private static void Secrets()
        {
            var protectedValue = SecretStore.Protect("Endpoint=sb://x/;SharedAccessKey=secret");
            Check("no key in clear", !protectedValue.Contains("secret"));
            Same("round trip", "Endpoint=sb://x/;SharedAccessKey=secret", SecretStore.Unprotect(protectedValue));
        }

        private static void EndToEnd()
        {
            using (var bus = new MockServiceBus())
            {
                Demo.Fill(bus);
                var reader = new SasServiceBusReader(ServiceBusConnectionInfo.Parse(bus.ConnectionString));
                var ns = NamespaceLoader.Load(reader, 4, _ => { }, CancellationToken.None);
                Same("queues", 4, ns.Queues.Count);
                Same("topics", 3, ns.Topics.Count);
                Same("subscriptions", 5, ns.AllSubscriptions.Count());
                Same("rules of a subscription", 2, ns.Topics.First().Subscriptions.First(s => s.Name == "marketing-platform").Rules.Count);
                Check("signed requests", bus.LastAuthorization.Contains("&skn=RootManageSharedAccessKey"));

                var organization = Demo.Organization("localhost");
                var service = new EndpointService(organization);
                var endpoints = service.LoadEndpoints();
                var steps = service.LoadSteps(endpoints);
                Same("endpoints", 9, endpoints.Count);
                Same("steps", 11, steps.Count);
                Same("images", 4, steps.Sum(s => s.Images.Count));
                Same("table of a step", "account", steps.First(s => s.Images.Count == 2).Table);
                Same("trimmed path", "crm-invoice-events", endpoints.First(e => e.Name == "Invoice events").Path);

                var model = MatchingService.Build(new[] { ns }, endpoints, steps);
                Same("linked", 4, model.Rows.Count(r => r.Status == MatchStatus.Linked));
                Same("contact steps on the topic", 4, ns.Topics.First(t => t.Name == "crm-contact-changes").Steps.Count());
                Same("missing", "crm-quote-events", model.Rows.Single(r => r.Status == MatchStatus.MissingOnAzure).Path);
            }
        }

        private static void Refused()
        {
            using (var bus = new MockServiceBus { RefuseAll = true })
            {
                var reader = new SasServiceBusReader(ServiceBusConnectionInfo.Parse(bus.ConnectionString));
                try
                {
                    NamespaceLoader.Load(reader, 4, _ => { }, CancellationToken.None);
                    Failures.Add("refused: no exception");
                }
                catch (AzureRequestException error)
                {
                    Same("401 is readable", 401, error.StatusCode);
                    Check("message explains the right", error.Message.Contains("Manage"));
                }
            }

            var dead = ServiceBusConnectionInfo.Parse("Endpoint=sb://localhost:1/;SharedAccessKeyName=a;SharedAccessKey=b;UseDevelopmentEmulator=true");
            Throws<AzureRequestException>("unreachable", () => new SasServiceBusReader(dead).ReadQueues(CancellationToken.None));

            using (var bus = new MockServiceBus())
            using (var stop = new CancellationTokenSource())
            {
                stop.Cancel();
                Throws<OperationCanceledException>("cancelled", () => new SasServiceBusReader(ServiceBusConnectionInfo.Parse(bus.ConnectionString)).ReadQueues(stop.Token));
            }
        }

        private static void Paging()
        {
            using (var bus = new MockServiceBus())
            {
                bus.RoutePaged("/$Resources/queues", skip =>
                {
                    var count = skip < 200 ? 100 : 37;
                    return AtomSamples.Feed(Enumerable.Range(skip, count).Select(i => AtomSamples.Queue("q" + i, 0, 0)).ToArray());
                });
                var queues = new SasServiceBusReader(ServiceBusConnectionInfo.Parse(bus.ConnectionString)).ReadQueues(CancellationToken.None);
                Same("three pages", 237, queues.Count);
                Same("no duplicates", 237, queues.Select(q => q.Name).Distinct().Count());
            }
        }

        private static void SaveOnlyChanges()
        {
            var organization = Demo.Organization("localhost");
            var service = new EndpointService(organization);
            var step = service.LoadSteps(service.LoadEndpoints()).First(s => s.Images.Count == 2);

            var draft = step.Clone();
            service.SaveStep(new StepChange { Original = step, Draft = draft });
            Same("nothing changed, nothing sent", 0, organization.Updates.Count);

            draft.FilteringAttributes = "name";
            draft.Rank = 5;
            service.SaveStep(new StepChange { Original = step, Draft = draft });
            var update = organization.Updates.Single();
            Same("only the two changed attributes", 2, update.Attributes.Count);
            Same("filtering value", "name", update["filteringattributes"]);
            Same("rank value", 5, update["rank"]);

            organization.Updates.Clear();
            var clearing = step.Clone();
            clearing.FilteringAttributes = null;
            service.SaveStep(new StepChange { Original = step, Draft = clearing });
            Check("clearing writes null", organization.Updates.Single().Attributes.Contains("filteringattributes") && organization.Updates.Single()["filteringattributes"] == null);

            organization.Updates.Clear();
            service.SetEnabled(step, false);
            Same("disabled state", 1, organization.Updates.Single().GetAttributeValue<OptionSetValue>("statecode").Value);
            Same("disabled status", 2, organization.Updates.Single().GetAttributeValue<OptionSetValue>("statuscode").Value);
        }

        private static void SaveImages()
        {
            var organization = Demo.Organization("localhost");
            var service = new EndpointService(organization);
            var step = service.LoadSteps(service.LoadEndpoints()).First(s => s.Images.Count == 2);

            var draft = step.Clone();
            var removed = draft.Images[0];
            var changed = draft.Images[1];
            draft.Images.Remove(removed);
            changed.Attributes = "telephone1, NAME";
            draft.Images.Add(new ImageInfo { Alias = "Added", Name = "Added", ImageType = ImageKind.Pre, Attributes = "name" });

            service.SaveStep(new StepChange { Original = step, Draft = draft });
            Same("deleted image", removed.Id, organization.Deletes.Single());
            Same("updated image columns are normalized", "name,telephone1", organization.Updates.Single()["attributes"]);
            var created = organization.Creates.Single();
            Same("created on the step", step.Id, created.GetAttributeValue<EntityReference>("sdkmessageprocessingstepid").Id);
            Same("message property of Update", "Target", created["messagepropertyname"]);
            Same("alias", "Added", created["entityalias"]);

            // Images of a step moved to another message follow its input parameter.
            organization.Updates.Clear();
            var original = service.LoadSteps(service.LoadEndpoints()).First(s => s.Message == "Create" && s.Images.Count == 1);
            var moved = original.Clone();
            service.SaveStep(new StepChange { Original = original, Draft = moved });
            Same("Create image keeps id", "id", organization.Updates.Single()["messagepropertyname"]);
        }

        private sealed class FixedToken : IArmTokenProvider
        {
            public string GetToken(string tenantId, string resource, CancellationToken token) => "fake";
        }

        /// <summary>The Microsoft account path, against a local server answering like Azure Resource Manager.</summary>
        private static void ArmReading()
        {
            using (var arm = new MockServiceBus())
            {
                var previous = ArmClient.Root;
                ArmClient.Root = arm.Url;
                try
                {
                    const string ns = "/subscriptions/s1/resourceGroups/rg1/providers/Microsoft.ServiceBus/namespaces/ns1";
                    arm.Json["/tenants"] = "{\"value\":[{\"tenantId\":\"t1\"},{\"tenantId\":\"t2\"}]}";
                    arm.Json["/subscriptions"] = "{\"value\":[{\"subscriptionId\":\"s1\",\"displayName\":\"Prod\"}]}";
                    arm.Json["/subscriptions/s1/providers/Microsoft.ServiceBus/namespaces"] =
                        "{\"value\":[{\"id\":\"" + ns + "\",\"name\":\"ns1\",\"location\":\"West Europe\",\"sku\":{\"name\":\"Standard\"},\"properties\":{\"serviceBusEndpoint\":\"https://ns1.servicebus.windows.net:443/\",\"status\":\"Active\"}}]}";
                    arm.Json[ns] = "{\"id\":\"" + ns + "\",\"location\":\"West Europe\",\"sku\":{\"name\":\"Standard\"},\"properties\":{\"status\":\"Active\"}}";
                    arm.Json[ns + "/queues"] = "{\"value\":[{\"name\":\"q1\",\"properties\":{\"status\":\"Active\",\"countDetails\":{\"activeMessageCount\":4}}}],\"nextLink\":\"" + arm.Url + ns + "/queues?page=2\"}";
                    arm.Json[ns + "/queues?page=2"] = "{\"value\":[{\"name\":\"q2\",\"properties\":{\"status\":\"Active\"}}]}";
                    arm.Json[ns + "/topics"] = "{\"value\":[{\"name\":\"t1\",\"properties\":{\"status\":\"Active\",\"subscriptionCount\":1}}]}";
                    arm.Json[ns + "/topics/t1/subscriptions"] = "{\"value\":[{\"name\":\"s1\",\"properties\":{\"status\":\"Active\",\"messageCount\":2}}]}";
                    arm.Json[ns + "/topics/t1/subscriptions/s1/rules"] = "{\"value\":[{\"name\":\"r1\",\"properties\":{\"filterType\":\"SqlFilter\",\"sqlFilter\":{\"sqlExpression\":\"a = 1\"}}}]}";

                    var found = new ArmClient(new FixedToken()).DiscoverNamespaces(null, _ => { }, CancellationToken.None);
                    Same("each tenant is read", 2, found.Count);
                    Same("discovered host", "ns1.servicebus.windows.net", found[0].Host);
                    Same("resource group", "rg1", found[0].ResourceGroup);

                    var read = NamespaceLoader.Load(new ArmServiceBusReader(new FixedToken(), found[0].Host, ns, "t1"), 4, _ => { }, CancellationToken.None);
                    Same("arm follows nextLink", 2, read.Queues.Count);
                    Same("arm counters", 4L, read.Queues[0].ActiveMessages);
                    Same("arm rule", "a = 1", read.Topics[0].Subscriptions[0].Rules[0].Expression);
                    Same("arm tier", "Standard", read.Sku);
                    Check("arm is not a key access", !read.ReadWithKey);

                    arm.Json.Remove(ns + "/queues");
                    try
                    {
                        NamespaceLoader.Load(new ArmServiceBusReader(new FixedToken(), found[0].Host, ns, "t1"), 4, _ => { }, CancellationToken.None);
                        Failures.Add("arm 404: no exception");
                    }
                    catch (AzureRequestException error)
                    {
                        Same("arm error status", 404, error.StatusCode);
                        Check("arm error detail", error.Details != null && error.Details.Contains("nothing at"));
                    }
                }
                finally
                {
                    ArmClient.Root = previous;
                }
            }
        }

        /// <summary>The three formats of a Dataverse endpoint, serialized by the real SDK, and what is not Dataverse.</summary>
        private static void Decoding()
        {
            var step = Guid.NewGuid();
            var record = Guid.NewGuid();
            var context = DataverseSamples.Context(step, "Update of account", "Update", "account", record);

            foreach (var (format, body, type) in new[]
                     {
                         (MessageDecoder.FormatBinary, DataverseSamples.Binary(context), "application/msbin1"),
                         (MessageDecoder.FormatXml, DataverseSamples.Xml(context), "application/xml"),
                         (MessageDecoder.FormatJson, DataverseSamples.Json(context), "application/json"),
                         (MessageDecoder.FormatBinary, DataverseSamples.Binary(context), null)
                     })
            {
                var message = new SbMessage { Body = body, ContentType = type };
                MessageDecoder.Decode(message);
                var name = $"{format} ({type ?? "no content type"})";
                Same(name + " format", format, message.BodyFormat);
                Check(name + " is a context", message.Context != null);
                if (message.Context == null) continue;
                Same(name + " step", step, message.Context.StepId);
                Same(name + " step name", "Update of account", message.Context.StepName);
                Same(name + " message", "Update", message.Context.MessageName);
                Same(name + " table", "account", message.Context.Table);
                Same(name + " record", record, message.Context.RecordId);
                Same(name + " stage", 40, message.Context.Stage);
                Same(name + " mode", 1, message.Context.Mode);
                Same(name + " organization", "contoso", message.Context.Organization);
                Same(name + " created on", DataverseSamples.CreatedOn, message.Context.OperationCreatedOn);
                Check(name + " body is readable", message.BodyText.Contains("Contoso Ltd") && message.BodyText.Contains("OwningExtension"));
            }

            var other = new SbMessage { Body = Bytes.Utf8("{\"orderId\": 42, \"MessageName\": null}"), ContentType = "application/json" };
            MessageDecoder.Decode(other);
            Same("other JSON format", MessageDecoder.FormatJson, other.BodyFormat);
            Check("other JSON is not a context", other.Context == null);
            Check("JSON is indented", other.BodyText.Contains(Environment.NewLine));

            var text = new SbMessage { Body = Bytes.Utf8("﻿hello world") };
            MessageDecoder.Decode(text);
            Same("text", MessageDecoder.FormatText, text.BodyFormat);
            Same("text without BOM", "hello world", text.BodyText);

            var raw = new SbMessage { Body = new byte[] { 0x00, 0xFF, 0x10, 0x80, 0x41 } };
            MessageDecoder.Decode(raw);
            Same("raw", MessageDecoder.FormatRaw, raw.BodyFormat);
            Check("hex dump", raw.BodyText.StartsWith("000000  00 FF 10 80 41"));

            var broken = new SbMessage { Body = Bytes.Utf8("<not xml") };
            MessageDecoder.Decode(broken);
            Same("broken XML stays text", MessageDecoder.FormatText, broken.BodyFormat);

            var empty = new SbMessage { Body = new byte[0] };
            MessageDecoder.Decode(empty);
            Same("empty", null, empty.BodyFormat);
        }

        /// <summary>Several subscriptions, both sub-queues, the limit, the steps, and a namespace that refuses.</summary>
        private static void MessageReading()
        {
            var ns = new SbNamespace { Host = "localhost" };
            var topic = new SbEntity { Kind = SbKind.Topic, Name = "crm", Namespace = ns };
            var all = new SbEntity { Kind = SbKind.Subscription, Name = "all", Topic = topic, Namespace = ns };
            var dw = new SbEntity { Kind = SbKind.Subscription, Name = "dw", Topic = topic, Namespace = ns };
            topic.Subscriptions.AddRange(new[] { dw, all });
            ns.Topics.Add(topic);
            var queue = new SbEntity { Kind = SbKind.Queue, Name = "q", Namespace = ns };
            ns.Queues.Add(queue);

            Same("a topic is read through its subscriptions", "all,dw", string.Join(",", MessageService.SourcesOf(topic).Select(s => s.Name)));
            Same("a queue is its own source", queue, MessageService.SourcesOf(queue).Single());
            Same("a ghost has no source", 0, MessageService.SourcesOf(new SbEntity { Kind = SbKind.Queue, Name = "g", Namespace = ns, IsGhost = true }).Count);
            Same("subscription path", "crm/Subscriptions/all", MessageService.PathOf(all));

            var stepA = new StepInfo { Id = Guid.NewGuid(), Name = "A" };
            var stepB = new StepInfo { Id = Guid.NewGuid(), Name = "B" };
            var reader = new FakeMessageReader();
            for (var i = 0; i < 5; i++) reader.Add("crm/Subscriptions/all", DataverseSamples.Binary(DataverseSamples.Context(stepA.Id, "A", "Create", "account", Guid.NewGuid())), "application/msbin1");
            reader.Add("crm/Subscriptions/all/$DeadLetterQueue", DataverseSamples.Json(DataverseSamples.Context(stepB.Id, "B", "Update", "contact", Guid.NewGuid())), "application/json", 10, "MaxDeliveryCountExceeded", "10 attempts");
            reader.Add("crm/Subscriptions/dw/$DeadLetterQueue", DataverseSamples.Json(DataverseSamples.Context(Guid.NewGuid(), "Other org step", "Delete", "lead", Guid.NewGuid())), "application/json");
            var progress = new List<string>();

            var batches = MessageService.Read(MessageService.SourcesOf(topic), true, true, 3, _ => reader, progress.Add, CancellationToken.None);
            Same("one batch per subscription and sub-queue", 4, batches.Count);
            Same("asks one more to know it is cut", "crm/Subscriptions/all", reader.Calls[0]);
            var active = batches.Single(b => b.Source == all && !b.DeadLetter);
            Same("limit applied", 3, active.Messages.Count);
            Check("limit reported", active.Truncated);
            Check("not cut when under the limit", !batches.Single(b => b.Source == all && b.DeadLetter).Truncated);
            Check("messages are decoded", active.Messages.All(m => m.Context?.MessageName == "Create" && m.Source == all && !m.DeadLetter));
            var dead = batches.Single(b => b.Source == all && b.DeadLetter).Messages.Single();
            Check("dead-letter flag", dead.DeadLetter);
            Same("dead-letter reason", "MaxDeliveryCountExceeded", dead.DeadLetterReason);
            Check("progress per sub-queue", progress.Count == 4 && progress.Any(p => p.Contains("dw · DLQ")));
            Check("reader disposed", reader.Disposed);

            MessageService.ResolveSteps(batches, new[] { stepA, stepB });
            Check("steps found from the context", active.Messages.All(m => m.Step == stepA) && dead.Step == stepB);
            Check("unknown step stays unlinked", batches.Single(b => b.Source == dw && b.DeadLetter).Messages.Single().Step == null);

            var activeOnly = MessageService.Read(new[] { queue }, true, false, 10, _ => new FakeMessageReader(), null, CancellationToken.None);
            Check("active only", activeOnly.Count == 1 && !activeOnly[0].DeadLetter);

            var refused = new FakeMessageReader { Error = new AzureRequestException(401, Strings.MessagesAccessDenied("localhost")) };
            var failed = MessageService.Read(MessageService.SourcesOf(topic), true, true, 10, _ => refused, null, CancellationToken.None);
            Check("refusal on every batch", failed.All(b => b.Error != null && ((AzureRequestException)b.Error).StatusCode == 401));
            Same("a refused namespace is asked once", 1, refused.Calls.Count);

            var unknown = new FakeMessageReader { Error = new AzureRequestException(404, Strings.AzureNotFound) };
            var missing = MessageService.Read(MessageService.SourcesOf(topic), true, false, 10, _ => unknown, null, CancellationToken.None);
            Same("a missing entity does not stop the others", 2, unknown.Calls.Count);

            var noConnection = MessageService.Read(new[] { queue }, true, true, 10, _ => throw new AzureRequestException(0, "not connected"), null, CancellationToken.None);
            Check("no connection fills every batch", noConnection.Count == 2 && noConnection.All(b => b.Error?.Message == "not connected"));

            using (var stop = new CancellationTokenSource())
            {
                stop.Cancel();
                Throws<OperationCanceledException>("stopped", () => MessageService.Read(new[] { queue }, true, true, 10, _ => new FakeMessageReader(), null, stop.Token));
            }
        }

        private static void Export()
        {
            using (var bus = new MockServiceBus())
            {
                Demo.Fill(bus);
                var ns = NamespaceLoader.Load(new SasServiceBusReader(ServiceBusConnectionInfo.Parse(bus.ConnectionString)), 4, _ => { }, CancellationToken.None);
                var service = new EndpointService(Demo.Organization("localhost"));
                var endpoints = service.LoadEndpoints();
                var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
                ExportService.Export(path, MatchingService.Build(new[] { ns }, endpoints, service.LoadSteps(endpoints)));
                using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
                {
                    Same("six sheets", 6, zip.Entries.Count(e => e.FullName.StartsWith("xl/worksheets/sheet")));
                }
                File.Delete(path);
            }
        }
    }
}
