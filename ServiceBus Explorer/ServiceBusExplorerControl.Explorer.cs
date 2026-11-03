using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ServiceBusExplorer.Forms;
using ServiceBusExplorer.Helpers;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.Models;
using ServiceBusExplorer.Services;
using ServiceBusExplorer.Services.Azure;
using ServiceBusExplorer.UI;
using ServiceBusExplorer.UI.Controls;

namespace ServiceBusExplorer
{
    /// <summary>
    /// Explorer page: the tree of the namespaces and the detail of the selected item.
    /// </summary>
    public partial class ServiceBusExplorerControl
    {
        private const int DetailSteps = 0;
        private const int DetailFilters = 1;
        private const int DetailProperties = 2;
        private const int DetailMessages = 3;

        private SbNamespace _alertNamespace;

        /// <summary>Entity whose messages the explorer shows (key of its node), to keep them while it stays selected.</summary>
        private string _messagesKey;
        private SbEntity _messagesEntity;

        #region Tree

        private void RebuildTree()
        {
            var previous = tree.SelectedNode == null ? null : NodeKey(tree.SelectedNode.Tag);
            var expanded = new Dictionary<string, bool>();
            foreach (var node in tree.AllNodes().Where(n => n.Nodes.Count > 0)) expanded[NodeKey(node.Tag)] = node.Expanded;
            var nodes = new List<ModernTreeNode>();

            if (_model != null)
            {
                var filter = txtTreeFilter.Text.Trim();
                foreach (var ns in _model.Namespaces)
                {
                    var node = NamespaceNode(ns, filter);
                    if (node != null) nodes.Add(node);
                }
            }

            tree.SetNodes(nodes);
            foreach (var node in tree.AllNodes())
            {
                if (expanded.TryGetValue(NodeKey(node.Tag), out var state)) node.Expanded = state;
            }
            tree.SetNodes(nodes);

            var showTree = nodes.Count > 0;
            tree.Visible = showTree;
            lblTreeEmpty.Visible = !showTree;
            lblTreeEmpty.Text = _model == null ? Strings.TreeEmpty : Strings.TreeNoMatch;

            var selected = previous == null ? null : tree.AllNodes().FirstOrDefault(n => NodeKey(n.Tag) == previous);
            if (selected != null) tree.SelectedNode = selected;
            ShowSelection();
        }

        private static string NodeKey(object tag)
        {
            switch (tag)
            {
                case SbNamespace ns: return "n:" + ns.Host;
                case SbEntity entity: return $"e:{entity.Namespace?.Host}/{entity.Topic?.Name}/{entity.Kind}/{entity.Name}";
                case SbRule rule: return $"r:{NodeKey(rule.Subscription)}/{rule.Name}";
                case string group: return group;
                default: return string.Empty;
            }
        }

        private ModernTreeNode NamespaceNode(SbNamespace ns, string filter)
        {
            var node = new ModernTreeNode(ns.Name)
            {
                Caption = ns.IsGhost || ns.Error != null ? ns.Host : Strings.NamespaceCaption(ns.Queues.Count(q => !q.IsGhost), ns.Topics.Count(t => !t.IsGhost)),
                Glyph = IconKind.Cloud,
                AccentColor = Theme.Accent,
                Muted = ns.IsGhost,
                Bold = true,
                Tag = ns,
                ToolTip = ns.Host
            };
            if (ns.Error != null) node.Pills.Add(Pill(Strings.PillError, Theme.Danger));
            else if (ns.IsGhost) node.Pills.Add(Pill(Strings.PillNotConnected, Theme.Diff));
            else AddStepPill(node, ns.Entities.SelectMany(e => e.Steps).Count());

            AddGroup(node, Strings.GroupQueues, "g:q:" + ns.Host, ns.Queues, IconKind.Queue, Theme.QueueColor, filter);
            AddGroup(node, Strings.GroupTopics, "g:t:" + ns.Host, ns.Topics, IconKind.Topic, Theme.TopicColor, filter);

            // Hidden by the filters, unless the namespace itself is what the user looks for.
            var keep = node.Nodes.Count > 0 || (filter.Length == 0 && !chkOnlyLinked.Checked) || Contains(ns.Host, filter);
            return keep ? node : null;
        }

        private void AddGroup(ModernTreeNode parent, string title, string key, List<SbEntity> entities, IconKind glyph, Color color, string filter)
        {
            var group = new ModernTreeNode(title) { Glyph = glyph, AccentColor = color, Tag = key };
            foreach (var entity in entities)
            {
                var node = EntityNode(entity, glyph, color, filter);
                if (node != null) group.Add(node);
            }
            if (group.Nodes.Count == 0) return;
            group.Caption = group.Nodes.Count.ToString();
            parent.Add(group);
        }

        private ModernTreeNode EntityNode(SbEntity entity, IconKind glyph, Color color, string filter)
        {
            var isTopic = entity.Kind == SbKind.Topic;
            var node = new ModernTreeNode(entity.Name)
            {
                Caption = entity.IsGhost ? Strings.KindLabel(entity.Kind) : isTopic ? Strings.TopicCaption(entity.Subscriptions.Count) : Strings.QueueCaption(entity.ActiveMessages, entity.DeadLetterMessages),
                Glyph = glyph,
                AccentColor = color,
                Muted = entity.IsGhost,
                Tag = entity,
                ToolTip = entity.Name
            };
            AddStatusPills(node, entity);
            AddStepPill(node, entity.Steps.Count());

            if (isTopic)
            {
                foreach (var subscription in entity.Subscriptions.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var child = SubscriptionNode(subscription, filter);
                    if (child != null) node.Add(child);
                }
            }

            var linked = !chkOnlyLinked.Checked || entity.Steps.Any();
            var matches = Contains(entity.Name, filter) || node.Nodes.Count > 0;
            return linked && matches ? node : null;
        }

        private ModernTreeNode SubscriptionNode(SbEntity subscription, string filter)
        {
            var node = new ModernTreeNode(subscription.Name)
            {
                Caption = Strings.SubscriptionCaption(subscription.Rules.Count, subscription.ActiveMessages, subscription.DeadLetterMessages),
                Glyph = IconKind.Subscription,
                AccentColor = Theme.SubscriptionColor,
                Tag = subscription,
                ToolTip = subscription.Name,
                Expanded = false
            };
            AddStatusPills(node, subscription);

            foreach (var rule in subscription.Rules)
            {
                node.Add(new ModernTreeNode(rule.Name)
                {
                    Caption = rule.Expression,
                    Glyph = IconKind.Filter,
                    AccentColor = Theme.RuleColor,
                    Tag = rule,
                    ToolTip = rule.Expression
                });
            }
            return filter.Length == 0 || Contains(subscription.Name, filter) || subscription.Topic != null && Contains(subscription.Topic.Name, filter) ? node : null;
        }

        private static void AddStatusPills(ModernTreeNode node, SbEntity entity)
        {
            if (entity.IsGhost && entity.Namespace?.IsGhost == false && entity.Namespace.Error == null) node.Pills.Add(Pill(Strings.PillMissing, Theme.Danger));
            if (!entity.IsActive) node.Pills.Add(Pill(Strings.PillDisabled, Theme.Subtle));
            if (entity.DeadLetterMessages > 0) node.Pills.Add(Pill(Strings.PillDeadLetter, Theme.Danger));
        }

        private static void AddStepPill(ModernTreeNode node, int count)
        {
            if (count > 0) node.Pills.Add(Pill(Strings.PillSteps(count), Theme.Accent));
        }

        private static NodePill Pill(string text, Color color) => new NodePill(text, color == Theme.Subtle ? Theme.Muted : color, Theme.Soft(color));

        private static bool Contains(string value, string filter) => filter.Length == 0 || (value ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

        #endregion

        #region Detail

        private void ShowSelection()
        {
            var tag = tree.SelectedNode?.Tag;
            if (tag is string) tag = tree.SelectedNode.Parent?.Tag;
            var rule = tag as SbRule;
            if (rule != null) tag = rule.Subscription;

            switch (tag)
            {
                case SbNamespace ns:
                    ShowNamespace(ns);
                    break;
                case SbEntity entity:
                    ShowEntity(entity, rule);
                    break;
                default:
                    cardDetail.Title = null;
                    cardDetail.Subtitle = null;
                    pnlDetail.Visible = false;
                    pnlDetailEmpty.Visible = true;
                    return;
            }

            pnlDetailEmpty.Visible = false;
            pnlDetail.Visible = true;
        }

        private void ShowNamespace(SbNamespace ns)
        {
            cardDetail.Title = ns.Name;
            cardDetail.Subtitle = ns.Host;

            SetAlert(ns.Error != null ? Strings.AlertNamespaceError(ns.Error.Message) : ns.IsGhost ? Strings.AlertGhostNamespace(ns.Host) : null,
                ns.Error != null ? Strings.ShowError : ns.IsGhost ? Strings.ConnectNamespace : null, ns);

            var entities = ns.Entities.ToList();
            SetTile(tile1, Strings.TileQueues, CountOf(ns, ns.Queues.Count(q => !q.IsGhost)), Theme.QueueColor);
            SetTile(tile2, Strings.TileTopics, CountOf(ns, ns.Topics.Count(t => !t.IsGhost)), Theme.TopicColor);
            SetTile(tile3, Strings.TileSubscriptions, CountOf(ns, ns.AllSubscriptions.Count()), Theme.SubscriptionColor);
            var unread = ns.IsGhost || ns.Error != null;
            SetTile(tile4, Strings.TileDeadLetter, unread ? "–" : entities.Concat(ns.AllSubscriptions).Sum(e => e.DeadLetterMessages).ToString("N0"), Theme.Danger);
            var steps = entities.SelectMany(e => e.Steps).ToList();
            SetTile(tile5, Strings.TileSteps, steps.Count.ToString(), Theme.Success);

            stepsExplorer.ShowMessages = false;
            stepsExplorer.SetSteps(steps);
            SetDetailTabs(steps.Count, null, false, null);
            FillProperties(NamespaceProperties(ns));
        }

        private void ShowEntity(SbEntity entity, SbRule rule)
        {
            cardDetail.Title = entity.Name;
            cardDetail.Subtitle = Strings.KindLabel(entity.Kind) + (entity.Topic != null ? " · " + entity.Topic.Name : string.Empty) + " · " + entity.Namespace?.Host;

            var ns = entity.Namespace;
            if (entity.IsGhost && ns?.Error != null) SetAlert(Strings.AlertNamespaceError(ns.Error.Message), Strings.ShowError, ns);
            else if (entity.IsGhost && ns?.IsGhost == true) SetAlert(Strings.AlertGhostNamespace(ns.Host), Strings.ConnectNamespace, ns);
            else if (entity.IsGhost) SetAlert(Strings.AlertGhostEntity, null, null);
            else if (!entity.IsActive) SetAlert(Strings.AlertEntityDisabled(entity.Status), null, null);
            else SetAlert(null, null, null);

            var steps = entity.Steps.ToList();
            var isTopic = entity.Kind == SbKind.Topic;

            // A ghost was not read on Azure: its counters are unknown, not zero.
            string Count(long value) => entity.IsGhost ? "–" : value.ToString("N0");
            SetTile(tile1, Strings.TileActive, Count(entity.ActiveMessages), Theme.Accent);
            SetTile(tile2, Strings.TileDeadLetter, Count(entity.DeadLetterMessages), entity.DeadLetterMessages > 0 ? Theme.Danger : Theme.Subtle);
            SetTile(tile3, Strings.TileScheduled, Count(entity.ScheduledMessages), Theme.TopicColor);
            if (isTopic) SetTile(tile4, Strings.TileSubscriptions, Count(entity.Subscriptions.Count), Theme.SubscriptionColor);
            else SetTile(tile4, Strings.TileSize, entity.IsGhost ? "–" : Format.Size(entity.SizeBytes), Theme.SubscriptionColor);
            SetTile(tile5, Strings.TileSteps, steps.Count.ToString(), Theme.Success);

            stepsExplorer.ShowMessages = true;
            stepsExplorer.SetSteps(steps, entity.Kind == SbKind.Subscription ? Strings.SubscriptionStepsHint(entity.Topic.Name) : null);

            var filters = !entity.IsGhost && (isTopic || entity.Kind == SbKind.Subscription);
            SetDetailTabs(steps.Count, filters ? entity : null, rule != null, entity);
            FillProperties(EntityProperties(entity));

            if (filters) FillFilters(entity, rule);
        }

        /// <summary>A namespace that was not read has unknown counters, not zeros.</summary>
        private static string CountOf(SbNamespace ns, int count) => ns.IsGhost || ns.Error != null ? "–" : count.ToString();

        private void SetTile(StatTile tile, string caption, string value, Color tint)
        {
            tile.Caption = caption;
            tile.Value = value;
            tile.Tint = tint;
        }

        private void SetAlert(string text, string action, SbNamespace ns)
        {
            _alertNamespace = ns;
            pnlAlert.Visible = !string.IsNullOrEmpty(text);
            lblAlert.Text = text;
            btnAlert.Visible = !string.IsNullOrEmpty(action);
            btnAlert.Text = action;
        }

        private void RunAlertAction()
        {
            var ns = _alertNamespace;
            if (ns == null) return;
            if (ns.Error != null) MessageDialog.ShowError(this, ns.Error);
            else ShowConnections(ns.Host);
        }

        /// <param name="filtersOwner">Topic or subscription that has a filters page, null for the others.</param>
        /// <param name="messagesOwner">Queue, topic or subscription whose messages can be peeked, null for a namespace.</param>
        private void SetDetailTabs(int stepCount, SbEntity filtersOwner, bool openFilters, SbEntity messagesOwner)
        {
            tabsDetail.Tabs[DetailSteps].Text = Strings.DetailSteps;
            tabsDetail.Tabs[DetailSteps].Badge = stepCount.ToString();
            tabsDetail.Tabs[DetailProperties].Text = Strings.DetailProperties;
            SetMessagesTab(messagesOwner);

            var filters = tabsDetail.Tabs[DetailFilters];
            filters.Text = filtersOwner == null ? Strings.DetailFilters : filtersOwner.Kind == SbKind.Topic ? Strings.DetailFilters : Strings.DetailFiltersOnly;
            filters.Badge = filtersOwner == null ? null : (filtersOwner.Kind == SbKind.Topic ? filtersOwner.Subscriptions.Count : filtersOwner.Rules.Count).ToString();
            tabsDetail.Tabs[DetailFilters].Hidden = filtersOwner == null;
            _filtersAvailable = filtersOwner != null;

            if (openFilters) tabsDetail.SelectedIndex = DetailFilters;
            else if (tabsDetail.SelectedIndex == DetailFilters && !_filtersAvailable) tabsDetail.SelectedIndex = DetailSteps;
            else if (tabsDetail.SelectedIndex == DetailMessages && messagesOwner == null) tabsDetail.SelectedIndex = DetailSteps;
            tabsDetail.Invalidate();
            ShowDetailPage();
        }

        private bool _filtersAvailable;

        private void ShowDetailPage()
        {
            var page = tabsDetail.SelectedIndex;
            if (page == DetailFilters && !_filtersAvailable) page = DetailSteps;
            if (page == DetailMessages && _messagesEntity == null) page = DetailSteps;
            stepsExplorer.Visible = page == DetailSteps;
            pnlFilters.Visible = page == DetailFilters;
            dgvProperties.Visible = page == DetailProperties;
            messagesExplorer.Visible = page == DetailMessages;
        }

        #endregion

        #region Messages

        /// <summary>The messages read stay while the same entity is selected (a reload of Dataverse rebuilds the tree).</summary>
        private void SetMessagesTab(SbEntity entity)
        {
            var tab = tabsDetail.Tabs[DetailMessages];
            tab.Text = Strings.DetailMessages;
            tab.Hidden = entity == null;

            // Counters of Azure: the active messages waiting, a topic being the sum of its subscriptions.
            var counted = entity == null || entity.IsGhost ? new List<SbEntity>() : entity.Kind == SbKind.Topic ? entity.Subscriptions : new List<SbEntity> { entity };
            tab.Badge = counted.Count == 0 ? null : counted.Sum(e => e.ActiveMessages).ToString("N0");

            var key = entity == null ? null : NodeKey(entity);
            var previous = _messagesEntity;
            _messagesEntity = entity;
            if (key == _messagesKey && entity != null)
            {
                // Same entity after a rebuild: the steps of the messages follow the new model.
                if (messagesExplorer.Batches != null && previous != entity) RelinkMessages(entity);
                return;
            }

            _messagesKey = key;
            if (entity == null) return;
            var sources = MessageService.SourcesOf(entity);
            messagesExplorer.SetContext(sources.Count == 0 ? null : Strings.MessagesIntro(entity.Name, sources.Count), sources.Count > 0);
        }

        private void RelinkMessages(SbEntity entity)
        {
            var sources = MessageService.SourcesOf(entity).ToDictionary(NodeKey);
            foreach (var batch in messagesExplorer.Batches)
            {
                if (sources.TryGetValue(NodeKey(batch.Source), out var source)) batch.Source = source;
                foreach (var message in batch.Messages) message.Source = batch.Source;
            }
            MessageService.ResolveSteps(messagesExplorer.Batches, _model?.Steps);
            messagesExplorer.SetBatches(messagesExplorer.Batches);
        }

        private void ReadExplorerMessages()
        {
            var entity = _messagesEntity;
            var sources = MessageService.SourcesOf(entity);
            if (sources.Count == 0) return;
            var read = MessageReading();
            var key = _messagesKey;

            RunWithOverlay(Strings.ReadingMessagesTitle, (worker, token) =>
                read(sources, m => worker.ReportProgress(0, m), token), value =>
            {
                if (_messagesKey != key) return;
                messagesExplorer.SetBatches((List<MessageBatch>)value);
            });
        }

        /// <summary>
        /// Reads the active messages (never the dead-letter queue) with the saved connections and the current limit,
        /// then links the messages to the loaded steps.
        /// </summary>
        private Func<List<SbEntity>, Action<string>, CancellationToken, List<MessageBatch>> MessageReading()
        {
            var connections = _settings.Connections.ToList();
            var settings = _settings;
            var max = Math.Max(1, Math.Min(PluginSettings.MaxMessagesLimit, _settings.MaxMessages));
            var steps = _model?.Steps.ToList() ?? new List<StepInfo>();

            return (sources, progress, token) =>
            {
                var batches = MessageService.Read(sources, true, false, max, ns =>
                {
                    var connection = connections.FirstOrDefault(c => string.Equals(c.Host, ns.Host, StringComparison.OrdinalIgnoreCase));
                    if (connection == null) throw new AzureRequestException(0, Strings.AlertGhostNamespace(ns.Host));
                    return ConnectionFactory.CreateMessageReader(connection, settings);
                }, progress, token);
                MessageService.ResolveSteps(batches, steps);
                return batches;
            };
        }

        /// <summary>Messages sent by the selected steps, read from the queue or the subscriptions of their endpoints.</summary>
        private void ShowStepMessages(List<StepInfo> steps)
        {
            if (steps.Count == 0) return;
            var targets = steps.Select(s => MessageService.TargetOf(s, _model)).Where(t => t != null).Distinct().ToList();
            var sources = targets.SelectMany(MessageService.SourcesOf).Distinct().ToList();
            var title = Strings.StepMessagesTitle(steps.Count);
            if (sources.Count == 0)
            {
                MessageDialog.Show(this, MessageKind.Info, title, Strings.StepMessagesUnavailable(targets.Count == 0 ? steps[0].TargetPath : targets[0].Name));
                return;
            }

            var subtitle = steps.Count == 1 ? $"{steps[0].Name} · {string.Join(", ", targets.Select(t => t.Name))}" : Strings.StepMessagesSubtitle(steps.Count, targets.Count);
            var read = MessageReading();
            using (var dialog = new MessagesDialog(title, subtitle, Strings.MessagesIntro(string.Join(", ", targets.Select(t => t.Name)), sources.Count),
                       new HashSet<Guid>(steps.Select(s => s.Id)), (progress, token) => read(sources, progress, token)))
            {
                dialog.ShowModal(this);
            }
        }

        #endregion

        #region Filters

        private void FillFilters(SbEntity entity, SbRule selectedRule)
        {
            var isTopic = entity.Kind == SbKind.Topic;
            splitFilters.Panel1Collapsed = !isTopic;

            _suppressEvents = true;
            dgvSubscriptions.Rows.Clear();
            foreach (var subscription in entity.Subscriptions.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                var index = dgvSubscriptions.Rows.Add(subscription.Name, subscription.ActiveMessages.ToString("N0"), subscription.DeadLetterMessages.ToString("N0"),
                    subscription.Rules.Count, subscription.ForwardTo, subscription.Status);
                dgvSubscriptions.Rows[index].Tag = subscription;
            }
            _suppressEvents = false;

            if (isTopic)
            {
                ShowSubscriptionRules();
                return;
            }
            FillRules(entity, selectedRule);
        }

        private void ShowSubscriptionRules()
        {
            if (_suppressEvents || dgvSubscriptions.SelectedRows.Count == 0)
            {
                if (!_suppressEvents) FillRules(null, null);
                return;
            }
            FillRules(dgvSubscriptions.SelectedRows[0].Tag as SbEntity, null);
        }

        private void FillRules(SbEntity subscription, SbRule selected)
        {
            lblRules.Text = subscription == null ? Strings.DetailFiltersOnly : $"{Strings.DetailFiltersOnly} · {subscription.Name}";
            dgvRules.Rows.Clear();
            if (subscription != null)
            {
                foreach (var rule in subscription.Rules)
                {
                    var index = dgvRules.Rows.Add(rule.Name, Strings.FilterKindLabel(rule.FilterKind), rule.Expression, rule.ActionExpression);
                    dgvRules.Rows[index].Tag = rule;
                    if (rule == selected) dgvRules.Rows[index].Selected = true;
                }
            }
            ShowRuleDetail();
        }

        private void ShowRuleDetail()
        {
            var rule = dgvRules.SelectedRows.Count > 0 ? dgvRules.SelectedRows[0].Tag as SbRule : null;
            btnCopyRule.Enabled = rule != null;
            if (rule == null)
            {
                txtRule.Text = Strings.NoFilterSelected;
                return;
            }
            txtRule.Text = rule.Expression + (string.IsNullOrEmpty(rule.ActionExpression) ? string.Empty : Environment.NewLine + Environment.NewLine + Strings.RuleAction + ": " + rule.ActionExpression);
        }

        private void CopyRule()
        {
            try
            {
                Clipboard.SetText(txtRule.Text);
            }
            catch (Exception)
            {
                // The clipboard is locked by another application: nothing to copy this time.
            }
        }

        #endregion

        #region Properties

        private void FillProperties(IEnumerable<KeyValuePair<string, string>> properties)
        {
            dgvProperties.Rows.Clear();
            foreach (var property in properties.Where(p => !string.IsNullOrEmpty(p.Value))) dgvProperties.Rows.Add(property.Key, property.Value);
        }

        private static KeyValuePair<string, string> Prop(string key, string value) => new KeyValuePair<string, string>(key, value);

        private static IEnumerable<KeyValuePair<string, string>> NamespaceProperties(SbNamespace ns)
        {
            yield return Prop(Strings.PropHost, ns.Host);
            yield return Prop(Strings.PropLocation, ns.Location);
            yield return Prop(Strings.PropTier, ns.Sku);
            yield return Prop(Strings.PropStatus, ns.Status);
            yield return Prop(Strings.PropAzureSubscription, ns.SubscriptionName);
            if (!ns.IsGhost) yield return Prop(Strings.PropAccess, ns.ReadWithKey ? Strings.PropAccessKey : Strings.PropAccessMicrosoft);
        }

        private static IEnumerable<KeyValuePair<string, string>> EntityProperties(SbEntity e)
        {
            yield return Prop(Strings.PropType, Strings.KindLabel(e.Kind));
            yield return Prop(Strings.PropPath, e.Kind == SbKind.Subscription ? $"{e.Topic.Name}/Subscriptions/{e.Name}" : e.Name);
            yield return Prop(Strings.PropEndpoints, string.Join(", ", e.Target.Endpoints.Select(x => x.Name)));
            if (e.IsGhost) yield break;

            yield return Prop(Strings.PropStatus, e.Status);
            yield return Prop(Strings.PropTotal, e.TotalMessages.ToString("N0"));
            yield return Prop(Strings.PropTransfer, e.TransferMessages.ToString("N0"));
            yield return Prop(Strings.TileSize, Format.Size(e.SizeBytes));
            if (e.MaxSizeMegabytes > 0) yield return Prop(Strings.PropMaxSize, Format.Size(e.MaxSizeMegabytes * 1048576));
            yield return Prop(Strings.PropLock, Format.Duration(e.LockDuration));
            yield return Prop(Strings.PropTtl, Format.Duration(e.DefaultTimeToLive));
            yield return Prop(Strings.PropAutoDelete, Format.Duration(e.AutoDeleteOnIdle));
            if (e.MaxDeliveryCount > 0) yield return Prop(Strings.PropMaxDelivery, e.MaxDeliveryCount.ToString());
            if (e.Kind != SbKind.Topic)
            {
                yield return Prop(Strings.PropSessions, Format.YesNo(e.RequiresSession));
                yield return Prop(Strings.PropDeadLetterExpiry, Format.YesNo(e.DeadLetterOnExpiration));
            }
            if (e.Kind != SbKind.Subscription)
            {
                yield return Prop(Strings.PropDuplicate, Format.YesNo(e.RequiresDuplicateDetection));
                yield return Prop(Strings.PropPartitioned, Format.YesNo(e.Partitioned));
            }
            yield return Prop(Strings.PropForwardTo, e.ForwardTo);
            yield return Prop(Strings.PropForwardDead, e.ForwardDeadLetteredTo);
            yield return Prop(Strings.PropCreated, Format.Date(e.CreatedOn));
            yield return Prop(Strings.PropUpdated, Format.Date(e.UpdatedOn));
            yield return Prop(Strings.PropAccessed, Format.Date(e.LastAccessedOn));
        }

        #endregion
    }
}
