using Relay.Api.Analytics;
using Relay.Api.Data.Seeding;

namespace Relay.Api.Tests;

public class WeekMathTests
{
    [Theory]
    [InlineData("2026-07-20", "2026-07-20")]   // a Monday is its own week start
    [InlineData("2026-07-26", "2026-07-20")]   // Sunday belongs to the week that began Monday
    [InlineData("2026-07-27", "2026-07-27")]   // next Monday starts a new week
    public void MondayOf_anchors_to_the_start_of_the_iso_week(string input, string expected)
        => Assert.Equal(DateOnly.Parse(expected), WeekMath.MondayOf(DateOnly.Parse(input)));

    [Fact]
    public void Data_ending_on_a_monday_shows_the_week_that_just_closed()
    {
        // The seed's last local date is Monday 2026-07-27. The week containing it is still
        // in progress, so the dashboard must show 2026-07-20..26 -- which is exactly the
        // Monday-morning review the ticket describes.
        Assert.Equal(DateOnly.Parse("2026-07-20"),
            WeekMath.LastCompleteWeekStart(DateOnly.Parse("2026-07-27")));
    }

    [Fact]
    public void Data_ending_on_a_sunday_shows_that_same_week()
    {
        // A week whose Sunday is covered is complete and must not be skipped.
        Assert.Equal(DateOnly.Parse("2026-07-20"),
            WeekMath.LastCompleteWeekStart(DateOnly.Parse("2026-07-26")));
    }

    [Fact]
    public void A_partial_week_is_never_shown_as_if_it_were_whole()
    {
        // Saturday: six of seven days present. Showing it would understate every location.
        Assert.Equal(DateOnly.Parse("2026-07-13"),
            WeekMath.LastCompleteWeekStart(DateOnly.Parse("2026-07-25")));
    }
}

public class TimeZoneSegmenterTests
{
    private static readonly DateTime SeedStart = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SeedEnd = new(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_zone_with_dst_splits_the_seed_range_in_two()
    {
        // America/Chicago moves from -06:00 to -05:00 on 2026-03-08. One fixed offset for
        // the whole range would mis-bucket every event on one side of that date.
        var segments = TimeZoneSegmenter.Segment(Tz("America/Chicago"), SeedStart, SeedEnd);

        Assert.Equal(2, segments.Count);
        Assert.Equal(-360, segments[0].OffsetMinutes);   // CST
        Assert.Equal(-300, segments[1].OffsetMinutes);   // CDT
    }

    [Fact]
    public void The_transition_lands_on_the_exact_hour_not_the_nearest_day()
    {
        // 2026-03-08 02:00 local == 08:00 UTC. An hourly scan places this precisely; a daily
        // scan would be off by up to 24 hours and put real events in the wrong local day.
        var segments = TimeZoneSegmenter.Segment(Tz("America/Chicago"), SeedStart, SeedEnd);

        Assert.Equal(new DateTime(2026, 3, 8, 8, 0, 0, DateTimeKind.Utc), segments[1].FromUtc);
    }

    [Fact]
    public void A_zone_without_dst_stays_a_single_segment()
    {
        // Phoenix does not observe DST, and two accounts in the seed live there.
        var segments = TimeZoneSegmenter.Segment(Tz("America/Phoenix"), SeedStart, SeedEnd);

        Assert.Single(segments);
        Assert.Equal(-420, segments[0].OffsetMinutes);
    }

    [Fact]
    public void Utc_is_a_single_zero_offset_segment()
    {
        var segments = TimeZoneSegmenter.Segment(Tz("UTC"), SeedStart, SeedEnd);

        Assert.Single(segments);
        Assert.Equal(0, segments[0].OffsetMinutes);
    }

    [Fact]
    public void Segments_tile_the_whole_range_with_no_gap_and_no_overlap()
    {
        // A gap here means events with no local date, which the seed loader treats as a
        // hard failure. Worth asserting directly rather than only through that guard.
        var segments = TimeZoneSegmenter.Segment(Tz("America/Los_Angeles"), SeedStart, SeedEnd);

        Assert.Equal(SeedStart, segments[0].FromUtc);
        Assert.Equal(SeedEnd, segments[^1].ToUtcExclusive);
        for (var i = 1; i < segments.Count; i++)
            Assert.Equal(segments[i - 1].ToUtcExclusive, segments[i].FromUtc);
    }

    [Fact]
    public void An_empty_range_produces_no_segments()
        => Assert.Empty(TimeZoneSegmenter.Segment(Tz("UTC"), SeedStart, SeedStart));

    /// <summary>IANA ids resolve on .NET 6+ regardless of host OS. This is the reason the
    /// conversion lives in C# rather than in T-SQL, where these ids are rejected outright
    /// (verified in PLAN.md Amendment 3).</summary>
    private static TimeZoneInfo Tz(string ianaId) => TimeZoneInfo.FindSystemTimeZoneById(ianaId);
}
