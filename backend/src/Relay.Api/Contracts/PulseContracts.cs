namespace Relay.Api.Contracts;

public sealed record AccountListItem(
    int Id,
    string Name,
    string Industry,
    string Timezone,
    int LocationCount,
    bool HasData,
    DateOnly? LatestLocalDate);

public sealed record WeekWindow(DateOnly Start, DateOnly End);

public sealed record BaselineWindow(int Weeks, DateOnly Start, DateOnly End);

/// <summary>One comparison of "this week" against "normal for us".</summary>
public sealed record MetricView(
    int Current,
    double BaselineMedian,
    double TypicalLow,
    double TypicalHigh,
    double? DeltaPct,
    double? DeviationScore,
    int BaselineWeeksUsed,
    string Status);

public sealed record LocationPulse(string Location, MetricView Metric);

/// <summary>Surfaced rather than hidden: the API says out loud how many rows it dropped and
/// whether the account has any data at all, so a reader can tell an empty account from a
/// broken query.</summary>
public sealed record DataQuality(
    bool HasData,
    int DuplicateEventsExcluded,
    DateOnly? EarliestLocalDate,
    DateOnly? LatestLocalDate);

public sealed record WeeklyPulseResponse(
    AccountListItem Account,
    WeekWindow Week,
    BaselineWindow Baseline,
    string EventType,
    MetricView? Total,
    IReadOnlyList<LocationPulse> Locations,
    DataQuality DataQuality);
