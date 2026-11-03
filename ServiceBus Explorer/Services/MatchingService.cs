using System;
using System.Collections.Generic;
using System.Linq;
using ServiceBusExplorer.Models;

namespace ServiceBusExplorer.Services
{
    /// <summary>
    /// Matches the Dataverse service endpoints with the Azure entities (namespace host + path, case-insensitive,
    /// like Service Bus itself). What Dataverse references without Azure counterpart is added as a ghost, so its
    /// steps stay visible and editable even when Azure cannot be read.
    /// </summary>
    public static class MatchingService
    {
        public static ExplorerModel Build(IEnumerable<SbNamespace> azure, List<EndpointInfo> endpoints, List<StepInfo> steps)
        {
            var namespaces = azure.Where(n => !n.IsGhost).ToList();
            foreach (var ns in namespaces)
            {
                ns.Queues.RemoveAll(e => e.IsGhost);
                ns.Topics.RemoveAll(e => e.IsGhost);
                foreach (var entity in ns.Entities) entity.Endpoints.Clear();
            }

            foreach (var endpoint in endpoints) endpoint.Steps.Clear();
            var byEndpoint = endpoints.ToDictionary(e => e.Id);
            foreach (var step in steps)
            {
                if (!byEndpoint.TryGetValue(step.EndpointId, out var endpoint)) continue;
                endpoint.Steps.Add(step);
                step.EndpointName = endpoint.Name;
                step.TargetPath = endpoint.Path;
            }

            var hosts = namespaces.Where(n => n.Error == null).ToDictionary(n => n.Host, StringComparer.OrdinalIgnoreCase);
            var failed = namespaces.Where(n => n.Error != null).ToDictionary(n => n.Host, StringComparer.OrdinalIgnoreCase);
            var ghosts = new Dictionary<string, SbNamespace>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<MatchRow>();

            foreach (var endpoint in endpoints.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!endpoint.IsServiceBus || string.IsNullOrEmpty(endpoint.Host) || string.IsNullOrEmpty(endpoint.Path))
                {
                    rows.Add(new MatchRow { Status = MatchStatus.OtherType, Endpoint = endpoint, Host = endpoint.Host, Path = endpoint.Path });
                    continue;
                }

                SbEntity entity;
                MatchStatus status;
                if (hosts.TryGetValue(endpoint.Host, out var ns))
                {
                    entity = Find(ns.Entities, endpoint.Path);
                    if (entity == null)
                    {
                        entity = AddGhost(ns, endpoint);
                        status = MatchStatus.MissingOnAzure;
                    }
                    else if (entity.IsGhost) status = MatchStatus.MissingOnAzure;
                    else if (entity.Kind != endpoint.ExpectedKind) status = MatchStatus.TypeMismatch;
                    else status = endpoint.Steps.Count > 0 ? MatchStatus.Linked : MatchStatus.NoSteps;
                }
                else
                {
                    var holder = failed.TryGetValue(endpoint.Host, out var unreadable) ? unreadable : GetGhost(ghosts, endpoint.Host);
                    entity = Find(holder.Entities, endpoint.Path) ?? AddGhost(holder, endpoint);
                    status = MatchStatus.NotConnected;
                }

                entity.Endpoints.Add(endpoint);
                rows.Add(new MatchRow { Status = status, Endpoint = endpoint, Entity = entity, Host = endpoint.Host, Path = entity.Name });
            }

            foreach (var ns in hosts.Values)
            {
                foreach (var entity in ns.Entities.Where(e => e.Endpoints.Count == 0))
                {
                    rows.Add(new MatchRow { Status = MatchStatus.NoEndpoint, Entity = entity, Host = ns.Host, Path = entity.Name });
                }
            }

            foreach (var ns in namespaces.Concat(ghosts.Values))
            {
                ns.Queues = ns.Queues.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
                ns.Topics = ns.Topics.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }

            return new ExplorerModel
            {
                Namespaces = namespaces.Concat(ghosts.Values).OrderBy(n => n.Host, StringComparer.OrdinalIgnoreCase).ToList(),
                Endpoints = endpoints,
                Steps = steps,
                Rows = rows
            };
        }

        private static SbEntity Find(IEnumerable<SbEntity> entities, string path) =>
            entities.FirstOrDefault(e => string.Equals(e.Name, path, StringComparison.OrdinalIgnoreCase));

        private static SbNamespace GetGhost(Dictionary<string, SbNamespace> ghosts, string host)
        {
            if (!ghosts.TryGetValue(host, out var ghost))
            {
                ghost = new SbNamespace { Host = host, IsGhost = true };
                ghosts[host] = ghost;
            }
            return ghost;
        }

        private static SbEntity AddGhost(SbNamespace ns, EndpointInfo endpoint)
        {
            var entity = new SbEntity { Kind = endpoint.ExpectedKind, Name = endpoint.Path, Namespace = ns, IsGhost = true };
            (entity.Kind == SbKind.Topic ? ns.Topics : ns.Queues).Add(entity);
            return entity;
        }
    }
}
