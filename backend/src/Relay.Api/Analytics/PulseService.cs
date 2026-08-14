using Microsoft.EntityFrameworkCore;
using Relay.Api.Contracts;
using Relay.Api.Data;

namespace Relay.Api.Analytics;

public sealed class PulseService(RelayDbContext db, WeeklyTotalsQuery totals)
{
    public static readonly int[] AllowedBaselineWeeks = [4, 8, 12];

    public async Task<IReadOnlyList<AccountListItem>> ListAccountsAsync(CancellationToken ct = default)
        => await db.Accounts
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new AccountListItem(
                a.Id,
                a.Name,
                a.Industry,
                a.Timezone,
                a.Events.Select(e => e.Location).Distinct().Count(),
                a.Events.Any(),
                a.Events.Max(e => e.OccurredLocalDate)))
            .ToListAsync(ct);

    /// <summary>Returns null when the account does not exist. An account that exists but has
    /// no events is NOT null — it is a real state the dashboard has to render, and Quiet
    /// Harbor Spa in the seed is exactly that.</summary>
    public async Task<WeeklyPulseResponse?> GetWeeklyPulseAsync(
        int accountId,
        DateOnly? requestedWeekStart,
        int baselineWeeks,
        string eventType,
        CancellationToken ct = default)
    {
        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new { a.Id, a.Name, a.Industry, a.Timezone })
            .FirstOrDefaultAsync(ct);

        if (account is null) return null;

        var bounds = await db.ActivityEvents
            .Where(e => e.AccountId == accountId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Earliest = g.Min(e => e.OccurredLocalDate),
                Latest = g.Max(e => e.OccurredLocalDate),
                Locations = g.Select(e => e.Location).Distinct().Count()
            })
            .FirstOrDefaultAsync(ct);

        var hasData = bounds is not null && bounds.Latest is not null;

        // "Now" comes from the data, never from the clock — the seed ends 2026-07-27 and the
        // system clock would produce an empty dashboard. When the account itself is empty we
        // still need a week to render, so fall back to the newest date anywhere in the set.
        var anchorDate = bounds?.Latest
                         ?? await db.ActivityEvents.MaxAsync(e => e.OccurredLocalDate, ct);

        var weekStart = requestedWeekStart is { } requested
            ? WeekMath.MondayOf(requested)
            : anchorDate is { } anchor
                ? WeekMath.LastCompleteWeekStart(anchor)
                : WeekMath.LastCompleteWeekStart(DateOnly.FromDateTime(DateTime.UtcNow));

        var windowStart = weekStart.AddDays(-7 * baselineWeeks);

        var accountItem = new AccountListItem(
            account.Id, account.Name, account.Industry, account.Timezone,
            bounds?.Locations ?? 0, hasData, bounds?.Latest);

        var week = new WeekWindow(weekStart, weekStart.AddDays(6));
        var baselineWindow = new BaselineWindow(baselineWeeks, windowStart, weekStart.AddDays(-1));

        if (!hasData)
        {
            return new WeeklyPulseResponse(
                accountItem, week, baselineWindow, eventType,
                Total: null,
                Locations: [],
                DataQuality: new DataQuality(false, 0, null, null));
        }

        var rows = await totals.RunAsync(accountId, weekStart, baselineWeeks, eventType, ct);
        var duplicates = await CountDuplicatesInWindowAsync(accountId, windowStart, weekStart, ct);

        var locations = rows
            .GroupBy(r => r.Location, StringComparer.Ordinal)
            .Select(g =>
            {
                var current = g.FirstOrDefault(r => r.WeekStart == weekStart)?.EventCount ?? 0;
                var baseline = g.Where(r => r.WeekStart < weekStart)
                                .OrderBy(r => r.WeekStart)
                                .Select(r => r.EventCount)
                                .ToList();
                return new LocationPulse(g.Key, ToView(BaselineCalculator.Assess(current, baseline)));
            })
            .OrderBy(l => ActionPriority(l.Metric.Status))
            .ThenByDescending(l => Math.Abs(l.Metric.DeviationScore ?? 0))
            .ThenBy(l => l.Location, StringComparer.Ordinal)
            .ToList();

        // The account roll-up is the same comparison applied to the sum across locations.
        // Summing the gap-filled per-location totals is what makes this correct: a location
        // missing from a week contributes 0 rather than being absent from the sum.
        var byWeek = rows.GroupBy(r => r.WeekStart)
                         .ToDictionary(g => g.Key, g => g.Sum(r => r.EventCount));

        var accountCurrent = byWeek.GetValueOrDefault(weekStart);
        var accountBaseline = byWeek.Where(kv => kv.Key < weekStart)
                                    .OrderBy(kv => kv.Key)
                                    .Select(kv => kv.Value)
                                    .ToList();

        return new WeeklyPulseResponse(
            accountItem, week, baselineWindow, eventType,
            Total: ToView(BaselineCalculator.Assess(accountCurrent, accountBaseline)),
            Locations: locations,
            DataQuality: new DataQuality(true, duplicates, bounds!.Earliest, bounds.Latest));
    }

    /// <summary>
    /// Diagnostic only — the pulse aggregate itself runs through the embedded SQL. Reported
    /// so the deduplication is visible in the payload instead of silently dropping rows.
    /// </summary>
    private async Task<int> CountDuplicatesInWindowAsync(
        int accountId, DateOnly windowStart, DateOnly weekStart, CancellationToken ct)
    {
        var inWindow = db.ActivityEvents.Where(e =>
            e.AccountId == accountId &&
            e.LocalWeekStart >= windowStart &&
            e.LocalWeekStart <= weekStart);

        var raw = await inWindow.CountAsync(ct);
        var distinct = await inWindow
            .Select(e => new { e.Location, e.EventType, e.OccurredAt, e.DurationSeconds, e.Outcome })
            .Distinct()
            .CountAsync(ct);

        return raw - distinct;
    }

    /// <summary>Locations that need a decision come first; suppressed ones sink.</summary>
    private static int ActionPriority(string status) => status switch
    {
        "below" or "above" => 0,
        "normal" => 1,
        "lowVolume" => 2,
        _ => 3
    };

    private static MetricView ToView(BaselineAssessment a) => new(
        a.Current,
        Math.Round(a.BaselineMedian, 2),
        Math.Round(a.TypicalLow, 2),
        Math.Round(a.TypicalHigh, 2),
        a.DeltaPct is { } d ? Math.Round(d, 4) : null,
        a.DeviationScore is { } s ? Math.Round(s, 3) : null,
        a.BaselineWeeksUsed,
        Camel(a.Status.ToString()));

    private static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];
}
