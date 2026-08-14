namespace Relay.Api.Data.Seeding;

/// <summary>A span of UTC time during which an account's zone has one constant offset.</summary>
public readonly record struct UtcOffsetSegment(DateTime FromUtc, DateTime ToUtcExclusive, int OffsetMinutes);

/// <summary>
/// Splits a UTC range into the periods where a given IANA zone holds a constant offset.
/// This is the whole of the timezone reasoning, deliberately kept as pure C# with no
/// database involved so it is unit-testable — the seed range spans the 2026-03-08 DST
/// transition, so a single fixed offset per account would be wrong.
/// </summary>
public static class TimeZoneSegmenter
{
    /// <summary>
    /// Scans hourly. DST transitions in every zone used by the dataset occur on the hour,
    /// so hourly resolution places each boundary exactly rather than approximately.
    /// A daily scan would misplace a transition by up to a day and mis-bucket events.
    /// </summary>
    public static IReadOnlyList<UtcOffsetSegment> Segment(TimeZoneInfo zone, DateTime fromUtc, DateTime toUtcExclusive)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (toUtcExclusive <= fromUtc) return [];

        var start = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(toUtcExclusive, DateTimeKind.Utc);

        var segments = new List<UtcOffsetSegment>();
        var segmentStart = start;
        var segmentOffset = OffsetMinutesAt(zone, start);

        for (var cursor = start.AddHours(1); cursor < end; cursor = cursor.AddHours(1))
        {
            var offset = OffsetMinutesAt(zone, cursor);
            if (offset == segmentOffset) continue;

            segments.Add(new UtcOffsetSegment(segmentStart, cursor, segmentOffset));
            segmentStart = cursor;
            segmentOffset = offset;
        }

        segments.Add(new UtcOffsetSegment(segmentStart, end, segmentOffset));
        return segments;
    }

    private static int OffsetMinutesAt(TimeZoneInfo zone, DateTime utcInstant)
        => (int)zone.GetUtcOffset(utcInstant).TotalMinutes;
}
