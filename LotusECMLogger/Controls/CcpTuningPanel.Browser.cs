using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using LotusECMLogger.Controls.Plotting;
using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Tuning;
using LotusECMLogger.Services.Tuning.Plotting;

namespace LotusECMLogger.Controls
{
    /// <summary>The Tuning page's calibration tree.</summary>
    internal sealed partial class CcpTuningPanel
    {
        // ── Browser ──────────────────────────────────────────────────────────────────────────────────────

        private void LoadTree()
        {
            treeModel = a2l is null ? null : CalibrationTree.Build(a2l);
            ShowItem(null);
            RebuildTree();
        }

        /// <summary>Rebuilds the visible tree from the model, keeping only items that pass the filters.</summary>
        private void RebuildTree()
        {
            var terms = searchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string kind = kindBox.SelectedItem as string ?? "All";
            bool Matches(A2lCalibrationItem item) =>
                (kind == "All" || kind == KindGroup(item.Kind)) &&
                (!changedBox.Checked || image!.IsChanged(item)) &&
                terms.All(t => item.Name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    item.LongIdentifier.Contains(t, StringComparison.OrdinalIgnoreCase));

            tree.BeginUpdate();
            try
            {
                tree.Nodes.Clear();
                itemNodes.Clear();
                if (treeModel is null) return;
                bool filtering = terms.Length > 0 || changedBox.Checked;
                foreach (var child in treeModel.Children)
                    if (Build(child, Matches) is { } node) tree.Nodes.Add(node);
                if (filtering) tree.ExpandAll();
                if (current is not null && itemNodes.TryGetValue(current, out var selected)) tree.SelectedNode = selected;
            }
            finally { tree.EndUpdate(); }
            MarkChangedItems();
        }

        private TreeNode? Build(CalibrationTreeNode model, Func<A2lCalibrationItem, bool> matches)
        {
            if (model.Item is { } item)
            {
                if (!matches(item)) return null;
                int icon = item.Kind switch
                {
                    A2lItemKind.Map => MapIcon, A2lItemKind.Curve => CurveIcon, A2lItemKind.AxisPts => AxisIcon,
                    A2lItemKind.Ascii => TextIcon, _ => ValueIcon,
                };
                var leaf = new TreeNode(model.Title, icon, icon)
                {
                    Tag = item,
                    ToolTipText = Wrap($"{item.Name}  ({Shape(item)})\n{item.LongIdentifier}"),
                    ForeColor = item.IsEditable ? SystemColors.WindowText : SystemColors.GrayText,
                };
                itemNodes[item] = leaf;
                return leaf;
            }
            var folder = new TreeNode(model.Title, FolderIcon, FolderIcon) { ToolTipText = model.Description };
            foreach (var child in model.Children)
                if (Build(child, matches) is { } node) folder.Nodes.Add(node);
            return folder.Nodes.Count > 0 ? folder : null;
        }

        private static string KindGroup(A2lItemKind kind) => kind switch
        {
            A2lItemKind.Map or A2lItemKind.Curve => "Tables",
            A2lItemKind.AxisPts => "Axes",
            _ => "Values",
        };

        private static string Shape(A2lCalibrationItem item) => item.Kind switch
        {
            A2lItemKind.Map => $"{item.CountX} × {item.CountY} map",
            A2lItemKind.Curve => $"{item.CountX}-point curve",
            A2lItemKind.AxisPts => $"{item.CountX}-point axis",
            A2lItemKind.ValBlk => $"{item.ElementCount} values",
            A2lItemKind.Ascii => "text",
            _ => "value",
        };

        /// <summary>Tooltips don't wrap on their own; break long lines at about 90 characters.</summary>
        private static string Wrap(string text)
        {
            if (text.Length > 1500) text = text[..1500] + "…";
            var lines = new List<string>();
            foreach (var paragraph in text.Split('\n'))
            {
                var line = new System.Text.StringBuilder();
                foreach (var word in paragraph.Split(' '))
                {
                    if (line.Length > 0 && line.Length + word.Length > 90)
                    {
                        lines.Add(line.ToString());
                        line.Clear();
                    }
                    if (line.Length > 0) line.Append(' ');
                    line.Append(word);
                }
                lines.Add(line.ToString());
            }
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>Bold for items that differ from the file.</summary>
        private void MarkChangedItems()
        {
            if (image is null) return;
            foreach (var (item, node) in itemNodes)
            {
                bool changed = image.IsChanged(item);
                var font = changed ? new Font(tree.Font, FontStyle.Bold) : null;
                if ((node.NodeFont is not null) != changed) node.NodeFont = font;
            }
        }

        /// <summary>Selects an item by name, as if picked in the tree.</summary>
        internal bool SelectItem(string name)
        {
            if (a2l is null || !a2l.Items.TryGetValue(name, out var item)) return false;
            if (!itemNodes.ContainsKey(item))
            {
                searchBox.Text = "";
                changedBox.Checked = false;
                kindBox.SelectedItem = "All";
            }
            if (!itemNodes.TryGetValue(item, out var node)) return false;
            tree.SelectedNode = node;
            node.EnsureVisible();
            return true;
        }
    }
}
