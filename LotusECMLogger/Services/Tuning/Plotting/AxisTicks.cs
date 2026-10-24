namespace LotusECMLogger.Services.Tuning.Plotting;

/// <summary>"Nice" axis ticks (1, 2, 5 × 10ⁿ steps), after Heckbert's Graphics Gems algorithm.</summary>
public static class AxisTicks
{
    /// <summary>A rounded range covering [min, max] and its tick values.</summary>
    public readonly record struct Ticks(double Min, double Max, double Step, IReadOnlyList<double> Values);

    public static Ticks Compute(double min, double max, int maxTicks = 6)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max))
            (min, max) = (0, 1);
        if (min > max)
            (min, max) = (max, min);
        if (min == max)
        {
            // A flat line: open a small range around it.
            double pad = min == 0 ? 1 : Math.Abs(min) * 0.1;
            min -= pad;
            max += pad;
        }
        maxTicks = Math.Max(2, maxTicks);

        double range = NiceNumber(max - min, round: false);
        double step = NiceNumber(range / (maxTicks - 1), round: true);
        double lo = Math.Floor(min / step) * step;
        double hi = Math.Ceiling(max / step) * step;

        var values = new List<double>();
        // Guard against floating point creep; at most a few dozen ticks.
        for (int i = 0; i <= 100; i++)
        {
            double v = lo + i * step;
            if (v > hi + step * 1e-9)
                break;
            values.Add(Math.Abs(v) < step * 1e-9 ? 0 : v);
        }
        return new Ticks(lo, hi, step, values);
    }

    private static double NiceNumber(double x, bool round)
    {
        if (x <= 0 || !double.IsFinite(x))
            return 1;
        double exp = Math.Floor(Math.Log10(x));
        double f = x / Math.Pow(10, exp);
        double nice = round
            ? f < 1.5 ? 1 : f < 3 ? 2 : f < 7 ? 5 : 10
            : f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10;
        return nice * Math.Pow(10, exp);
    }

    /// <summary>Format that shows ticks of <paramref name="step"/> without needless decimals.</summary>
    public static string FormatFor(double step)
    {
        if (step <= 0 || !double.IsFinite(step))
            return "0.##";
        int decimals = Math.Clamp((int)Math.Ceiling(-Math.Log10(step) - 1e-9), 0, 6);
        return decimals == 0 ? "0" : "0." + new string('0', decimals);
    }
}
