using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using ServiceBusExplorer.Models;

namespace ServiceBusExplorer.Services.Dataverse
{
    /// <summary>
    /// Reads the service endpoints of the organization, their steps and images, and what the step editor offers
    /// (messages, tables, columns).
    /// </summary>
    public sealed class EndpointService
    {
        private const int ChunkSize = 200;
        private const string DefaultSuffix = ".servicebus.windows.net";

        private readonly IOrganizationService _service;
        private readonly Dictionary<string, List<ColumnInfo>> _columns = new Dictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase);
        private List<KeyValuePair<Guid, string>> _messages;

        public EndpointService(IOrganizationService service)
        {
            _service = service;
        }

        #region Endpoints, steps, images

        public List<EndpointInfo> LoadEndpoints()
        {
            var query = new QueryExpression("serviceendpoint")
            {
                ColumnSet = new ColumnSet("name", "description", "contract", "namespaceaddress", "solutionnamespace", "path", "saskeyname", "authtype", "messageformat", "ismanaged")
            };
            return RetrieveAll(query).Select(e => new EndpointInfo
            {
                Id = e.Id,
                Name = e.GetAttributeValue<string>("name"),
                Description = e.GetAttributeValue<string>("description"),
                Contract = e.GetAttributeValue<OptionSetValue>("contract")?.Value ?? 0,
                Host = NormalizeHost(e.GetAttributeValue<string>("namespaceaddress"), e.GetAttributeValue<string>("solutionnamespace")),
                Path = (e.GetAttributeValue<string>("path") ?? string.Empty).Trim().Trim('/'),
                SasKeyName = e.GetAttributeValue<string>("saskeyname"),
                AuthType = e.GetAttributeValue<OptionSetValue>("authtype")?.Value ?? 0,
                MessageFormat = e.GetAttributeValue<OptionSetValue>("messageformat")?.Value ?? 0,
                IsManaged = e.GetAttributeValue<bool>("ismanaged")
            }).ToList();
        }

        /// <summary>
        /// Lower-case host of an endpoint: the address (sb://name.servicebus.windows.net/) or, for the older
        /// « namespace name » format, the name on the public cloud.
        /// </summary>
        public static string NormalizeHost(string address, string solutionNamespace)
        {
            address = address?.Trim();
            if (!string.IsNullOrEmpty(address))
            {
                if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)) return uri.Host.ToLowerInvariant();
                if (Uri.TryCreate("sb://" + address, UriKind.Absolute, out uri)) return uri.Host.ToLowerInvariant();
            }

            var name = solutionNamespace?.Trim();
            if (string.IsNullOrEmpty(name)) return null;
            return (name.Contains(".") ? name : name + DefaultSuffix).ToLowerInvariant();
        }

        public List<StepInfo> LoadSteps(IEnumerable<EndpointInfo> endpoints)
        {
            var steps = new List<StepInfo>();
            foreach (var chunk in Chunks(endpoints.Select(e => e.Id).ToList()))
            {
                var query = new QueryExpression("sdkmessageprocessingstep")
                {
                    ColumnSet = new ColumnSet("name", "description", "stage", "mode", "rank", "statecode", "filteringattributes", "asyncautodelete",
                        "eventhandler", "sdkmessageid", "sdkmessagefilterid", "ismanaged", "iscustomizable")
                };
                query.Criteria.AddCondition(new ConditionExpression("eventhandler", ConditionOperator.In, chunk.Cast<object>().ToArray()));
                var filter = query.AddLink("sdkmessagefilter", "sdkmessagefilterid", "sdkmessagefilterid", JoinOperator.LeftOuter);
                filter.EntityAlias = "f";
                filter.Columns = new ColumnSet("primaryobjecttypecode");

                steps.AddRange(RetrieveAll(query).Select(ToStep));
            }
            LoadImages(steps);
            return steps.OrderBy(s => s.Table, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Message, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static StepInfo ToStep(Entity e)
        {
            var message = e.GetAttributeValue<EntityReference>("sdkmessageid");
            var table = e.GetAttributeValue<AliasedValue>("f.primaryobjecttypecode")?.Value as string;
            return new StepInfo
            {
                Id = e.Id,
                Name = e.GetAttributeValue<string>("name"),
                Description = e.GetAttributeValue<string>("description"),
                EndpointId = e.GetAttributeValue<EntityReference>("eventhandler")?.Id ?? Guid.Empty,
                MessageId = message?.Id ?? Guid.Empty,
                Message = message?.Name,
                FilterId = e.GetAttributeValue<EntityReference>("sdkmessagefilterid")?.Id,
                Table = string.IsNullOrEmpty(table) || table == "none" ? null : table,
                Stage = e.GetAttributeValue<OptionSetValue>("stage")?.Value ?? 0,
                Mode = e.GetAttributeValue<OptionSetValue>("mode")?.Value ?? 0,
                Rank = e.GetAttributeValue<int>("rank"),
                Enabled = (e.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? 0) == 0,
                FilteringAttributes = e.GetAttributeValue<string>("filteringattributes"),
                DeleteJobOnSuccess = e.GetAttributeValue<bool>("asyncautodelete"),
                IsManaged = e.GetAttributeValue<bool>("ismanaged"),
                IsCustomizable = e.GetAttributeValue<BooleanManagedProperty>("iscustomizable")?.Value ?? true
            };
        }

        private void LoadImages(List<StepInfo> steps)
        {
            var byId = steps.ToDictionary(s => s.Id);
            foreach (var chunk in Chunks(steps.Select(s => s.Id).ToList()))
            {
                var query = new QueryExpression("sdkmessageprocessingstepimage")
                {
                    ColumnSet = new ColumnSet("name", "entityalias", "imagetype", "attributes", "messagepropertyname", "sdkmessageprocessingstepid")
                };
                query.Criteria.AddCondition(new ConditionExpression("sdkmessageprocessingstepid", ConditionOperator.In, chunk.Cast<object>().ToArray()));

                foreach (var image in RetrieveAll(query))
                {
                    var stepId = image.GetAttributeValue<EntityReference>("sdkmessageprocessingstepid")?.Id;
                    if (stepId == null || !byId.TryGetValue(stepId.Value, out var step)) continue;
                    step.Images.Add(new ImageInfo
                    {
                        Id = image.Id,
                        Name = image.GetAttributeValue<string>("name"),
                        Alias = image.GetAttributeValue<string>("entityalias"),
                        ImageType = image.GetAttributeValue<OptionSetValue>("imagetype")?.Value ?? 0,
                        Attributes = image.GetAttributeValue<string>("attributes"),
                        MessageProperty = image.GetAttributeValue<string>("messagepropertyname")
                    });
                }
            }
        }

        #endregion

        #region Step editor data

        /// <summary>Messages a step can be registered on, sorted by name.</summary>
        public List<KeyValuePair<Guid, string>> LoadMessages()
        {
            if (_messages != null) return _messages;

            // With Distinct, the key only comes back when it is requested: without it every Id would be empty.
            var query = new QueryExpression("sdkmessage") { ColumnSet = new ColumnSet("sdkmessageid", "name"), Distinct = true };
            var filter = query.AddLink("sdkmessagefilter", "sdkmessageid", "sdkmessageid", JoinOperator.Inner);
            filter.LinkCriteria.AddCondition("iscustomprocessingstepallowed", ConditionOperator.Equal, true);

            _messages = RetrieveAll(query)
                .Select(e => new KeyValuePair<Guid, string>(e.GetAttributeValue<Guid>("sdkmessageid"), e.GetAttributeValue<string>("name")))
                .Where(m => m.Key != Guid.Empty && !string.IsNullOrEmpty(m.Value))
                .OrderBy(m => m.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return _messages;
        }

        /// <summary>Tables of a message: logical name → sdkmessagefilter id. The key "" is the « all tables » filter.</summary>
        public Dictionary<string, Guid> LoadTables(Guid messageId)
        {
            var query = new QueryExpression("sdkmessagefilter") { ColumnSet = new ColumnSet("primaryobjecttypecode") };
            query.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, messageId);
            query.Criteria.AddCondition("iscustomprocessingstepallowed", ConditionOperator.Equal, true);

            var tables = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in RetrieveAll(query))
            {
                var table = row.GetAttributeValue<string>("primaryobjecttypecode");
                tables[table == "none" ? string.Empty : table ?? string.Empty] = row.Id;
            }
            return tables;
        }

        /// <summary>Readable columns of a table, cached for the connection.</summary>
        public List<ColumnInfo> LoadColumns(string table)
        {
            lock (_columns)
            {
                if (_columns.TryGetValue(table, out var cached)) return cached;
            }

            var response = (RetrieveEntityResponse)_service.Execute(new RetrieveEntityRequest
            {
                LogicalName = table,
                EntityFilters = EntityFilters.Attributes
            });

            var columns = response.EntityMetadata.Attributes
                .Where(a => a.IsValidForRead == true && a.AttributeOf == null && a.AttributeType != AttributeTypeCode.Virtual)
                .Select(a => new ColumnInfo
                {
                    LogicalName = a.LogicalName,
                    DisplayName = a.DisplayName?.UserLocalizedLabel?.Label ?? a.LogicalName,
                    Type = a.AttributeType?.ToString()
                })
                .OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            lock (_columns) _columns[table] = columns;
            return columns;
        }

        #endregion

        #region Writes

        /// <summary>Writes the changed attributes of a step, then its images (removed ones first).</summary>
        public void SaveStep(StepChange change)
        {
            var o = change.Original;
            var d = change.Draft;

            var update = new Entity("sdkmessageprocessingstep", o.Id);
            if (d.MessageId != o.MessageId) update["sdkmessageid"] = new EntityReference("sdkmessage", d.MessageId);
            if (d.FilterId != o.FilterId) update["sdkmessagefilterid"] = d.FilterId.HasValue ? new EntityReference("sdkmessagefilter", d.FilterId.Value) : null;
            if (d.Stage != o.Stage) update["stage"] = new OptionSetValue(d.Stage);
            if (d.Mode != o.Mode) update["mode"] = new OptionSetValue(d.Mode);
            if (d.Rank != o.Rank) update["rank"] = d.Rank;
            if (!string.Equals(d.FilteringAttributes ?? string.Empty, o.FilteringAttributes ?? string.Empty, StringComparison.Ordinal))
            {
                update["filteringattributes"] = string.IsNullOrEmpty(d.FilteringAttributes) ? null : d.FilteringAttributes;
            }
            if (d.DeleteJobOnSuccess != o.DeleteJobOnSuccess) update["asyncautodelete"] = d.DeleteJobOnSuccess;
            if (d.Name != o.Name) update["name"] = d.Name;
            if (d.Description != o.Description) update["description"] = d.Description;
            if (update.Attributes.Count > 0) _service.Update(update);

            foreach (var image in o.Images.Where(i => d.Images.All(x => x.Id != i.Id)))
            {
                _service.Delete("sdkmessageprocessingstepimage", image.Id);
            }

            var property = StepRules.ImageProperty(d.Message);
            foreach (var image in d.Images)
            {
                var before = o.Images.FirstOrDefault(i => i.Id == image.Id);
                if (before != null && StepRules.SameImage(before, image) && before.MessageProperty == property) continue;

                var entity = image.Id == Guid.Empty ? new Entity("sdkmessageprocessingstepimage") : new Entity("sdkmessageprocessingstepimage", image.Id);
                entity["name"] = image.Name;
                entity["entityalias"] = image.Alias;
                entity["imagetype"] = new OptionSetValue(image.ImageType);
                entity["attributes"] = image.AllColumns ? null : StepRules.NormalizeColumns(image.Attributes);
                entity["messagepropertyname"] = property;

                if (image.Id == Guid.Empty)
                {
                    entity["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", o.Id);
                    _service.Create(entity);
                }
                else
                {
                    _service.Update(entity);
                }
            }
        }

        public void SetEnabled(StepInfo step, bool enabled)
        {
            var update = new Entity("sdkmessageprocessingstep", step.Id)
            {
                ["statecode"] = new OptionSetValue(enabled ? 0 : 1),
                ["statuscode"] = new OptionSetValue(enabled ? 1 : 2)
            };
            _service.Update(update);
        }

        #endregion

        private static IEnumerable<List<Guid>> Chunks(List<Guid> ids)
        {
            for (var i = 0; i < ids.Count; i += ChunkSize) yield return ids.GetRange(i, Math.Min(ChunkSize, ids.Count - i));
        }

        private IEnumerable<Entity> RetrieveAll(QueryExpression query)
        {
            query.PageInfo = new PagingInfo { PageNumber = 1, Count = 5000 };
            while (true)
            {
                var page = _service.RetrieveMultiple(query);
                foreach (var entity in page.Entities) yield return entity;
                if (!page.MoreRecords) yield break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
        }
    }
}
