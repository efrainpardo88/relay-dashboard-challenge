namespace Relay.Api.Analytics;

public enum PulseStatus
{
    /// <summary>Inside the typical band.</summary>
    Normal,
    Above,
    Below,

    /// <summary>Baseline median below the low-volume threshold. Counts and band are still
    /// shown, but no status badge: a location going 5 -> 9 is "+64%" and is also noise.</summary>
    LowVolume,

    /// <summary>Fewer complete weeks of history than the baseline needs to mean anything.</summary>
    InsufficientHistory,

    /// <summary>Baseline median is zero. There is no percentage change from zero.</summary>
    NoBaseline
}

public sealed record BaselineOptions
{
    public int MinHistoryWeeks { get; init; } = 4;
    public int LowVolumeThreshold { get; init; } = 5;

    /// <summary>Half-width of the typical band, in robust standard deviations. 2 gives
    /// roughly a 95% band for normally distributed counts.</summary>
    public double BandSigmas { get; init; } = 2.0;

    public static BaselineOptions Default { get; } = new();
}

public sealed record BaselineAssessment
{
    public required int Current { get; init; }
    public required int BaselineWeeksUsed { get; init; }
    public required double BaselineMedian { get; init; }
    public required double Mad { get; init; }
    public required double Spread { get; init; }
    public required double TypicalLow { get; init; }
    public required double TypicalHigh { get; init; }

    /// <summary>Null when there is no meaningful baseline to divide by. Never infinity,
    /// never NaN, never a fabricated -100%.</summary>
    public double? DeltaPct { get; init; }

    /// <summary>Signed distance from the baseline in spread units. Used to rank locations
    /// by how unusual they are rather than by raw volume. Null when not comparable.</summary>
    public double? DeviationScore { get; init; }

    public required PulseStatus Status { get; init; }
}

/// <summary>
/// Turns a location's weekly totals into "is this normal for us?".
///
/// Deliberately pure and database-free (PLAN.md Amendment 2) so every branch is unit
/// testable. Two of the branches — InsufficientHistory and NoBaseline — have no real data
/// behind them anywhere in the seed (minimum baseline median across all 69 account/location
/// pairs is 3.5, minimum history 24 weeks), so their tests use synthetic fixtures. That is
/// recorded in PLAN.md Amendment 1 rather than papered over.
/// </summary>
public static class BaselineCalculator
{
    /// <summary>Scales MAD to a standard-deviation equivalent for a normal distribution.</summary>
    private const double MadToSigma = 0.6745;

    public static BaselineAssessment Assess(
        int current,
        IReadOnlyList<int> baselineWeeklyTotals,
        BaselineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(baselineWeeklyTotals);
        var opts = options ?? BaselineOptions.Default;

        if (baselineWeeklyTotals.Count < opts.MinHistoryWeeks)
        {
            return new BaselineAssessment
            {
                Current = current,
                BaselineWeeksUsed = baselineWeeklyTotals.Count,
                BaselineMedian = 0,
                Mad = 0,
                Spread = 0,
                TypicalLow = 0,
                TypicalHigh = 0,
                DeltaPct = null,
                DeviationScore = null,
                Status = PulseStatus.InsufficientHistory
            };
        }

        // Median, not mean. Account 6 carries 805 events on 2026-06-03 against a typical
        // 10.9/day; that single day moves a mean-based weekly baseline by ~3.6x and leaves
        // the median untouched. Seven of the eight low-volume locations in the seed carry
        // that burst week inside their default baseline window, so this is not theoretical.
        var median = Median(baselineWeeklyTotals);

        if (median == 0)
        {
            return new BaselineAssessment
            {
                Current = current,
                BaselineWeeksUsed = baselineWeeklyTotals.Count,
                BaselineMedian = 0,
                Mad = 0,
                Spread = 0,
                TypicalLow = 0,
                TypicalHigh = 0,
                DeltaPct = null,          // dividing by zero is not a percentage
                DeviationScore = null,
                Status = PulseStatus.NoBaseline
            };
        }

        var mad = Median(baselineWeeklyTotals.Select(x => Math.Abs(x - median)).ToList());

        // MAD collapses to zero whenever a MAJORITY of weeks share a value, not only when
        // all of them do — account 12 / Site A reads [8,8,8,8,3,6,8,14], median 8, MAD 0,
        // with real spread present. Without a fallback that location would flag 3 and 14 as
        // extreme. sqrt(median) is the Poisson-ish spread for count data.
        var spread = mad > 0 ? mad / MadToSigma : Math.Sqrt(median);

        var halfWidth = opts.BandSigmas * spread;
        var typicalLow = Math.Max(0, median - halfWidth);   // a week cannot hold fewer than 0 events
        var typicalHigh = median + halfWidth;

        var status = median < opts.LowVolumeThreshold
            ? PulseStatus.LowVolume
            : current < typicalLow ? PulseStatus.Below
            : current > typicalHigh ? PulseStatus.Above
            : PulseStatus.Normal;

        return new BaselineAssessment
        {
            Current = current,
            BaselineWeeksUsed = baselineWeeklyTotals.Count,
            BaselineMedian = median,
            Mad = mad,
            Spread = spread,
            TypicalLow = typicalLow,
            TypicalHigh = typicalHigh,
            DeltaPct = (current - median) / median,
            DeviationScore = spread > 0 ? (current - median) / spread : 0,
            Status = status
        };
    }

    /// <summary>Even-sized sets take the mean of the two central values.</summary>
    public static double Median(IReadOnlyList<int> values)
    {
        if (values.Count == 0) throw new ArgumentException("Median of an empty set is undefined.", nameof(values));

        var sorted = values.OrderBy(v => v).ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
