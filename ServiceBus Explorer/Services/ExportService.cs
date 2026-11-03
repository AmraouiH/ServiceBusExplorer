using System.Linq;
using ServiceBusExplorer.Helpers;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.Models;

namespace ServiceBusExplorer.Services
{
    /// <summary>
    /// Excel report: the matching, the steps and images, the Azure entities and their filters.
    /// </summary>
    public static class ExportService
    {
        public static void Export(string path, ExplorerModel model)
        {
            var writer = new ExcelWriter();

            writer.AddSheet(Strings.SheetMatching,
                new[] { Strings.ColMatch, Strings.ColEndpoint, Strings.ColContract, Strings.ColPath, Strings.ColNamespace, Strings.ColSteps, Strings.ColEnabledSteps, Strings.ColImages, Strings.ColActive, Strings.ColDeadLetter },
                model.Rows.Select(r => new object[]
                {
                    Strings.StatusLabel(r.Status), r.Endpoint?.Name, r.Endpoint != null ? Strings.ContractLabel(r.Endpoint.Contract) : Strings.KindLabel(r.Entity.Kind),
                    r.Path, r.Host, r.StepCount, r.EnabledStepCount, r.ImageCount, r.Entity?.ActiveMessages ?? 0, r.Entity?.DeadLetterMessages ?? 0
                }),
                new double[] { 18, 36, 18, 36, 42, 8, 10, 8, 10, 11 });

            writer.AddSheet(Strings.SheetSteps,
                new[] { Strings.ColStep, Strings.ColState, Strings.ColMessage, Strings.ColTable, Strings.ColStage, Strings.ColMode, Strings.ColRank, Strings.ColFiltering, Strings.ColImages, Strings.ColTarget, Strings.ColEndpoint },
                model.Steps.Select(s => new object[]
                {
                    s.Name, s.Enabled ? Strings.Enabled : Strings.Disabled, s.Message, s.Table ?? Strings.AllTables, Strings.StageLabel(s.Stage), Strings.ModeLabel(s.Mode),
                    s.Rank, s.FilteringAttributes, s.Images.Count, s.TargetPath, s.EndpointName
                }),
                new double[] { 50, 11, 12, 22, 15, 14, 7, 44, 8, 30, 32 });

            writer.AddSheet(Strings.SheetImages,
                new[] { Strings.ColStep, Strings.ColType, Strings.ColAlias, Strings.ColName, Strings.ColColumns },
                model.Steps.SelectMany(s => s.Images.Select(i => new object[]
                {
                    s.Name, Strings.ImageTypeLabel(i.ImageType), i.Alias, i.Name, i.AllColumns ? Strings.AllColumns : i.Attributes
                })),
                new double[] { 50, 11, 20, 24, 70 });

            var entities = model.Namespaces.SelectMany(n => n.Entities).Where(e => !e.IsGhost).ToList();
            writer.AddSheet(Strings.SheetEntities,
                new[] { Strings.ColNamespace, Strings.ColType, Strings.ColName, Strings.ColStatus, Strings.ColActive, Strings.ColDeadLetter, Strings.ColScheduled, Strings.ColSize, Strings.ColForwardTo },
                entities.Select(e => new object[]
                {
                    e.Namespace.Host, Strings.KindLabel(e.Kind), e.Name, e.Status, e.ActiveMessages, e.DeadLetterMessages, e.ScheduledMessages, e.SizeBytes, e.ForwardTo
                }),
                new double[] { 42, 10, 36, 10, 9, 11, 10, 13, 30 });

            var subscriptions = model.Namespaces.SelectMany(n => n.AllSubscriptions).ToList();
            writer.AddSheet(Strings.SheetSubscriptions,
                new[] { Strings.ColNamespace, Strings.ColTopic, Strings.ColSubscription, Strings.ColStatus, Strings.ColActive, Strings.ColDeadLetter, Strings.ColFilters, Strings.ColForwardTo },
                subscriptions.Select(s => new object[]
                {
                    s.Namespace.Host, s.Topic.Name, s.Name, s.Status, s.ActiveMessages, s.DeadLetterMessages, s.Rules.Count, s.ForwardTo
                }),
                new double[] { 42, 30, 34, 10, 9, 11, 8, 30 });

            writer.AddSheet(Strings.SheetRules,
                new[] { Strings.ColNamespace, Strings.ColTopic, Strings.ColSubscription, Strings.ColRule, Strings.ColType, Strings.ColExpression, Strings.ColAction },
                subscriptions.SelectMany(s => s.Rules.Select(r => new object[]
                {
                    s.Namespace.Host, s.Topic.Name, s.Name, r.Name, Strings.FilterKindLabel(r.FilterKind), r.Expression, r.ActionExpression
                })),
                new double[] { 42, 30, 34, 24, 14, 70, 40 });

            writer.Save(path);
        }
    }
}
