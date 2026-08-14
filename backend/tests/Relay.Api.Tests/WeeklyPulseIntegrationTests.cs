using Microsoft.EntityFrameworkCore;
using Relay.Api.Data.Seeding;

namespace Relay.Api.Tests;

/// <summary>
/// Aggregates checked against the seed. Every expected number here was computed
/// independently in Python first (see analysis/) — if the C# and the SQL agree with each
/// other but disagree with these, the pipeline is wrong, which is the failure a test
/// written from the implementation would never catch.
/// </summary>
[Collection(SeededDatabaseCollection.Name)]
public class WeeklyPulseIntegrationTests(SeededDatabaseFixture fx)
{
    private static readonly DateOnly DefaultWeek = new(2026, 7, 20);

    [Fact]
    public async Task Seed_loads_the_expected_shape()
    {
        Assert.Equal(20, await fx.Db.Accounts.CountAsync());
        Assert.Equal(12_626, await fx.Db.ActivityEvents.CountAsync());
        Assert.Equal(0, await fx.Db.ActivityEvents.CountAsync(e => e.OccurredLocalDate == null));
    }

    [Fact]
    public void Reseeding_an_already_seeded_database_is_a_no_op()
    {
        // seed.sql is 12.6k INSERTs with explicit ids; a second run would violate the
        // primary key. The fixture just ran the loader against a live database.
        Assert.Contains(fx.SeedOutcome.Result, new[] { SeedResult.Applied, SeedResult.AlreadyApplied });
    }

    [Fact]
    public async Task The_planted_burst_day_is_present_and_untouched()
    {
        // 805 events on one day against a typical 10.9. The data is not cleaned: the median
        // baseline is what makes it harmless.
        var count = await fx.Db.ActivityEvents.CountAsync(e =>
            e.AccountId == 6 &&
            e.OccurredAt >= new DateTime(2026, 6, 3) &&
            e.OccurredAt < new DateTime(2026, 6, 4));

        Assert.Equal(805, count);
    }

    [Fact]
    public async Task Exactly_twelve_exact_duplicates_exist_across_the_dataset()
    {
        var raw = await fx.Db.ActivityEvents.CountAsync();
        var distinct = await fx.Db.ActivityEvents
            .Select(e => new { e.AccountId, e.Location, e.EventType, e.OccurredAt, e.DurationSeconds, e.Outcome })
            .Distinct()
            .CountAsync();

        Assert.Equal(12, raw - distinct);
    }

    [Fact]
    public async Task Default_week_comes_from_the_data_and_not_from_the_clock()
    {
        // The seed ends Monday 2026-07-27, months behind wall time. Using the system clock
        // would render an empty dashboard.
        var pulse = await fx.Pulse.GetWeeklyPulseAsync(6, null, 8, "all");

        Assert.NotNull(pulse);
        Assert.Equal(DefaultWeek, pulse.Week.Start);
        Assert.Equal(new DateOnly(2026, 7, 26), pulse.Week.End);
        Assert.Equal(new DateOnly(2026, 5, 25), pulse.Baseline.Start);
    }

    [Fact]
    public async Task Account_rollup_matches_the_hand_checked_arithmetic()
    {
        // Baseline weekly totals for account 6 are [53, 881, 102, 59, 76, 69, 79, 50].
        //   median = (69 + 76) / 2                 = 72.5
        //   MAD    = median of deviations          = 16.5
        //   spread = 16.5 / 0.6745                 = 24.46
        //   band   = 72.5 +/- 2 * 24.46            = [23.57, 121.43]
        // Current week is 87 -> inside the band.
        var pulse = await fx.Pulse.GetWeeklyPulseAsync(6, DefaultWeek, 8, "all");

        Assert.NotNull(pulse!.Total);
        Assert.Equal(87, pulse.Total.Current);
        Assert.Equal(72.5, pulse.Total.BaselineMedian);
        Assert.Equal(23.57, pulse.Total.TypicalLow, 2);
        Assert.Equal(121.43, pulse.Total.TypicalHigh, 2);
        Assert.Equal("normal", pulse.Total.Status);
    }

    [Fact]
    public async Task A_mean_baseline_would_call_this_normal_week_a_collapse()
    {
        // The burst week (881) sits inside the default baseline window. Mean of the eight
        // baseline weeks is 171.125, so a mean-based comparison would report the current 87
        // as roughly -49%. This is the single most important behavioural difference in the
        // feature, and it is visible on the default screen rather than only in a fixture.
        var pulse = await fx.Pulse.GetWeeklyPulseAsync(6, DefaultWeek, 8, "all");

        int[] baselineWeeks = [53, 881, 102, 59, 76, 69, 79, 50];
        var meanDelta = (87 - baselineWeeks.Average()) / baselineWeeks.Average();

        Assert.InRange(meanDelta, -0.50, -0.48);          // what a mean would have said
        Assert.InRange(pulse!.Total!.DeltaPct!.Value, 0.19, 0.21);   // what the median says
    }

    [Fact]
    public async Task Every_location_gets_a_full_run_of_weeks_including_the_silent_ones()
    {
        // GROUP BY only returns weeks that have rows. If gap filling were missing, a
        // location with a quiet week would have a shorter baseline and a wrong median.
        var pulse = await fx.Pulse.GetWeeklyPulseAsync(6, DefaultWeek, 8, "all");

        Assert.NotEmpty(pulse!.Locations);
        Assert.All(pulse.Locations, l => Assert.Equal(8, l.Metric.BaselineWeeksUsed));
    }

    [Fact]
    public async Task Low_volume_locations_match_the_independently_computed_medians()
    {
        // From analysis/03: the eight low-volume locations under the default window, seven
        // of them account 6 sites whose baseline carries the burst week.
        var expected = new Dictionary<string, (int Current, double Median)>
        {
            ["Site J"] = (5, 3.5),
            ["Site M"] = (7, 3.5),
            ["Site I"] = (6, 4.0),
            ["Site D"] = (4, 4.5),
            ["Site G"] = (6, 4.5),
            ["Site N"] = (6, 4.5),
            ["Site O"] = (8, 4.5),
        };

        var pulse = await fx.Pulse.GetWeeklyPulseAsync(6, DefaultWeek, 8, "all");
        var byLocation = pulse!.Locations.ToDictionary(l => l.Location, l => l.Metric);

        foreach (var (location, (current, median)) in expected)
        {
            Assert.Equal(current, byLocation[location].Current);
            Assert.Equal(median, byLocation[location].BaselineMedian);
            Assert.Equal("lowVolume", byLocation[location].Status);
        }
    }

    [Fact]
    public async Task Deduplication_is_applied_and_reported()
    {
        // Four of the twelve duplicates belong to account 6, but only the 2026-06-03 one
        // falls inside the default window.
        var pulse = await fx.Pulse.GetWeeklyPulseAsync(6, DefaultWeek, 8, "all");

        Assert.Equal(1, pulse!.DataQuality.DuplicateEventsExcluded);
    }

    [Fact]
    public async Task An_account_with_no_events_renders_instead_of_failing()
    {
        // Quiet Harbor Spa. Not a 404, not a 500, no NaN — an empty state with a real week.
        var pulse = await fx.Pulse.GetWeeklyPulseAsync(20, null, 8, "all");

        Assert.NotNull(pulse);
        Assert.False(pulse.DataQuality.HasData);
        Assert.Null(pulse.Total);
        Assert.Empty(pulse.Locations);
        Assert.Equal(DefaultWeek, pulse.Week.Start);   // still shows which week it is empty for
    }

    [Fact]
    public async Task An_unknown_account_is_absent_rather_than_empty()
        => Assert.Null(await fx.Pulse.GetWeeklyPulseAsync(9999, null, 8, "all"));

    [Fact]
    public async Task Location_names_are_scoped_to_their_account()
    {
        // 'Site A' exists in 19 of the 20 accounts. An unscoped query would blend them.
        var six = await fx.Pulse.GetWeeklyPulseAsync(6, DefaultWeek, 8, "all");
        var sixteen = await fx.Pulse.GetWeeklyPulseAsync(16, DefaultWeek, 8, "all");

        var a6 = six!.Locations.Single(l => l.Location == "Site A").Metric.Current;
        var a16 = sixteen!.Locations.Single(l => l.Location == "Site A").Metric.Current;

        Assert.Equal(9, a6);
        Assert.Equal(8, a16);
        Assert.Single(sixteen.Locations);   // account 16 is single-site
    }

    [Fact]
    public async Task Filtering_by_event_type_reduces_every_total()
    {
        var all = await fx.Pulse.GetWeeklyPulseAsync(6, DefaultWeek, 8, "all");
        var calls = await fx.Pulse.GetWeeklyPulseAsync(6, DefaultWeek, 8, "call_received");

        Assert.True(calls!.Total!.Current < all!.Total!.Current);
        Assert.Equal(all.Locations.Count, calls.Locations.Count);   // the spine is unchanged
    }

    [Fact]
    public async Task A_wider_baseline_window_uses_more_weeks()
    {
        var four = await fx.Pulse.GetWeeklyPulseAsync(6, DefaultWeek, 4, "all");
        var twelve = await fx.Pulse.GetWeeklyPulseAsync(6, DefaultWeek, 12, "all");

        Assert.Equal(4, four!.Total!.BaselineWeeksUsed);
        Assert.Equal(12, twelve!.Total!.BaselineWeeksUsed);
        Assert.Equal(new DateOnly(2026, 6, 22), four.Baseline.Start);
    }
}
