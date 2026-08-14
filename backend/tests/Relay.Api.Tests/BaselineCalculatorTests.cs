using Relay.Api.Analytics;

namespace Relay.Api.Tests;

/// <summary>
/// The statistics that decide what a customer is told. No database — that split is the
/// point of PLAN.md Amendment 2.
///
/// Where a fixture is a real series from the seed it says so and cites the account and
/// location; those numbers come from analysis/, never from intuition. Two branches
/// (InsufficientHistory, NoBaseline) have no real data behind them anywhere in the dataset,
/// so they use synthetic fixtures — recorded in PLAN.md Amendment 1 rather than dressed up
/// as coverage of real rows.
/// </summary>
public class BaselineCalculatorTests
{
    // Account 6 / Site J, default window. The 56 is the planted burst week.
    private static readonly int[] SiteJWithBurst = [4, 56, 11, 2, 3, 3, 5, 3];

    // Account 12 / Site A, default window. A majority of weeks share the value 8.
    private static readonly int[] SiteAMajorityIdentical = [8, 8, 8, 8, 3, 6, 8, 14];

    [Fact]
    public void Burst_week_does_not_move_the_baseline()
    {
        var withBurst = BaselineCalculator.Assess(5, SiteJWithBurst);
        var withoutBurst = BaselineCalculator.Assess(5, [4, 11, 2, 3, 3, 5, 3]);

        Assert.Equal(3.5, withBurst.BaselineMedian);

        // The mean of the same series is 10.875 -- more than 3x the median. A mean-based
        // baseline would report this normal week as a 54% collapse.
        Assert.Equal(10.875, SiteJWithBurst.Average(), 3);
        Assert.True(withBurst.DeltaPct > 0, "Median baseline sees this week as above normal.");

        // Removing the outlier barely moves the median: that is the property being bought.
        Assert.InRange(Math.Abs(withBurst.BaselineMedian - withoutBurst.BaselineMedian), 0, 0.5);
    }

    [Fact]
    public void Silent_weeks_count_as_zero_rather_than_being_ignored()
    {
        // The SQL gap-fills; the calculator must then treat those zeros as real observations.
        // If zeros were dropped, the median here would be 5 instead of 2.5 and a location
        // that trades every other week would look perfectly healthy.
        var assessment = BaselineCalculator.Assess(0, [5, 0, 5, 0, 5, 0, 5, 0]);

        Assert.Equal(2.5, assessment.BaselineMedian);
        Assert.Equal(8, assessment.BaselineWeeksUsed);
    }

    [Fact]
    public void Zero_baseline_yields_no_percentage_at_all()
    {
        // Synthetic: no location in the seed has a zero baseline (minimum median is 3.5).
        var assessment = BaselineCalculator.Assess(4, [0, 0, 0, 0, 0, 0, 0, 0]);

        Assert.Equal(PulseStatus.NoBaseline, assessment.Status);
        Assert.Null(assessment.DeltaPct);          // not infinity, not NaN, not a fake +400%
        Assert.Null(assessment.DeviationScore);
        Assert.Equal(4, assessment.Current);       // the raw count is still reported
    }

    [Fact]
    public void Too_little_history_is_reported_rather_than_guessed()
    {
        // Synthetic: every location in the seed has at least 24 complete weeks.
        var assessment = BaselineCalculator.Assess(9, [7, 8, 6]);

        Assert.Equal(PulseStatus.InsufficientHistory, assessment.Status);
        Assert.Equal(3, assessment.BaselineWeeksUsed);
        Assert.Null(assessment.DeltaPct);
    }

    [Fact]
    public void Mad_of_zero_falls_back_to_a_count_based_spread()
    {
        // Real: account 12 / Site A. MAD collapses because a MAJORITY of weeks read 8, not
        // because the series is flat -- there is genuine spread (3 and 14) present. Without
        // the fallback the band would have zero width and both would flag as extreme.
        var assessment = BaselineCalculator.Assess(7, SiteAMajorityIdentical);

        Assert.Equal(8, assessment.BaselineMedian);
        Assert.Equal(0, assessment.Mad);
        Assert.Equal(Math.Sqrt(8), assessment.Spread, 6);
        Assert.True(assessment.TypicalHigh > assessment.TypicalLow, "Band must have width.");
        Assert.False(double.IsNaN(assessment.DeviationScore ?? 0));
        Assert.False(double.IsInfinity(assessment.DeviationScore ?? 0));
    }

    [Fact]
    public void Low_volume_locations_report_counts_but_suppress_the_verdict()
    {
        // Real: account 6 / Site J. 5 against a baseline of 3.5 is "+43%" and is also noise.
        var assessment = BaselineCalculator.Assess(5, SiteJWithBurst);

        Assert.Equal(PulseStatus.LowVolume, assessment.Status);
        Assert.Equal(5, assessment.Current);
        Assert.NotNull(assessment.DeltaPct);   // the number is still available to show
    }

    [Theory]
    [InlineData(9, PulseStatus.Normal)]    // dead on the median
    [InlineData(15, PulseStatus.Normal)]   // exactly on the upper edge -- inclusive
    [InlineData(16, PulseStatus.Above)]    // one past it
    [InlineData(3, PulseStatus.Normal)]    // exactly on the lower edge -- inclusive
    [InlineData(2, PulseStatus.Below)]
    public void Band_edges_are_inclusive(int current, PulseStatus expected)
    {
        // Chosen so the band edges land on whole counts, which is the only way to test
        // inclusivity with an integer event count.
        //   sorted [2,9,9,9,9,9,9,16] -> median 9
        //   deviations [0,0,0,0,0,0,7,7] -> MAD 0, so spread falls back to sqrt(9) = 3
        //   band = 9 +/- 2*3 = [3, 15]
        int[] baseline = [9, 9, 9, 9, 9, 2, 16, 9];
        var assessment = BaselineCalculator.Assess(current, baseline);

        Assert.Equal(9, assessment.BaselineMedian);
        Assert.Equal(3, assessment.TypicalLow);
        Assert.Equal(15, assessment.TypicalHigh);
        Assert.Equal(expected, assessment.Status);
    }

    [Fact]
    public void Band_width_follows_the_observed_spread()
    {
        // Same median, real MAD this time:
        //   sorted [8,8,10,10,10,10,12,12] -> median 10
        //   deviations [0,0,0,0,2,2,2,2] -> MAD 1 -> spread 1/0.6745 = 1.4826
        //   band = 10 +/- 2.9652 = [7.03, 12.97]
        // A steadier location earns a tighter band, so the same absolute swing reads as
        // unusual here and as ordinary somewhere noisier.
        var assessment = BaselineCalculator.Assess(13, [10, 10, 8, 12, 8, 12, 10, 10]);

        Assert.Equal(10, assessment.BaselineMedian);
        Assert.Equal(1, assessment.Mad);
        Assert.Equal(7.03, assessment.TypicalLow, 2);
        Assert.Equal(12.97, assessment.TypicalHigh, 2);
        Assert.Equal(PulseStatus.Above, assessment.Status);
    }

    [Fact]
    public void Typical_band_never_goes_below_zero()
    {
        // A week cannot contain a negative number of events, so the band floor is 0 even
        // when median - 2*spread is negative.
        var assessment = BaselineCalculator.Assess(6, [6, 1, 9, 2, 8, 3, 7, 5]);

        Assert.Equal(0, assessment.TypicalLow);
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3 }, 2.0)]
    [InlineData(new[] { 1, 2, 3, 4 }, 2.5)]
    [InlineData(new[] { 5 }, 5.0)]
    [InlineData(new[] { 3, 1, 2 }, 2.0)]   // unsorted input
    public void Median_handles_odd_even_and_unsorted(int[] values, double expected)
        => Assert.Equal(expected, BaselineCalculator.Median(values));

    [Fact]
    public void Median_of_nothing_is_an_error_not_a_zero()
        => Assert.Throws<ArgumentException>(() => BaselineCalculator.Median(Array.Empty<int>()));
}
