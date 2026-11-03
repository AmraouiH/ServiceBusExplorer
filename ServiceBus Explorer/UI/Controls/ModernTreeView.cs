using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ServiceBusExplorer.UI.Controls
{
    /// <summary>
    /// Colored pill shown on the right of a tree row
    /// </summary>
    public class NodePill
    {
        public NodePill(string text, Color fore, Color back)
        {
            Text = text;
            Fore = fore;
            Back = back;
        }

        public string Text { get; }
        public Color Fore { get; }
        public Color Back { get; }
    }

    public class ModernTreeNode
    {
        private readonly List<ModernTreeNode> nodes = new List<ModernTreeNode>();

        public ModernTreeNode(string title)
        {
            Title = title;
        }

        /// <summary>First line, the record name</summary>
        public string Title { get; set; }

        /// <summary>Second line, smaller and muted</summary>
        public string Caption { get; set; }

        /// <summary>Icon drawn in the round avatar</summary>
        public IconKind Glyph { get; set; }

        public Color AccentColor { get; set; } = Theme.Accent;

        public List<NodePill> Pills { get; } = new List<NodePill>();

        public string ToolTip { get; set; }

        public bool Bold { get; set; }

        public bool Muted { get; set; }

        public bool CheckEnabled { get; set; } = true;

        public bool Checked { get; internal set; }

        public bool Expanded { get; set; } = true;

        public object Tag { get; set; }

        public ModernTreeNode Parent { get; private set; }

        public IReadOnlyList<ModernTreeNode> Nodes => nodes;

        public int Level => Parent == null ? 0 : Parent.Level + 1;

        internal bool IsLastSibling(IList<ModernTreeNode> roots)
        {
            var siblings = Parent == null ? roots : Parent.nodes;
            return siblings.Count > 0 && siblings[siblings.Count - 1] == this;
        }

        public ModernTreeNode Add(ModernTreeNode child)
        {
            child.Parent = this;
            nodes.Add(child);
            return child;
        }
    }

    public class ModernTreeNodeEventArgs : EventArgs
    {
        public ModernTreeNodeEventArgs(ModernTreeNode node, bool byUser)
        {
            Node = node;
            ByUser = byUser;
        }

        public ModernTreeNode Node { get; }

        /// <summary>False when the change comes from code (cascade)</summary>
        public bool ByUser { get; }
    }

    /// <summary>
    /// Owner-drawn tree: rows with a check box, a colored avatar, two lines of text and pills,
    /// linked to their parent by rounded connector lines. Drawn with GDI+ only.
    /// </summary>
    [DesignerCategory("Code")]
    public class ModernTreeView : ScrollableControl
    {
        private const int RowHeight = 44;
        private const int RowGap = 4;
        private const int TopPadding = 6;
        private const int Indent = 28;
        private const int ChevronSize = 16;
        private const int CheckSize = 18;
        private const int AvatarSize = 28;

        private static readonly Color LineColor = Color.FromArgb(208, 214, 225);
        private static readonly Color HoverBack = Color.FromArgb(247, 248, 252);
        private static readonly Color CheckBorder = Color.FromArgb(190, 198, 211);

        private readonly List<ModernTreeNode> roots = new List<ModernTreeNode>();
        private readonly List<ModernTreeNode> visible = new List<ModernTreeNode>();
        private readonly ToolTip toolTip = new ToolTip { InitialDelay = 600, ReshowDelay = 200 };

        private ModernTreeNode selected;
        private ModernTreeNode hover;
        private HitPart hoverPart;

        private enum HitPart
        {
            None,
            Row,
            Chevron,
            Check
        }

        public ModernTreeView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            AutoScroll = true;
            BackColor = Theme.Card;
            Font = Theme.Base;
            TabStop = true;
        }

        /// <summary>Checkboxes of the rows (hidden by default in this tool).</summary>
        public bool ShowChecks { get; set; }

        public event EventHandler<ModernTreeNodeEventArgs> AfterCheck;

        public event EventHandler SelectedNodeChanged;

        public IReadOnlyList<ModernTreeNode> Nodes => roots;

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ModernTreeNode SelectedNode
        {
            get => selected;
            set
            {
                if (selected == value)
                {
                    return;
                }
                selected = value;

                // A node hidden in a collapsed branch becomes visible when selected
                for (var parent = value?.Parent; parent != null; parent = parent.Parent)
                {
                    if (!parent.Expanded)
                    {
                        parent.Expanded = true;
                        Rebuild();
                    }
                }

                EnsureVisible(value);
                Invalidate();
                SelectedNodeChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void SetNodes(IEnumerable<ModernTreeNode> nodes)
        {
            roots.Clear();
            roots.AddRange(nodes);
            selected = null;
            hover = null;
            Rebuild();
        }

        public IEnumerable<ModernTreeNode> AllNodes()
        {
            var stack = new Stack<ModernTreeNode>(roots.AsEnumerable().Reverse());
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                yield return node;
                for (var i = node.Nodes.Count - 1; i >= 0; i--)
                {
                    stack.Push(node.Nodes[i]);
                }
            }
        }

        public void SetChecked(ModernTreeNode node, bool value)
        {
            SetChecked(node, value, false);
        }

        private void SetChecked(ModernTreeNode node, bool value, bool byUser)
        {
            if (node.Checked == value)
            {
                return;
            }
            node.Checked = value;
            InvalidateNode(node);
            AfterCheck?.Invoke(this, new ModernTreeNodeEventArgs(node, byUser));
        }

        public void ExpandAll()
        {
            SetExpanded(true);
        }

        public void CollapseAll()
        {
            SetExpanded(false);
        }

        private void SetExpanded(bool expanded)
        {
            foreach (var node in AllNodes().Where(n => n.Nodes.Count > 0))
            {
                node.Expanded = expanded;
            }

            // The selection cannot stay inside a collapsed branch
            if (!expanded && selected != null)
            {
                var root = selected;
                while (root.Parent != null)
                {
                    root = root.Parent;
                }
                selected = root;
                SelectedNodeChanged?.Invoke(this, EventArgs.Empty);
            }
            Rebuild();
        }

        private void Toggle(ModernTreeNode node)
        {
            if (node.Nodes.Count == 0)
            {
                return;
            }
            node.Expanded = !node.Expanded;
            if (!node.Expanded && selected != null && IsDescendant(selected, node))
            {
                SelectedNode = node;
            }
            Rebuild();
        }

        private static bool IsDescendant(ModernTreeNode node, ModernTreeNode ancestor)
        {
            for (var parent = node.Parent; parent != null; parent = parent.Parent)
            {
                if (parent == ancestor)
                {
                    return true;
                }
            }
            return false;
        }

        private void Rebuild()
        {
            visible.Clear();
            foreach (var root in roots)
            {
                AddVisible(root);
            }
            AutoScrollMinSize = new Size(0, TopPadding * 2 + visible.Count * RowHeight);
            Invalidate();
        }

        private void AddVisible(ModernTreeNode node)
        {
            visible.Add(node);
            if (!node.Expanded)
            {
                return;
            }
            foreach (var child in node.Nodes)
            {
                AddVisible(child);
            }
        }

        #region Geometry

        private int RowTop(int index) => TopPadding + index * RowHeight + AutoScrollPosition.Y;

        private static int NodeLeft(ModernTreeNode node) => 10 + node.Level * Indent;

        private Rectangle RowBounds(int index) => new Rectangle(4, RowTop(index), ClientSize.Width - 8, RowHeight - RowGap);

        private Rectangle ChevronBounds(ModernTreeNode node, int index)
        {
            var row = RowBounds(index);
            return new Rectangle(NodeLeft(node), row.Top + (row.Height - ChevronSize) / 2, ChevronSize, ChevronSize);
        }

        private Rectangle CheckBounds(ModernTreeNode node, int index)
        {
            var row = RowBounds(index);
            return new Rectangle(NodeLeft(node) + ChevronSize + 6, row.Top + (row.Height - CheckSize) / 2, CheckSize, CheckSize);
        }

        private Rectangle AvatarBounds(ModernTreeNode node, int index)
        {
            var row = RowBounds(index);
            var left = ShowChecks ? CheckBounds(node, index).Right + 10 : NodeLeft(node) + ChevronSize + 6;
            return new Rectangle(left, row.Top + (row.Height - AvatarSize) / 2, AvatarSize, AvatarSize);
        }

        /// <summary>Column of the vertical line joining a node to its children</summary>
        private static int LineX(ModernTreeNode node) => NodeLeft(node) + ChevronSize / 2;

        private void EnsureVisible(ModernTreeNode node)
        {
            var index = visible.IndexOf(node);
            if (index < 0)
            {
                return;
            }

            var top = TopPadding + index * RowHeight;
            var bottom = top + RowHeight;
            var scroll = -AutoScrollPosition.Y;
            if (top < scroll)
            {
                AutoScrollPosition = new Point(0, top - TopPadding);
            }
            else if (bottom > scroll + ClientSize.Height)
            {
                AutoScrollPosition = new Point(0, bottom - ClientSize.Height + TopPadding);
            }
        }

        private void InvalidateNode(ModernTreeNode node)
        {
            var index = visible.IndexOf(node);
            if (index >= 0)
            {
                Invalidate(RowBounds(index));
            }
        }

        private int IndexAt(Point location)
        {
            var index = (location.Y - AutoScrollPosition.Y - TopPadding) / RowHeight;
            if (location.Y - AutoScrollPosition.Y - TopPadding < 0 || index < 0 || index >= visible.Count)
            {
                return -1;
            }
            return index;
        }

        private HitPart HitTest(Point location, out ModernTreeNode node)
        {
            node = null;
            var index = IndexAt(location);
            if (index < 0)
            {
                return HitPart.None;
            }

            node = visible[index];
            var chevron = ChevronBounds(node, index);
            chevron.Inflate(4, 4);
            if (node.Nodes.Count > 0 && chevron.Contains(location))
            {
                return HitPart.Chevron;
            }

            var check = CheckBounds(node, index);
            check.Inflate(4, 4);
            if (ShowChecks && check.Contains(location))
            {
                return HitPart.Check;
            }
            return HitPart.Row;
        }

        #endregion

        #region Painting

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (visible.Count == 0)
            {
                return;
            }

            var first = Math.Max(0, IndexAt(new Point(0, 0)));
            var last = IndexAt(new Point(0, ClientSize.Height - 1));
            if (last < 0)
            {
                last = visible.Count - 1;
            }

            for (var i = first; i <= last; i++)
            {
                PaintRow(g, visible[i], i);
            }
        }

        private void PaintRow(Graphics g, ModernTreeNode node, int index)
        {
            var row = RowBounds(index);
            var isSelected = node == selected;
            var isHover = node == hover;

            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Row background
            if (isSelected || isHover)
            {
                using (var path = Theme.RoundedRect(new RectangleF(row.X, row.Y, row.Width - 1, row.Height - 1), 8))
                using (var fill = new SolidBrush(isSelected ? Theme.AccentSoft : HoverBack))
                {
                    g.FillPath(fill, path);
                    if (isSelected)
                    {
                        using (var pen = new Pen(Theme.Blend(Theme.AccentSoft, Theme.Accent, 0.25f)))
                        {
                            g.DrawPath(pen, path);
                        }
                    }
                }
                if (isSelected && Focused)
                {
                    using (var bar = Theme.RoundedRect(new RectangleF(row.X + 1, row.Y + 9, 3, row.Height - 18), 1.5f))
                    using (var brush = new SolidBrush(Theme.Accent))
                    {
                        g.FillPath(brush, bar);
                    }
                }
            }

            PaintConnectors(g, node, index);

            // Expand / collapse chevron
            if (node.Nodes.Count > 0)
            {
                var chevron = ChevronBounds(node, index);
                if (isHover && hoverPart == HitPart.Chevron)
                {
                    var circle = chevron;
                    circle.Inflate(4, 4);
                    using (var brush = new SolidBrush(Color.FromArgb(232, 235, 242)))
                    {
                        g.FillEllipse(brush, circle);
                    }
                }
                LineIcons.Draw(g, node.Expanded ? IconKind.ChevronDown : IconKind.ChevronRight, chevron, Theme.Muted);
            }

            if (ShowChecks) PaintCheck(g, node, CheckBounds(node, index), isHover && hoverPart == HitPart.Check);

            // Avatar with the initials of the table
            var avatar = AvatarBounds(node, index);
            var accent = node.Muted ? Theme.Subtle : node.AccentColor;
            using (var fill = new SolidBrush(Theme.Blend(Color.White, accent, 0.16f)))
            using (var ring = new Pen(Theme.Blend(Color.White, accent, 0.35f)))
            {
                g.FillEllipse(fill, avatar);
                g.DrawEllipse(ring, avatar);
            }
            LineIcons.Draw(g, node.Glyph, new RectangleF(avatar.X + (avatar.Width - 16) / 2f, avatar.Y + (avatar.Height - 16) / 2f, 16, 16), accent);

            // Pills, from the right
            var right = row.Right - 10;
            for (var i = node.Pills.Count - 1; i >= 0; i--)
            {
                var pill = node.Pills[i];
                var size = TextRenderer.MeasureText(g, pill.Text, Theme.SmallBold, Size.Empty, TextFormatFlags.NoPadding);
                var bounds = new Rectangle(right - size.Width - 16, row.Top + (row.Height - 22) / 2, size.Width + 16, 22);
                if (bounds.Left < avatar.Right + 60)
                {
                    break;
                }
                using (var path = Theme.RoundedRect(bounds, 11))
                using (var brush = new SolidBrush(pill.Back))
                {
                    g.FillPath(brush, path);
                }
                TextRenderer.DrawText(g, pill.Text, Theme.SmallBold, bounds, pill.Fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                right = bounds.Left - 6;
            }

            // Title and caption
            var textLeft = avatar.Right + 10;
            var textWidth = Math.Max(10, right - textLeft - 4);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            var hasCaption = !string.IsNullOrEmpty(node.Caption);
            var titleTop = hasCaption ? row.Top + 4 : row.Top + (row.Height - 16) / 2;
            TextRenderer.DrawText(g, node.Title, node.Bold ? Theme.Semibold : Theme.Base, new Rectangle(textLeft, titleTop, textWidth, 18),
                node.Muted ? Theme.Subtle : Theme.Text, flags);
            if (hasCaption)
            {
                TextRenderer.DrawText(g, node.Caption, Theme.Small, new Rectangle(textLeft, row.Top + 22, textWidth, 16),
                    node.Muted ? Theme.Subtle : Theme.Muted, flags);
            }
        }

        private void PaintConnectors(Graphics g, ModernTreeNode node, int index)
        {
            var slotTop = RowTop(index);
            var slotBottom = slotTop + RowHeight;
            var center = RowBounds(index).Top + (RowHeight - RowGap) / 2;

            using (var pen = new Pen(LineColor, 1.4f))
            {
                // Lines of the ancestors that still have siblings below
                for (var ancestor = node.Parent; ancestor?.Parent != null; ancestor = ancestor.Parent)
                {
                    if (!ancestor.IsLastSibling(roots))
                    {
                        var x = LineX(ancestor.Parent);
                        g.DrawLine(pen, x, slotTop, x, slotBottom);
                    }
                }

                // Rounded elbow from the parent line to this node
                if (node.Parent != null)
                {
                    var x = LineX(node.Parent);
                    var end = NodeLeft(node) + (node.Nodes.Count > 0 ? -3 : ChevronSize + 3);
                    const int radius = 8;
                    using (var path = new GraphicsPath())
                    {
                        path.AddLine(x, slotTop, x, center - radius);
                        path.AddArc(x, center - 2 * radius, 2 * radius, 2 * radius, 180, -90);
                        path.AddLine(x + radius, center, end, center);
                        g.DrawPath(pen, path);
                    }
                    if (!node.IsLastSibling(roots))
                    {
                        g.DrawLine(pen, x, center, x, slotBottom);
                    }
                }

                // Start of the line to the children
                if (node.Expanded && node.Nodes.Count > 0)
                {
                    var x = LineX(node);
                    g.DrawLine(pen, x, center + ChevronSize / 2 + 3, x, slotBottom);
                }
            }
        }

        private static void PaintCheck(Graphics g, ModernTreeNode node, Rectangle bounds, bool hot)
        {
            using (var path = Theme.RoundedRect(new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1, bounds.Height - 1), 5))
            {
                if (!node.CheckEnabled)
                {
                    using (var fill = new SolidBrush(Theme.Divider))
                    using (var pen = new Pen(Theme.Border))
                    {
                        g.FillPath(fill, path);
                        g.DrawPath(pen, path);
                    }
                    LineIcons.Draw(g, IconKind.Minus, bounds, Theme.Subtle);
                    return;
                }

                if (node.Checked)
                {
                    using (var fill = new SolidBrush(hot ? Theme.AccentHover : Theme.Accent))
                    {
                        g.FillPath(fill, path);
                    }
                    LineIcons.Draw(g, IconKind.Check, bounds, Color.White);
                    return;
                }

                using (var fill = new SolidBrush(Color.White))
                using (var pen = new Pen(hot ? Theme.Accent : CheckBorder, 1.4f))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(pen, path);
                }
            }
        }

        #endregion

        #region Mouse and keyboard

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            ModernTreeNode node;
            var part = HitTest(e.Location, out node);
            if (node == hover && part == hoverPart)
            {
                return;
            }

            if (node != hover)
            {
                toolTip.SetToolTip(this, node == null ? null : BuildToolTip(node));
            }
            hover = node;
            hoverPart = part;
            Cursor = part == HitPart.Check && node != null && node.CheckEnabled || part == HitPart.Chevron ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        private static string BuildToolTip(ModernTreeNode node)
        {
            var lines = new[] { node.Title, node.Caption, node.ToolTip }.Where(l => !string.IsNullOrEmpty(l)).Distinct();
            return string.Join(Environment.NewLine, lines);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = null;
            hoverPart = HitPart.None;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            ModernTreeNode node;
            switch (HitTest(e.Location, out node))
            {
                case HitPart.Chevron:
                    Toggle(node);
                    break;

                case HitPart.Check:
                    SelectedNode = node;
                    if (node.CheckEnabled)
                    {
                        SetChecked(node, !node.Checked, true);
                    }
                    break;

                case HitPart.Row:
                    SelectedNode = node;
                    if (e.Clicks == 2)
                    {
                        Toggle(node);
                    }
                    break;
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                case Keys.Space:
                case Keys.Home:
                case Keys.End:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (visible.Count == 0)
            {
                return;
            }

            var index = selected == null ? -1 : visible.IndexOf(selected);
            switch (e.KeyCode)
            {
                case Keys.Down:
                    SelectedNode = visible[Math.Min(visible.Count - 1, index + 1)];
                    break;

                case Keys.Up:
                    SelectedNode = visible[Math.Max(0, index - 1)];
                    break;

                case Keys.Home:
                    SelectedNode = visible[0];
                    break;

                case Keys.End:
                    SelectedNode = visible[visible.Count - 1];
                    break;

                case Keys.Right:
                    if (selected != null && selected.Nodes.Count > 0)
                    {
                        if (!selected.Expanded)
                        {
                            Toggle(selected);
                        }
                        else
                        {
                            SelectedNode = selected.Nodes[0];
                        }
                    }
                    break;

                case Keys.Left:
                    if (selected != null)
                    {
                        if (selected.Expanded && selected.Nodes.Count > 0)
                        {
                            Toggle(selected);
                        }
                        else if (selected.Parent != null)
                        {
                            SelectedNode = selected.Parent;
                        }
                    }
                    break;

                case Keys.Space:
                    if (ShowChecks && selected != null && selected.CheckEnabled)
                    {
                        SetChecked(selected, !selected.Checked, true);
                    }
                    break;

                default:
                    return;
            }
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                toolTip.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
