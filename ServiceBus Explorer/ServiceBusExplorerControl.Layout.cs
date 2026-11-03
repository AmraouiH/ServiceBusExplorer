using System.Drawing;
using System.Windows.Forms;
using ServiceBusExplorer.UI;
using ServiceBusExplorer.UI.Controls;

namespace ServiceBusExplorer
{
    /// <summary>
    /// Construction of the tool UI (built in code: header, tabs and the three pages).
    /// Docking rule used everywhere: the Fill control is added first, then the edge controls from the innermost to the outermost.
    /// </summary>
    public partial class ServiceBusExplorerControl
    {
        #region Controls

        private ToolStrip tsHeader;
        private ToolStripButton tsbClose, tsbLoad, tsbConnections, tsbExport, tsbOptions, tsbAuthor, tsbDonate, tsbHelp;
        private ToolStripDropDownButton tsddLanguage;
        private ToolStripMenuItem tsmiEnglish, tsmiFrench;

        private SegmentedTabs tabs;
        private Label lblResultInfo;
        private Panel pageExplorer, pageMatching, pageSteps;

        // Explorer
        private CardPanel cardTree, cardDetail;
        private TextBox txtTreeFilter;
        private ModernButton btnAddNamespace;
        private CheckBox chkOnlyLinked;
        private ModernTreeView tree;
        private Label lblTreeEmpty;
        private Panel pnlDetail, pnlDetailEmpty;
        private Label lblDetailEmptyTitle, lblDetailEmpty;
        private Panel pnlAlert;
        private Label lblAlert;
        private ModernButton btnAlert;
        private FlowLayoutPanel flpTiles;
        private StatTile tile1, tile2, tile3, tile4, tile5;
        private SegmentedTabs tabsDetail;
        private Panel pnlDetailPages, pnlFilters;
        private StepsPanel stepsExplorer;
        private MessagesPanel messagesExplorer;
        private SplitContainer splitFilters;
        private DataGridView dgvSubscriptions, dgvRules, dgvProperties;
        private TextBox txtRule;
        private Label lblSubscriptions, lblRules, lblRuleDetail;
        private ModernButton btnCopyRule;

        // Matching
        private StatTile tileEndpoints, tileLinked, tileProblems, tileNotConnected, tileUnused;
        private CardPanel cardMatching;
        private ComboBox cboMatchFilter;
        private TextBox txtMatchSearch;
        private Label lblMatchCount;
        private ModernButton btnOpenMatch;
        private DataGridView dgvMatching;

        // Steps
        private StatTile tileAllSteps, tileEnabledSteps, tileImages;
        private CardPanel cardSteps;
        private StepsPanel stepsAll;

        private ToolTip toolTip;
        private LoadingOverlay overlay;

        #endregion

        private void BuildLayout()
        {
            SuspendLayout();
            Font = Theme.Base;
            BackColor = Theme.Canvas;
            Size = new Size(1400, 860);
            toolTip = new ToolTip { AutoPopDelay = 15000 };

            BuildHeader();

            // Below this size the body scrolls instead of crushing the cards.
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 12), BackColor = Theme.Canvas, AutoScroll = true, AutoScrollMinSize = new Size(1000, 560) };

            var pages = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas };
            pageExplorer = NewPage();
            pageMatching = NewPage();
            pageSteps = NewPage();
            pages.Controls.AddRange(new Control[] { pageExplorer, pageMatching, pageSteps });
            BuildExplorer();
            BuildMatching();
            BuildSteps();

            var tabsHost = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Canvas, Padding = new Padding(0, 2, 0, 2) };
            tabs = new SegmentedTabs { Dock = DockStyle.Fill };
            tabs.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Cloud, BadgeColor = Theme.Accent });
            tabs.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Compare, BadgeColor = Theme.Danger });
            tabs.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Play, BadgeColor = Theme.Accent });
            lblResultInfo = new Label { Dock = DockStyle.Right, Width = 620, TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Muted, Font = Theme.Small };
            tabsHost.Controls.Add(tabs);
            tabsHost.Controls.Add(lblResultInfo);

            body.Controls.Add(pages);
            body.Controls.Add(tabsHost);

            Controls.Add(body);
            Controls.Add(tsHeader);

            // Over everything (header included) while a task runs.
            overlay = new LoadingOverlay();
            Controls.Add(overlay);
            overlay.BringToFront();
            ResumeLayout(false);
        }

        private static Panel NewPage() => new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas, Visible = false };

        #region Header

        /// <summary>
        /// Command bar of Entity Fields Analyser: white Fluent surface, close glyph, accent primary button,
        /// actions with icons, and the Donate / Help / author links on the right.
        /// </summary>
        private void BuildHeader()
        {
            tsHeader = new ToolStrip
            {
                Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden,
                AutoSize = false,
                Height = 48,
                Padding = new Padding(10, 6, 10, 6),
                ImageScalingSize = new Size(16, 16),
                BackColor = Fluent.Surface
            };

            tsbClose = new ToolStripButton
            {
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                ImageScaling = ToolStripItemImageScaling.None,
                Margin = new Padding(0, 0, 2, 0),
                Padding = new Padding(6, 0, 6, 0),
                Overflow = ToolStripItemOverflow.Never
            };
            tsbLoad = HeaderButton(FluentToolStripRenderer.Primary);
            tsbLoad.Padding = new Padding(10, 0, 12, 0);
            tsbLoad.Margin = new Padding(2, 0, 4, 0);
            tsbConnections = HeaderButton(null);
            tsbExport = HeaderButton(null);
            tsbOptions = HeaderButton(null);
            tsddLanguage = new ToolStripDropDownButton
            {
                ImageScaling = ToolStripItemImageScaling.None,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Margin = new Padding(2, 0, 2, 0),
                Padding = new Padding(8, 0, 8, 0)
            };
            tsmiEnglish = new ToolStripMenuItem(Localization.Strings.EnglishName);
            tsmiFrench = new ToolStripMenuItem(Localization.Strings.FrenchName);
            tsddLanguage.DropDownItems.AddRange(new ToolStripItem[] { tsmiEnglish, tsmiFrench });

            // Right-aligned items are laid out from the right edge: author, then Help, then Donate.
            tsbAuthor = HeaderButton(FluentToolStripRenderer.Link);
            tsbAuthor.Alignment = ToolStripItemAlignment.Right;
            tsbAuthor.Margin = new Padding(2, 0, 0, 0);
            tsbHelp = HeaderButton(FluentToolStripRenderer.Link);
            tsbHelp.Alignment = ToolStripItemAlignment.Right;
            tsbDonate = HeaderButton(FluentToolStripRenderer.Link);
            tsbDonate.Alignment = ToolStripItemAlignment.Right;
            foreach (var link in new[] { tsbAuthor, tsbHelp, tsbDonate }) link.Padding = new Padding(6, 0, 6, 0);

            tsHeader.Items.AddRange(new ToolStripItem[]
            {
                tsbClose, new ToolStripSeparator { Margin = new Padding(8, 0, 8, 0) }, tsbLoad, tsbConnections, tsbExport,
                new ToolStripSeparator { Margin = new Padding(8, 0, 8, 0) }, tsbOptions, tsddLanguage,
                tsbAuthor, tsbHelp, tsbDonate
            });
        }

        private static ToolStripButton HeaderButton(string tag) => new ToolStripButton
        {
            ImageScaling = ToolStripItemImageScaling.None,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Margin = new Padding(2, 0, 2, 0),
            Padding = new Padding(8, 0, 8, 0),
            Tag = tag
        };

        #endregion

        #region Explorer

        private void BuildExplorer()
        {
            // Tree
            cardTree = new CardPanel { Dock = DockStyle.Left, Width = 420 };
            var filterRow = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Theme.Card };
            txtTreeFilter = new TextBox { Dock = DockStyle.Fill };
            var filterHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(0, 4, 8, 0) };
            filterHost.Controls.Add(txtTreeFilter);
            btnAddNamespace = new ModernButton { Variant = ButtonVariant.Secondary, Glyph = IconKind.Plus, Dock = DockStyle.Right, Width = 36 };
            filterRow.Controls.Add(filterHost);
            filterRow.Controls.Add(btnAddNamespace);
            chkOnlyLinked = new CheckBox { Dock = DockStyle.Top, Height = 28, ForeColor = Theme.Text, BackColor = Theme.Card };
            tree = new ModernTreeView { Dock = DockStyle.Fill };
            lblTreeEmpty = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, BackColor = Theme.Card, TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(16, 0, 16, 40) };
            cardTree.Controls.Add(tree);
            cardTree.Controls.Add(lblTreeEmpty);
            cardTree.Controls.Add(chkOnlyLinked);
            cardTree.Controls.Add(filterRow);

            // Detail
            cardDetail = new CardPanel { Dock = DockStyle.Fill };

            pnlDetailEmpty = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };
            lblDetailEmptyTitle = new Label { Dock = DockStyle.Top, Height = 150, Font = Theme.CardTitle, ForeColor = Theme.Text, TextAlign = ContentAlignment.BottomCenter };
            lblDetailEmpty = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.TopCenter, Padding = new Padding(120, 10, 120, 0) };
            pnlDetailEmpty.Controls.Add(lblDetailEmpty);
            pnlDetailEmpty.Controls.Add(lblDetailEmptyTitle);

            pnlDetail = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Visible = false };

            pnlAlert = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.WarningSoft, Padding = new Padding(10, 4, 6, 4), Visible = false };
            lblAlert = new Label { Dock = DockStyle.Fill, ForeColor = Theme.WarningText, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            btnAlert = new ModernButton { Variant = ButtonVariant.Secondary, Dock = DockStyle.Right, Width = 110 };
            pnlAlert.Controls.Add(lblAlert);
            pnlAlert.Controls.Add(btnAlert);

            flpTiles = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 70, WrapContents = false, BackColor = Theme.Card, Padding = new Padding(0, 4, 0, 0) };
            tile1 = DetailTile(Theme.Accent);
            tile2 = DetailTile(Theme.Danger);
            tile3 = DetailTile(Theme.TopicColor);
            tile4 = DetailTile(Theme.SubscriptionColor);
            tile5 = DetailTile(Theme.Success);
            flpTiles.Controls.AddRange(new Control[] { tile1, tile2, tile3, tile4, tile5 });

            tabsDetail = new SegmentedTabs { Dock = DockStyle.Top, Height = 44 };
            tabsDetail.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Play, BadgeColor = Theme.Accent });
            tabsDetail.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Filter, BadgeColor = Theme.TopicColor });
            tabsDetail.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.List });
            tabsDetail.Tabs.Add(new SegmentedTabs.Tab { Glyph = IconKind.Log, BadgeColor = Theme.Accent });

            pnlDetailPages = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(0, 6, 0, 0) };
            stepsExplorer = new StepsPanel(false) { Dock = DockStyle.Fill };
            messagesExplorer = new MessagesPanel { Dock = DockStyle.Fill, Visible = false };
            BuildFilters();
            dgvProperties = NewGrid();
            dgvProperties.ColumnHeadersVisible = false;
            dgvProperties.RowTemplate.Height = 26;
            dgvProperties.Columns.AddRange(TextColumn("colKey", 240), TextColumn("colValue", 0));
            dgvProperties.Columns["colKey"].DefaultCellStyle.ForeColor = Theme.Muted;
            dgvProperties.Columns["colValue"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dgvProperties.SelectionMode = DataGridViewSelectionMode.CellSelect;
            pnlDetailPages.Controls.AddRange(new Control[] { stepsExplorer, pnlFilters, dgvProperties, messagesExplorer });

            pnlDetail.Controls.Add(pnlDetailPages);
            pnlDetail.Controls.Add(tabsDetail);
            pnlDetail.Controls.Add(flpTiles);
            pnlDetail.Controls.Add(pnlAlert);

            cardDetail.Controls.Add(pnlDetail);
            cardDetail.Controls.Add(pnlDetailEmpty);

            pageExplorer.Controls.Add(cardDetail);
            pageExplorer.Controls.Add(new Splitter { Dock = DockStyle.Left, Width = 10, BackColor = Theme.Canvas, MinExtra = 500, MinSize = 280 });
            pageExplorer.Controls.Add(cardTree);
        }

        private void BuildFilters()
        {
            pnlFilters = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };

            splitFilters = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Theme.Card, SplitterWidth = 8 };
            splitFilters.Panel1.BackColor = Theme.Card;
            splitFilters.Panel2.BackColor = Theme.Card;

            lblSubscriptions = SectionLabel();
            dgvSubscriptions = NewGrid();
            dgvSubscriptions.Columns.AddRange(
                TextColumn("colSubName", 0),
                TextColumn("colSubActive", 80, DataGridViewContentAlignment.MiddleRight),
                TextColumn("colSubDead", 90, DataGridViewContentAlignment.MiddleRight),
                TextColumn("colSubFilters", 60, DataGridViewContentAlignment.MiddleRight),
                TextColumn("colSubForward", 180),
                TextColumn("colSubStatus", 90));
            dgvSubscriptions.Columns["colSubName"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dgvSubscriptions.Columns["colSubName"].MinimumWidth = 160;
            dgvSubscriptions.Columns["colSubName"].DefaultCellStyle.Font = Theme.Semibold;
            dgvSubscriptions.Columns["colSubDead"].DefaultCellStyle.ForeColor = Theme.Danger;
            splitFilters.Panel1.Controls.Add(dgvSubscriptions);
            splitFilters.Panel1.Controls.Add(lblSubscriptions);

            lblRules = SectionLabel();
            dgvRules = NewGrid();
            dgvRules.Columns.AddRange(TextColumn("colRuleName", 170), TextColumn("colRuleType", 110), TextColumn("colRuleExpression", 0), TextColumn("colRuleAction", 180));
            dgvRules.Columns["colRuleExpression"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dgvRules.Columns["colRuleExpression"].DefaultCellStyle.Font = Theme.Mono;
            dgvRules.Columns["colRuleName"].DefaultCellStyle.Font = Theme.Semibold;

            var detail = new Panel { Dock = DockStyle.Bottom, Height = 104, BackColor = Theme.Card };
            var detailHead = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Theme.Card };
            lblRuleDetail = SectionLabel();
            lblRuleDetail.Dock = DockStyle.Fill;
            btnCopyRule = new ModernButton { Variant = ButtonVariant.Ghost, Glyph = IconKind.Copy, Dock = DockStyle.Right, Width = 90, Height = 26 };
            detailHead.Controls.Add(lblRuleDetail);
            detailHead.Controls.Add(btnCopyRule);
            txtRule = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.SameBack,
                ForeColor = Theme.Text,
                Font = Theme.Mono
            };
            detail.Controls.Add(txtRule);
            detail.Controls.Add(detailHead);

            splitFilters.Panel2.Controls.Add(dgvRules);
            splitFilters.Panel2.Controls.Add(lblRules);
            splitFilters.Panel2.Controls.Add(detail);

            pnlFilters.Controls.Add(splitFilters);
        }

        private static Label SectionLabel() => new Label { Dock = DockStyle.Top, Height = 24, ForeColor = Theme.Muted, Font = Theme.Semibold, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };

        private static StatTile DetailTile(Color tint) => new StatTile { Tint = tint, Size = new Size(158, 60), Margin = new Padding(0, 0, 10, 0) };

        #endregion

        #region Matching

        private void BuildMatching()
        {
            var tiles = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 74, WrapContents = false, BackColor = Theme.Canvas };
            tileEndpoints = PageTile(Theme.Accent);
            tileLinked = PageTile(Theme.Success);
            tileProblems = PageTile(Theme.Danger);
            tileNotConnected = PageTile(Theme.Diff);
            tileUnused = PageTile(Theme.Subtle);
            tiles.Controls.AddRange(new Control[] { tileEndpoints, tileLinked, tileProblems, tileNotConnected, tileUnused });

            cardMatching = new CardPanel { Dock = DockStyle.Fill };
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Theme.Card, Padding = new Padding(0, 0, 0, 6) };
            cboMatchFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(0, 4, 10, 0) };
            txtMatchSearch = new TextBox { Width = 280, Margin = new Padding(0, 5, 10, 0) };
            btnOpenMatch = new ModernButton { Variant = ButtonVariant.Secondary, Glyph = IconKind.Target, AutoSize = true, MinimumSize = new Size(120, 30), Height = 30, Margin = new Padding(0, 0, 10, 0) };
            lblMatchCount = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 8, 0, 0) };
            toolbar.Controls.AddRange(new Control[] { cboMatchFilter, txtMatchSearch, btnOpenMatch, lblMatchCount });

            dgvMatching = NewGrid();
            dgvMatching.Columns.AddRange(
                TextColumn("colMatch", 140),
                TextColumn("colMatchEndpoint", 0),
                TextColumn("colMatchType", 120),
                TextColumn("colMatchPath", 220),
                TextColumn("colMatchNamespace", 270),
                TextColumn("colMatchSteps", 70, DataGridViewContentAlignment.MiddleRight),
                TextColumn("colMatchImages", 64, DataGridViewContentAlignment.MiddleRight),
                TextColumn("colMatchMessages", 90, DataGridViewContentAlignment.MiddleRight));
            dgvMatching.Columns["colMatchEndpoint"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            dgvMatching.Columns["colMatchEndpoint"].MinimumWidth = 180;
            dgvMatching.Columns["colMatchEndpoint"].DefaultCellStyle.Font = Theme.Semibold;

            cardMatching.Controls.Add(dgvMatching);
            cardMatching.Controls.Add(toolbar);

            pageMatching.Controls.Add(cardMatching);
            pageMatching.Controls.Add(tiles);
        }

        private static StatTile PageTile(Color tint) => new StatTile { Tint = tint, Size = new Size(176, 62), Margin = new Padding(0, 2, 10, 2) };

        #endregion

        #region Steps

        private void BuildSteps()
        {
            var tiles = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 74, WrapContents = false, BackColor = Theme.Canvas };
            tileAllSteps = PageTile(Theme.Accent);
            tileEnabledSteps = PageTile(Theme.Success);
            tileImages = PageTile(Theme.TopicColor);
            tiles.Controls.AddRange(new Control[] { tileAllSteps, tileEnabledSteps, tileImages });

            cardSteps = new CardPanel { Dock = DockStyle.Fill };
            stepsAll = new StepsPanel(true) { Dock = DockStyle.Fill };
            cardSteps.Controls.Add(stepsAll);

            pageSteps.Controls.Add(cardSteps);
            pageSteps.Controls.Add(tiles);
        }

        #endregion

        #region Helpers

        private static DataGridView NewGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
            };
            Theme.StyleGrid(grid, false);
            return grid;
        }

        private static DataGridViewTextBoxColumn TextColumn(string name, int width, DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
        {
            var column = new DataGridViewTextBoxColumn { Name = name, ReadOnly = true };
            if (width > 0) column.Width = width;
            column.DefaultCellStyle.Alignment = alignment;
            if (alignment != DataGridViewContentAlignment.MiddleLeft) column.HeaderCell.Style.Alignment = alignment;
            return column;
        }

        #endregion
    }
}
