using LotusECMLogger.Services.A2l;

namespace LotusECMLogger.Services.Tuning
{
    /// <summary>A folder of the calibration browser, or (with <see cref="Item"/>) one calibration item.</summary>
    public sealed class CalibrationTreeNode
    {
        public required string Title { get; init; }
        public string Description { get; init; } = "";
        public A2lCalibrationItem? Item { get; init; }
        public List<CalibrationTreeNode> Children { get; } = [];

        public bool IsFolder => Item is null;

        /// <summary>Every item at or below this node.</summary>
        public IEnumerable<A2lCalibrationItem> Items() =>
            Item is { } item ? [item] : Children.SelectMany(c => c.Items());
    }

    /// <summary>
    /// Builds the browser tree from the A2L's ROOT GROUPs and their SUB_GROUPs. Items no group lists go under
    /// "Ungrouped". An A2L without groups is sorted by <see cref="CalibrationGroups"/> name rules instead.
    /// </summary>
    public static class CalibrationTree
    {
        public static CalibrationTreeNode Build(A2lResolvedModule a2l)
        {
            ArgumentNullException.ThrowIfNull(a2l);
            var root = new CalibrationTreeNode { Title = a2l.Module.Name };
            var groups = a2l.Module.Groups.GroupBy(g => g.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var listed = new HashSet<string>(StringComparer.Ordinal);
            var roots = a2l.Module.Groups.Where(g => g.Root).ToList();
            if (roots.Count > 0)
            {
                foreach (var group in roots)
                    if (FromGroup(group, groups, a2l, listed, []) is { } node) root.Children.Add(node);
            }
            else
            {
                foreach (var subsystem in a2l.Items.Values.GroupBy(i => CalibrationGroups.Classify(i.Name))
                    .OrderBy(g => IndexOf(g.Key)))
                {
                    var folder = new CalibrationTreeNode { Title = subsystem.Key.Title };
                    var prefixes = subsystem.GroupBy(i => CalibrationGroups.Prefix(i.Name)).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
                    foreach (var prefix in prefixes)
                    {
                        var parent = folder;
                        if (prefixes.Count > 1) folder.Children.Add(parent = new CalibrationTreeNode { Title = prefix.Key });
                        parent.Children.AddRange(prefix.Select(Leaf).OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase));
                    }
                    root.Children.Add(folder);
                    listed.UnionWith(subsystem.Select(i => i.Name));
                }
            }
            var ungrouped = a2l.Items.Values.Where(i => !listed.Contains(i.Name)).Select(Leaf)
                .OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase).ToList();
            if (ungrouped.Count > 0)
            {
                var folder = new CalibrationTreeNode { Title = "Ungrouped" };
                folder.Children.AddRange(ungrouped);
                root.Children.Add(folder);
            }
            return root;
        }

        private static int IndexOf(CalibrationGroups.Subsystem subsystem)
        {
            for (int i = 0; i < CalibrationGroups.Subsystems.Count; i++)
                if (ReferenceEquals(CalibrationGroups.Subsystems[i], subsystem)) return i;
            return int.MaxValue;
        }

        private static CalibrationTreeNode? FromGroup(A2lGroup group, Dictionary<string, A2lGroup> groups, A2lResolvedModule a2l,
            HashSet<string> listed, HashSet<string> path)
        {
            if (!path.Add(group.Name)) return null; // a SUB_GROUP cycle
            var node = new CalibrationTreeNode
            {
                Title = group.LongIdentifier.Length > 0 ? group.LongIdentifier : group.Name,
                Description = group.Name,
            };
            foreach (var name in group.SubGroups)
                if (groups.TryGetValue(name, out var sub) && FromGroup(sub, groups, a2l, listed, path) is { } child) node.Children.Add(child);
            foreach (var name in group.Characteristics.Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!a2l.Items.TryGetValue(name, out var item)) continue; // unavailable or unknown
                node.Children.Add(Leaf(item));
                listed.Add(name);
            }
            path.Remove(group.Name);
            return node;
        }

        private static CalibrationTreeNode Leaf(A2lCalibrationItem item) => new()
        {
            Title = item.Name.StartsWith("CAL_", StringComparison.Ordinal) ? item.Name[4..] : item.Name,
            Description = item.LongIdentifier,
            Item = item,
        };
    }
}
