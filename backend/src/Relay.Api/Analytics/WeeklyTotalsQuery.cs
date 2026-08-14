using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Data;

namespace Relay.Api.Analytics;

/// <summary>One row of the SQL aggregate. A flat DTO, deliberately not an entity, so EF
/// change tracking never touches it.</summary>
public sealed class WeeklyLocationTotal
{
    public string Location { get; set; } = string.Empty;
    public DateOnly WeekStart { get; set; }
    public int EventCount { get; set; }
}

/// <summary>
/// Runs the weekly-totals aggregate. The SQL lives in its own embedded .sql file rather
/// than in a string literal (PLAN.md Amendment 4) so it can be executed straight against
/// the database to verify its numbers — which matters, because correct aggregates are the
/// thing being judged.
/// </summary>
public sealed class WeeklyTotalsQuery(RelayDbContext db)
{
    public static readonly string[] AllowedEventTypes =
        ["all", "call_received", "lead_created", "appointment_set"];

    private static readonly string Sql = EmbeddedSql.Load("Analytics.Sql.WeeklyTotalsByLocation.sql");

    /// <summary>Called at startup so a missing or renamed resource fails immediately with a
    /// useful message rather than on the first request.</summary>
    public static void EnsureSqlLoaded() => _ = Sql.Length;

    public Task<List<WeeklyLocationTotal>> RunAsync(
        int accountId,
        DateOnly currentWeekStart,
        int baselineWeeks,
        string eventType,
        CancellationToken ct = default)
    {
        if (!AllowedEventTypes.Contains(eventType))
            throw new ArgumentException($"Unsupported event type '{eventType}'.", nameof(eventType));

        // Computed here rather than DECLAREd in the .sql so that file stays a single
        // statement — EF composes over raw SQL, and a multi-statement batch would break it.
        var windowStart = currentWeekStart.AddDays(-7 * baselineWeeks);

        return db.Database.SqlQueryRaw<WeeklyLocationTotal>(Sql,
            new SqlParameter("@accountId", SqlDbType.Int) { Value = accountId },
            new SqlParameter("@currentWeekStart", SqlDbType.Date) { Value = currentWeekStart.ToDateTime(TimeOnly.MinValue) },
            new SqlParameter("@windowStart", SqlDbType.Date) { Value = windowStart.ToDateTime(TimeOnly.MinValue) },
            new SqlParameter("@baselineWeeks", SqlDbType.Int) { Value = baselineWeeks },
            new SqlParameter("@eventType", SqlDbType.NVarChar, 40) { Value = eventType }
        ).ToListAsync(ct);
    }
}

internal static class EmbeddedSql
{
    public static string Load(string relativeName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"{assembly.GetName().Name}.{relativeName}";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            var available = string.Join(Environment.NewLine + "  ", assembly.GetManifestResourceNames());
            throw new InvalidOperationException(
                $"Embedded SQL resource '{resourceName}' was not found. Resource names are derived from " +
                $"the file path, so a moved or renamed .sql breaks this silently at runtime.{Environment.NewLine}" +
                $"Available resources:{Environment.NewLine}  {available}");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
