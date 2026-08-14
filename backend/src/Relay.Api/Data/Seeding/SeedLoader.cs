using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Data.Entities;

namespace Relay.Api.Data.Seeding;

public enum SeedResult { Applied, AlreadyApplied, SkippedDataPresent }

public sealed record SeedOutcome(SeedResult Result, int AccountRows, int EventRows, string Sha256);

/// <summary>
/// Loads the provided seed.sql without modifying it.
///
/// The file is ~12.6k bare INSERTs carrying explicit ids, so a second run would violate
/// the primary key. The brief says to treat it as production data, so idempotency comes
/// from wrapping the load — a hash-stamped marker row in seed_runs — rather than from
/// rewriting the file with IF NOT EXISTS guards.
/// </summary>
public sealed class SeedLoader(RelayDbContext db, ILogger<SeedLoader> logger)
{
    private const int StatementsPerBatch = 500;

    /// <summary>
    /// Derives the account-local calendar date and Monday week start for one constant-offset
    /// span. Set-based: one statement per (account, DST segment), so ~2 per account rather
    /// than 12.6k row updates. 1900-01-01 was a Monday, which makes the week start
    /// arithmetic independent of the connection's DATEFIRST setting.
    /// </summary>
    private const string FillLocalDatesSql = """
        UPDATE e
           SET occurred_local_date = d.local_date,
               local_week_start    = DATEADD(DAY, -(DATEDIFF(DAY, '19000101', d.local_date) % 7), d.local_date)
          FROM activity_events AS e
         CROSS APPLY (SELECT CAST(DATEADD(MINUTE, @offsetMinutes, e.occurred_at) AS date)) AS d(local_date)
         WHERE e.account_id  = @accountId
           AND e.occurred_at >= @fromUtc
           AND e.occurred_at <  @toUtc;
        """;

    public async Task<SeedOutcome> EnsureSeededAsync(string seedSqlPath, CancellationToken ct = default)
    {
        if (!File.Exists(seedSqlPath))
            throw new FileNotFoundException($"Seed file not found. Expected it at '{seedSqlPath}'.", seedSqlPath);

        var sql = await File.ReadAllTextAsync(seedSqlPath, ct);
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();

        if (await db.SeedRuns.AnyAsync(r => r.Sha256 == sha, ct))
        {
            var existing = await db.SeedRuns.AsNoTracking().FirstAsync(r => r.Sha256 == sha, ct);
            logger.LogInformation("Seed {Sha} already applied at {At}; skipping.", sha[..12], existing.AppliedAtUtc);
            return new SeedOutcome(SeedResult.AlreadyApplied, existing.AccountRows, existing.EventRows, sha);
        }

        // Data from some other source is present. Refuse rather than half-load on top of it.
        if (await db.Accounts.AnyAsync(ct))
        {
            logger.LogWarning("Tables already hold data that did not come from seed {Sha}; skipping load.", sha[..12]);
            return new SeedOutcome(SeedResult.SkippedDataPresent, 0, 0, sha);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await ExecuteSeedStatementsAsync(sql, ct);
        await FillLocalDatesAsync(ct);
        await AssertNoUnbucketedEventsAsync(ct);

        var accountRows = await db.Accounts.CountAsync(ct);
        var eventRows = await db.ActivityEvents.CountAsync(ct);

        db.SeedRuns.Add(new SeedRun
        {
            SourceFile = Path.GetFileName(seedSqlPath),
            Sha256 = sha,
            AccountRows = accountRows,
            EventRows = eventRows,
            AppliedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation("Seeded {Accounts} accounts and {Events} events from {File}.",
            accountRows, eventRows, Path.GetFileName(seedSqlPath));

        return new SeedOutcome(SeedResult.Applied, accountRows, eventRows, sha);
    }

    /// <summary>Executes the file in batches. One round trip per statement would be 12.6k
    /// round trips; the file has no GO separators and every statement is one line.</summary>
    private async Task ExecuteSeedStatementsAsync(string sql, CancellationToken ct)
    {
        var statements = sql
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal))
            .ToList();

        foreach (var batch in statements.Chunk(StatementsPerBatch))
            await db.Database.ExecuteSqlRawAsync(string.Join('\n', batch), ct);
    }

    private async Task FillLocalDatesAsync(CancellationToken ct)
    {
        var accounts = await db.Accounts.AsNoTracking().ToListAsync(ct);

        foreach (var account in accounts)
        {
            var bounds = await db.ActivityEvents
                .Where(e => e.AccountId == account.Id)
                .GroupBy(_ => 1)
                .Select(g => new { Min = g.Min(e => e.OccurredAt), Max = g.Max(e => e.OccurredAt) })
                .FirstOrDefaultAsync(ct);

            // Quiet Harbor Spa (id 20) has no events at all. Not an error — an empty account
            // is a case the dashboard has to render, so it must survive seeding untouched.
            if (bounds is null) continue;

            var zone = TimeZoneInfo.FindSystemTimeZoneById(account.Timezone);
            var segments = TimeZoneSegmenter.Segment(zone, bounds.Min, bounds.Max.AddSeconds(1));

            foreach (var segment in segments)
            {
                await db.Database.ExecuteSqlRawAsync(FillLocalDatesSql,
                [
                    new SqlParameter("@offsetMinutes", segment.OffsetMinutes),
                    new SqlParameter("@accountId", account.Id),
                    new SqlParameter("@fromUtc", segment.FromUtc),
                    new SqlParameter("@toUtc", segment.ToUtcExclusive)
                ], ct);
            }

            logger.LogDebug("Account {Id} ({Zone}): {Segments} offset segment(s).",
                account.Id, account.Timezone, segments.Count);
        }
    }

    /// <summary>The local-date columns are nullable only because seed.sql cannot supply them.
    /// Nothing downstream tolerates a null, so a gap is a hard failure at load time rather
    /// than a silently missing row in an aggregate.</summary>
    private async Task AssertNoUnbucketedEventsAsync(CancellationToken ct)
    {
        var unbucketed = await db.ActivityEvents.CountAsync(e => e.OccurredLocalDate == null, ct);
        if (unbucketed > 0)
            throw new InvalidOperationException(
                $"{unbucketed} event(s) have no local date after seeding. Every event must fall inside " +
                "one of its account's offset segments; a gap means the segmentation is wrong.");
    }
}
