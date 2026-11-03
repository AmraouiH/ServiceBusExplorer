using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.Models;
using ServiceBusExplorer.UI;

namespace ServiceBusExplorer
{
    /// <summary>
    /// Matching and Steps pages.
    /// </summary>
    public partial class ServiceBusExplorerControl
    {
        private const int FilterAll = 0;
        private const int FilterProblems = 1;

        /// <summary>Statuses of the filter list, after « all » and « to check ».</summary>
        private static readonly MatchStatus[] FilterStatuses =
        {
            MatchStatus.Linked, MatchStatus.NoSteps, MatchStatus.MissingOnAzure, MatchStatus.TypeMismatch,
            MatchStatus.NotConnected, MatchStatus.NoEndpoint, MatchStatus.OtherType
        };

        private static bool IsProblem(MatchStatus status) =>
            status == MatchStatus.MissingOnAzure || status == MatchStatus.TypeMismatch || status == MatchStatus.NoSteps;

        private int ProblemCount() => _model.Rows.Count(r => IsProblem(r.Status));

        private void FillMatchFilter()
        {
            var index = cboMatchFilter.SelectedIndex;
            cboMatchFilter.Items.Clear();
            cboMatchFilter.Items.Add(Strings.MatchAll);
            cboMatchFilter.Items.Add(Strings.MatchProblems);
            foreach (var status in FilterStatuses) cboMatchFilter.Items.Add(Strings.StatusLabel(status));
            cboMatchFilter.SelectedIndex = index >= 0 ? index : FilterAll;
        }

        private bool MatchesFilter(MatchRow row)
        {
            var index = cboMatchFilter.SelectedIndex;
            if (index == FilterProblems && !IsProblem(row.Status)) return false;
            if (index > FilterProblems && row.Status != FilterStatuses[index - 2]) return false;
            // Webhooks and relays are noise here: they only show when asked for.
            if (index == FilterAll && row.Status == MatchStatus.OtherType) return false;

            var search = txtMatchSearch.Text.Trim();
            return search.Length == 0 || Contains(row.Endpoint?.Name, search) || Contains(row.Path, search) || Contains(row.Host, search);
        }

        private void FillMatching()
        {
            var total = _model?.Rows.Count(r => r.Status != MatchStatus.OtherType) ?? 0;
            var rows = _model?.Rows.Where(MatchesFilter).ToList();

            dgvMatching.Rows.Clear();
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    var messages = row.Entity == null || row.Entity.IsGhost ? string.Empty : row.Entity.ActiveMessages.ToString("N0");
                    var index = dgvMatching.Rows.Add(
                        Strings.StatusLabel(row.Status),
                        row.Endpoint?.Name ?? "—",
                        row.Endpoint != null ? Strings.ContractLabel(row.Endpoint.Contract) : Strings.KindLabel(row.Entity.Kind),
                        row.Path,
                        row.Host,
                        row.Endpoint != null ? $"{row.EnabledStepCount} / {row.StepCount}" : string.Empty,
                        row.Endpoint != null ? row.ImageCount.ToString() : string.Empty,
                        messages);
                    dgvMatching.Rows[index].Tag = row;
                }
            }

            lblMatchCount.Text = _model == null ? string.Empty : Strings.MatchCount(rows.Count, total);
            tileEndpoints.Value = _model == null ? "–" : _model.Endpoints.Count(e => e.IsServiceBus).ToString();
            tileLinked.Value = _model == null ? "–" : _model.Rows.Count(r => r.Status == MatchStatus.Linked).ToString();
            tileProblems.Value = _model == null ? "–" : ProblemCount().ToString();
            tileNotConnected.Value = _model == null ? "–" : _model.Rows.Count(r => r.Status == MatchStatus.NotConnected).ToString();
            tileUnused.Value = _model == null ? "–" : _model.Rows.Count(r => r.Status == MatchStatus.NoEndpoint).ToString();
            UpdateMatchButton();
        }

        private void UpdateMatchButton()
        {
            var row = dgvMatching.SelectedRows.Count > 0 ? dgvMatching.SelectedRows[0].Tag as MatchRow : null;
            btnOpenMatch.Enabled = row?.Entity != null;
        }

        private void OpenMatch()
        {
            var row = dgvMatching.SelectedRows.Count > 0 ? dgvMatching.SelectedRows[0].Tag as MatchRow : null;
            if (row?.Entity == null) return;

            // The target may be hidden by the filters of the tree.
            _suppressEvents = true;
            txtTreeFilter.Text = string.Empty;
            chkOnlyLinked.Checked = false;
            _suppressEvents = false;
            RebuildTree();

            var key = NodeKey(row.Entity);
            var node = tree.AllNodes().FirstOrDefault(n => NodeKey(n.Tag) == key);
            if (node == null) return;
            tabs.SelectedIndex = PageExplorer;
            tree.SelectedNode = node;
        }

        private void PaintMatchStatus(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
            if (!(dgvMatching.Rows[e.RowIndex].Tag is MatchRow row)) return;

            var color = StatusColor(row.Status);
            Theme.PaintPillCell(e, dgvMatching, e.Value as string, color == Theme.Subtle ? Theme.Muted : color == Theme.Success ? Theme.SuccessText : color == Theme.Diff ? Theme.WarningText : color, Theme.Soft(color));
        }

        private static Color StatusColor(MatchStatus status)
        {
            switch (status)
            {
                case MatchStatus.Linked: return Theme.Success;
                case MatchStatus.MissingOnAzure:
                case MatchStatus.TypeMismatch: return Theme.Danger;
                case MatchStatus.NoSteps:
                case MatchStatus.NotConnected: return Theme.Diff;
                default: return Theme.Subtle;
            }
        }

        private void FillStepsPage()
        {
            stepsAll.SetSteps(_model?.Steps ?? Enumerable.Empty<StepInfo>());
            tileAllSteps.Value = _model == null ? "–" : _model.Steps.Count.ToString();
            tileEnabledSteps.Value = _model == null ? "–" : _model.Steps.Count(s => s.Enabled).ToString();
            tileImages.Value = _model == null ? "–" : _model.Steps.Sum(s => s.Images.Count).ToString();
        }
    }
}
