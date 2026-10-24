using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Ccp;

namespace LotusECMLogger.Services.Tuning
{
    /// <summary>Where an input falls on an axis: between point <see cref="Index"/> and the next, <see cref="Fraction"/> of the way.</summary>
    public readonly record struct AxisPosition(int Index, double Fraction)
    {
        /// <summary>The position as a fractional point index, e.g. 3.25.</summary>
        public double At => Index + Fraction;

        /// <summary>The nearest axis point.</summary>
        public int Nearest => (int)Math.Round(At, MidpointRounding.AwayFromZero);
    }

    /// <summary>The live operating point on an item: the position on each bound axis (null when unbound), and the raw inputs.</summary>
    public sealed record OperatingPointSample(AxisPosition? X, AxisPosition? Y, double? XInput, double? YInput);

    public static class OperatingPoint
    {
        /// <summary>
        /// Where a raw input falls on raw axis points, as the lookups walk them: clamped to the first and last points,
        /// otherwise between the last point at or below the input and the next. Repeated points give fraction 0.
        /// </summary>
        public static AxisPosition Locate(IReadOnlyList<double> axis, double input)
        {
            ArgumentNullException.ThrowIfNull(axis);
            if (axis.Count == 0) throw new ArgumentException("The axis has no points.", nameof(axis));
            if (axis.Count == 1 || double.IsNaN(input) || input <= axis[0]) return new(0, 0);
            if (input >= axis[^1]) return new(axis.Count - 1, 0);
            int i = 0;
            while (i + 1 < axis.Count && axis[i + 1] <= input) i++;
            double span = axis[i + 1] - axis[i];
            return new(i, span > 0 ? (input - axis[i]) / span : 0);
        }

        /// <summary>The value at a fractional position: linear along X, bilinear when Y is given. Values are [x, y].</summary>
        public static double Interpolate(double[,] values, AxisPosition x, AxisPosition? y = null)
        {
            int sx = values.GetLength(0), sy = values.GetLength(1);
            int x0 = Math.Clamp(x.Index, 0, sx - 1), x1 = Math.Min(x0 + 1, sx - 1);
            int y0 = Math.Clamp(y?.Index ?? 0, 0, sy - 1), y1 = Math.Min(y0 + 1, sy - 1);
            double fx = x1 == x0 ? 0 : x.Fraction, fy = y is { } yy && y1 != y0 ? yy.Fraction : 0;
            double top = values[x0, y0] + (values[x1, y0] - values[x0, y0]) * fx;
            double bottom = values[x0, y1] + (values[x1, y1] - values[x0, y1]) * fx;
            return top + (bottom - top) * fy;
        }
    }

    /// <summary>
    /// An axis's InputQuantity resolved to the ECU variables to stream: a MEASUREMENT read directly, or a VIRTUAL
    /// measurement whose FORM formula combines its inputs' raw values (X1, X2, …). The result is the raw value the
    /// lookup compares with the raw axis points.
    /// </summary>
    public sealed class AxisInputSource
    {
        private AxisInputSource(string quantity, IReadOnlyList<A2lMeasurement> sources, A2lFormula? formula) =>
            (Quantity, Sources, Formula) = (quantity, sources, formula);

        public string Quantity { get; }
        public IReadOnlyList<A2lMeasurement> Sources { get; }
        public A2lFormula? Formula { get; }

        public double Evaluate(IReadOnlyList<double> sourceRaw) => Formula is null ? sourceRaw[0] : Formula.Evaluate(sourceRaw);

        public static AxisInputSource? Resolve(A2lModule module, string quantity, out string? problem)
        {
            ArgumentNullException.ThrowIfNull(module);
            problem = null;
            if (!module.Measurements.TryGetValue(quantity, out var measurement))
            {
                problem = $"InputQuantity {quantity} is not a MEASUREMENT.";
                return null;
            }
            if (!measurement.IsVirtual) return Readable(measurement, ref problem) ? new(quantity, [measurement], null) : null;
            var conversion = module.Compu(measurement.Conversion);
            if (conversion.Kind != A2lConversionKind.Formula || conversion.Formula is null)
            {
                problem = $"VIRTUAL {quantity} needs a FORM conversion.";
                return null;
            }
            var sources = new List<A2lMeasurement>();
            foreach (var name in measurement.VirtualInputs)
            {
                if (!module.Measurements.TryGetValue(name, out var source) || source.IsVirtual)
                {
                    problem = $"VIRTUAL {quantity} reads {name}, which is not an addressed MEASUREMENT.";
                    return null;
                }
                if (!Readable(source, ref problem)) return null;
                sources.Add(source);
            }
            if (conversion.Formula.InputCount > sources.Count)
            {
                problem = $"VIRTUAL {quantity}'s formula reads X{conversion.Formula.InputCount} but has {sources.Count} input(s).";
                return null;
            }
            return new(quantity, sources, conversion.Formula);
        }

        private static bool Readable(A2lMeasurement measurement, ref string? problem)
        {
            if (measurement.Problem is not null) problem = $"{measurement.Name}: {measurement.Problem}";
            else if (measurement.Address is null) problem = $"{measurement.Name} has no address.";
            else if (measurement.BitMask is not null) problem = $"{measurement.Name} has a BIT_MASK, which the cursor does not apply.";
            else if (measurement.DataType.Size() is not (1 or 2 or 4)) problem = $"{measurement.Name} is not 1, 2 or 4 bytes.";
            return problem is null;
        }

        /// <summary>The variable as the CCP watch streams it: raw (scale 1, offset 0).</summary>
        public static CcpVariable ToVariable(A2lMeasurement m) => new(m.Name, m.Address!.Value, m.DataType.Size(),
            m.DataType is A2lDataType.SByte or A2lDataType.SWord or A2lDataType.SLong, 1, 0, "", null, false, m.LongIdentifier);
    }

    /// <summary>
    /// Streams the inputs of an item's bound axes over DAQ (Phase 6e) and places the operating point on the item's
    /// axes, using the image's raw axis points (the ECU's, while live sync keeps them in step).
    /// </summary>
    public sealed class LiveCursor : IAsyncDisposable
    {
        private readonly CcpVariableWatch watch;
        private readonly (AxisInputSource Source, int[] Slots)?[] axes;

        private LiveCursor(A2lCalibrationItem item, CcpVariableWatch watch, (AxisInputSource, int[])?[] axes) =>
            (Item, this.watch, this.axes) = (item, watch, axes);

        public A2lCalibrationItem Item { get; }
        public bool IsActive => watch.IsActive;

        /// <summary>The InputQuantity of each of the item's axes that can be streamed, or why not.</summary>
        public static (AxisInputSource? X, AxisInputSource? Y, IReadOnlyList<string> Problems) Inputs(A2lModule module, A2lCalibrationItem item)
        {
            var problems = new List<string>();
            AxisInputSource? Resolve(int axis)
            {
                if (axis >= item.Axes.Count || item.Axes[axis].InputQuantity is not { } quantity) return null;
                var source = AxisInputSource.Resolve(module, quantity, out var problem);
                if (problem is not null) problems.Add(problem);
                return source;
            }
            return item.Kind is A2lItemKind.Map or A2lItemKind.Curve ? (Resolve(0), Resolve(1), problems) : (null, null, problems);
        }

        /// <summary>Starts streaming at <paramref name="rateHz"/>, or returns null when no axis of the item has an input.</summary>
        public static async Task<LiveCursor?> StartAsync(CcpMaster master, A2lModule module, A2lCalibrationItem item, int rateHz = 20,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(master);
            var (x, y, _) = Inputs(module, item);
            if (x is null && y is null) return null;
            var variables = new List<CcpVariable>();
            var slots = new Dictionary<string, int>(StringComparer.Ordinal);
            (AxisInputSource, int[])? Plan(AxisInputSource? source)
            {
                if (source is null) return null;
                var indices = source.Sources.Select(m =>
                {
                    if (!slots.TryGetValue(m.Name, out int slot))
                    {
                        slots[m.Name] = slot = variables.Count;
                        variables.Add(AxisInputSource.ToVariable(m));
                    }
                    return slot;
                }).ToArray();
                return (source, indices);
            }
            var axes = new[] { Plan(x), Plan(y) };
            var watch = await CcpVariableWatch.StartAsync(master, variables, rateHz, cancellationToken).ConfigureAwait(false);
            return new LiveCursor(item, watch, axes);
        }

        /// <summary>The latest operating point, or null until every streamed variable has a value.</summary>
        public OperatingPointSample? Read(CalibrationImage image)
        {
            var values = watch.Snapshot();
            if (values.Any(v => v is null)) return null;
            double? Input(int axis) => axes[axis] is { } plan ? plan.Source.Evaluate(plan.Slots.Select(s => values[s]!.Value.Value).ToList()) : null;
            AxisPosition? Position(int axis, double? input) => input is { } v
                ? OperatingPoint.Locate(image.ReadAxisRaw(Item.Axes[axis], Item.ByteOrder), v) : null;
            double? xi = Input(0), yi = Input(1);
            return new OperatingPointSample(Position(0, xi), Position(1, yi), xi, yi);
        }

        public ValueTask DisposeAsync() => watch.DisposeAsync();
    }
}
