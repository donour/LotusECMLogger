namespace LotusECMLogger.Services.A2l
{
    public enum A2lItemKind { Value, ValBlk, Curve, Map, Ascii, AxisPts }

    /// <summary>One axis of a resolved curve or map: stored points (<see cref="Address"/>) or fixed ones.</summary>
    public sealed record A2lAxisLayout(int Count, A2lDataType Type, uint Address, A2lCompuMethod Conversion,
        IReadOnlyList<double>? FixedRaw, string? InputQuantity, string? AxisPtsName)
    {
        public bool IsStored => FixedRaw is null;

        public uint PointAddress(int index)
        {
            if (!IsStored) throw new InvalidOperationException("A fixed axis has no stored points.");
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return Address + (uint)(index * Type.Size());
        }
    }

    /// <summary>
    /// A calibration item with every address worked out: where each cell is, and where its axes are. Built by
    /// <see cref="A2lLayout.Resolve(A2lModule)"/> only when nothing about it is unsupported.
    /// </summary>
    public sealed class A2lCalibrationItem
    {
        public required string Name { get; init; }
        public string LongIdentifier { get; init; } = "";
        public required A2lItemKind Kind { get; init; }
        public required A2lDataType ValueType { get; init; }
        public required uint Address { get; init; }
        /// <summary>Cells along X (the first AXIS_DESCR); the element count for VALUE, VAL_BLK, ASCII and axes.</summary>
        public required int CountX { get; init; }
        public int CountY { get; init; } = 1;
        public A2lIndexMode IndexMode { get; init; } = A2lIndexMode.RowDir;
        public required A2lCompuMethod Conversion { get; init; }
        public A2lByteOrder ByteOrder { get; init; }
        public double LowerLimit { get; init; }
        public double UpperLimit { get; init; }
        public IReadOnlyList<A2lAxisLayout> Axes { get; init; } = [];
        /// <summary>Why the item may be read but not written, or null when it is editable.</summary>
        public string? ReadOnlyReason { get; init; }

        public int ElementCount => CountX * CountY;
        public int ElementSize => ValueType.Size();
        public bool IsEditable => ReadOnlyReason is null;

        /// <summary>The address of cell (x, y). X is the first AXIS_DESCR; ROW_DIR stores X fastest.</summary>
        public uint CellAddress(int x, int y = 0)
        {
            if ((uint)x >= (uint)CountX) throw new ArgumentOutOfRangeException(nameof(x));
            if ((uint)y >= (uint)CountY) throw new ArgumentOutOfRangeException(nameof(y));
            int index = IndexMode == A2lIndexMode.RowDir ? y * CountX + x : x * CountY + y;
            return Address + (uint)(index * ElementSize);
        }

        /// <summary>Every byte range this item or its own (STD_AXIS) points occupy; shared axes are separate items.</summary>
        public IEnumerable<(uint Address, int Length)> Spans()
        {
            yield return (Address, ElementCount * ElementSize);
            foreach (var axis in Axes.Where(a => a.IsStored && a.AxisPtsName is null))
                yield return (axis.Address, axis.Count * axis.Type.Size());
        }
    }

    public sealed record A2lUnavailableItem(string Name, string Reason);

    public sealed record A2lResolvedModule(A2lModule Module, IReadOnlyDictionary<string, A2lCalibrationItem> Items,
        IReadOnlyList<A2lUnavailableItem> Unavailable);

    public static class A2lLayout
    {
        /// <summary>Resolves every CHARACTERISTIC and AXIS_PTS; anything unsupported is listed with its reason.</summary>
        public static A2lResolvedModule Resolve(A2lModule module)
        {
            var items = new Dictionary<string, A2lCalibrationItem>(StringComparer.Ordinal);
            var unavailable = new List<A2lUnavailableItem>();
            foreach (var axis in module.AxisPts.Values)
            {
                if (ResolveAxisPts(module, axis, out var reason) is { } item) items[item.Name] = item;
                else unavailable.Add(new(axis.Name, reason!));
            }
            foreach (var characteristic in module.Characteristics.Values)
            {
                if (ResolveCharacteristic(module, characteristic, out var reason) is not { } item)
                    unavailable.Add(new(characteristic.Name, reason!));
                else if (!items.TryAdd(item.Name, item))
                    unavailable.Add(new(characteristic.Name, "an AXIS_PTS has the same name."));
            }
            return new A2lResolvedModule(module, items, unavailable.OrderBy(u => u.Name, StringComparer.Ordinal).ToList());
        }

        public static A2lCalibrationItem? ResolveAxisPts(A2lModule module, A2lAxisPts axis, out string? reason)
        {
            reason = axis.Problem;
            if (reason is not null) return null;
            if (!module.RecordLayouts.TryGetValue(axis.RecordLayout, out var layout)) { reason = $"RECORD_LAYOUT {axis.RecordLayout} is not defined."; return null; }
            if (layout.Problem is not null) { reason = layout.Problem; return null; }
            if (layout.AxisPtsX is null || layout.FncValues is not null || layout.AxisPtsY is not null)
            {
                reason = $"RECORD_LAYOUT {layout.Name} is not a plain AXIS_PTS_X layout.";
                return null;
            }
            var conversion = module.Compu(axis.Conversion);
            if (conversion.Problem is not null) { reason = conversion.Problem; return null; }
            int count = layout.FixNoAxisPtsX ?? axis.MaxAxisPoints;
            if (count <= 0) { reason = "it has no axis points."; return null; }
            return new A2lCalibrationItem
            {
                Name = axis.Name, LongIdentifier = axis.LongIdentifier, Kind = A2lItemKind.AxisPts,
                ValueType = layout.AxisPtsX.Type, Address = axis.Address, CountX = count, Conversion = conversion,
                ByteOrder = axis.ByteOrder ?? module.ByteOrder, LowerLimit = axis.LowerLimit, UpperLimit = axis.UpperLimit,
                ReadOnlyReason = ReadOnlyReason(axis.ReadOnly, conversion),
            };
        }

        public static A2lCalibrationItem? ResolveCharacteristic(A2lModule module, A2lCharacteristic c, out string? reason)
        {
            reason = c.Problem;
            if (reason is not null) return null;
            if (!module.RecordLayouts.TryGetValue(c.RecordLayout, out var layout)) { reason = $"RECORD_LAYOUT {c.RecordLayout} is not defined."; return null; }
            if (layout.Problem is not null) { reason = layout.Problem; return null; }
            if (layout.FncValues is null) { reason = $"RECORD_LAYOUT {layout.Name} has no FNC_VALUES."; return null; }
            var conversion = module.Compu(c.Conversion);
            if (conversion.Problem is not null) { reason = conversion.Problem; return null; }
            var byteOrder = c.ByteOrder ?? module.ByteOrder;
            var valueType = layout.FncValues.Type;

            var kind = c.Kind switch
            {
                A2lCharacteristicKind.Value => A2lItemKind.Value,
                A2lCharacteristicKind.ValBlk => A2lItemKind.ValBlk,
                A2lCharacteristicKind.Curve => A2lItemKind.Curve,
                A2lCharacteristicKind.Map => A2lItemKind.Map,
                _ => A2lItemKind.Ascii,
            };
            if (kind is A2lItemKind.Value or A2lItemKind.ValBlk or A2lItemKind.Ascii)
            {
                if (layout.AxisPtsX is not null || layout.AxisPtsY is not null)
                {
                    reason = $"RECORD_LAYOUT {layout.Name} stores axis points, which a {c.Kind} has none of.";
                    return null;
                }
                if (kind == A2lItemKind.Ascii && valueType.Size() != 1) { reason = "ASCII needs a byte-sized FNC_VALUES."; return null; }
                return new A2lCalibrationItem
                {
                    Name = c.Name, LongIdentifier = c.LongIdentifier, Kind = kind, ValueType = valueType, Address = c.Address,
                    CountX = kind == A2lItemKind.Value ? 1 : c.Number, Conversion = conversion, ByteOrder = byteOrder,
                    LowerLimit = c.LowerLimit, UpperLimit = c.UpperLimit, ReadOnlyReason = ReadOnlyReason(c.ReadOnly, conversion),
                };
            }

            // Curves and maps: lay out the record's own items (STD_AXIS points, then values) in Position order.
            var axes = new A2lAxisLayout[c.Axes.Count];
            var stored = new List<(int Position, A2lDataType Type, int Count, int Slot)>(); // slot -1 = values
            for (int i = 0; i < c.Axes.Count; i++)
            {
                var descr = c.Axes[i];
                if (descr.Problem is not null) { reason = $"axis {i + 1}: {descr.Problem}"; return null; }
                var axisLayoutItem = i == 0 ? layout.AxisPtsX : layout.AxisPtsY;
                switch (descr.Kind)
                {
                    case A2lAxisKind.Common:
                        if (!module.AxisPts.TryGetValue(descr.AxisPtsRef!, out var pts)) { reason = $"AXIS_PTS {descr.AxisPtsRef} is not defined."; return null; }
                        if (ResolveAxisPts(module, pts, out var axisReason) is not { } shared) { reason = $"AXIS_PTS {pts.Name}: {axisReason}"; return null; }
                        if (shared.ByteOrder != byteOrder) { reason = $"AXIS_PTS {pts.Name} has a different byte order."; return null; }
                        axes[i] = new A2lAxisLayout(shared.CountX, shared.ValueType, shared.Address, shared.Conversion, null,
                            descr.InputQuantity, pts.Name);
                        break;
                    case A2lAxisKind.Fixed:
                        axes[i] = new A2lAxisLayout(descr.FixedRawPoints!.Count, A2lDataType.Float64, 0, module.Compu(descr.Conversion),
                            descr.FixedRawPoints, descr.InputQuantity, null);
                        break;
                    default:
                        if (axisLayoutItem is null) { reason = $"STD_AXIS {i + 1} has no AXIS_PTS_{(i == 0 ? 'X' : 'Y')} in {layout.Name}."; return null; }
                        int count = (i == 0 ? layout.FixNoAxisPtsX : layout.FixNoAxisPtsY) ?? descr.MaxAxisPoints;
                        stored.Add((axisLayoutItem.Position, axisLayoutItem.Type, count, i));
                        axes[i] = new A2lAxisLayout(count, axisLayoutItem.Type, 0, module.Compu(descr.Conversion), null, descr.InputQuantity, null);
                        break;
                }
                if (axes[i].Conversion.Problem is { } conversionProblem) { reason = $"axis {i + 1}: {conversionProblem}"; return null; }
                if (axes[i].Count <= 0) { reason = $"axis {i + 1} has no points."; return null; }
            }
            int countX = axes[0].Count, countY = kind == A2lItemKind.Map ? axes[1].Count : 1;
            stored.Add((layout.FncValues.Position, valueType, countX * countY, -1));

            uint offset = 0, valueAddress = c.Address;
            foreach (var item in stored.OrderBy(s => s.Position))
            {
                int align = layout.Alignment.GetValueOrDefault(item.Type, item.Type.Size());
                uint address = c.Address + offset;
                address = (uint)((address + align - 1) / align * align);
                if (item.Slot < 0) valueAddress = address;
                else axes[item.Slot] = axes[item.Slot] with { Address = address };
                offset = address - c.Address + (uint)(item.Count * item.Type.Size());
            }

            bool axisReadOnly = c.Axes.Any(a => a.ReadOnly && a.Kind == A2lAxisKind.Standard);
            return new A2lCalibrationItem
            {
                Name = c.Name, LongIdentifier = c.LongIdentifier, Kind = kind, ValueType = valueType, Address = valueAddress,
                CountX = countX, CountY = countY, IndexMode = layout.IndexMode, Conversion = conversion, ByteOrder = byteOrder,
                LowerLimit = c.LowerLimit, UpperLimit = c.UpperLimit, Axes = axes,
                ReadOnlyReason = ReadOnlyReason(c.ReadOnly || axisReadOnly, conversion),
            };
        }

        private static string? ReadOnlyReason(bool readOnly, A2lCompuMethod conversion) =>
            readOnly ? "marked READ_ONLY in the A2L."
            : !conversion.CanInvert ? $"COMPU_METHOD {conversion.Name} has no inverse, so physical values cannot be written."
            : null;
    }
}
