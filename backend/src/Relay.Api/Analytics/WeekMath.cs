namespace Relay.Api.Analytics;

/// <summary>Monday-start week arithmetic, in whatever calendar the caller already resolved
/// to (always account-local here — never UTC).</summary>
public static class WeekMath
{
    public static DateOnly MondayOf(DateOnly date)
        => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>
    /// The most recent Monday–Sunday week that is fully covered by data ending on
    /// <paramref name="lastLocalDate"/>. The seed ends on Monday 2026-07-27, so this
    /// returns 2026-07-20 — the week just gone, which is what a customer admin reviews on
    /// a Monday morning. Anchoring to the system clock instead would render an empty
    /// dashboard, since the data is months behind wall time.
    /// </summary>
    public static DateOnly LastCompleteWeekStart(DateOnly lastLocalDate)
    {
        var monday = MondayOf(lastLocalDate);
        return lastLocalDate >= monday.AddDays(6)
            ? monday                 // data runs through Sunday: that week is complete
            : monday.AddDays(-7);    // partial week in progress: fall back one
    }
}
