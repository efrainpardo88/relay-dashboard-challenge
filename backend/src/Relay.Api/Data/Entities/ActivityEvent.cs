namespace Relay.Api.Data.Entities;

/// <summary>Inbound customer activity. The first seven columns mirror the seed file
/// one-for-one; the last two are ours.</summary>
public sealed class ActivityEvent
{
    public int Id { get; set; }
    public int AccountId { get; set; }

    /// <summary>Site/branch. Only unique WITHIN an account — 'Site A' exists in 19 of
    /// the 20 seed accounts. Never group or filter on it without account_id.</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>'call_received' | 'lead_created' | 'appointment_set'.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>UTC, as delivered.</summary>
    public DateTime OccurredAt { get; set; }

    public int? DurationSeconds { get; set; }
    public string? Outcome { get; set; }

    /// <summary>Calendar date of <see cref="OccurredAt"/> in the ACCOUNT's local time.
    /// Derived at load time rather than at query time: SQL Server's AT TIME ZONE rejects
    /// IANA ids (verified — PLAN.md Amendment 3), and a stored column keeps the day
    /// bucket sargable. Nullable only because seed.sql is executed verbatim and does not
    /// supply it; the loader fills it in the same transaction and asserts no nulls
    /// remain, and a production ingest path would compute it at write time and make the
    /// column NOT NULL.</summary>
    public DateOnly? OccurredLocalDate { get; set; }

    /// <summary>Monday of the local week containing <see cref="OccurredLocalDate"/>.</summary>
    public DateOnly? LocalWeekStart { get; set; }

    public Account? Account { get; set; }
}
