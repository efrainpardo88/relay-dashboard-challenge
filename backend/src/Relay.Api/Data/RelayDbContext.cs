using Microsoft.EntityFrameworkCore;
using Relay.Api.Data.Entities;

namespace Relay.Api.Data;

public sealed class RelayDbContext(DbContextOptions<RelayDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<SeedRun> SeedRuns => Set<SeedRun>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        // Snake_case table and column names are not a style choice: the provided seed.sql
        // targets these exact names, and executing it verbatim is what lets us treat the
        // dataset as production data instead of rewriting it.
        model.Entity<Account>(e =>
        {
            e.ToTable("accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
            e.Property(x => x.Industry).HasColumnName("industry").HasMaxLength(60).IsRequired();
            e.Property(x => x.Timezone).HasColumnName("timezone").HasMaxLength(60).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(0)");
        });

        model.Entity<ActivityEvent>(e =>
        {
            e.ToTable("activity_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.Location).HasColumnName("location").HasMaxLength(80).IsRequired();
            e.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(40).IsRequired();
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasColumnType("datetime2(0)");
            e.Property(x => x.DurationSeconds).HasColumnName("duration_seconds");
            e.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(40);
            e.Property(x => x.OccurredLocalDate).HasColumnName("occurred_local_date").HasColumnType("date");
            e.Property(x => x.LocalWeekStart).HasColumnName("local_week_start").HasColumnType("date");

            e.HasOne(x => x.Account)
             .WithMany(a => a.Events)
             .HasForeignKey(x => x.AccountId)
             .OnDelete(DeleteBehavior.Cascade);

            // The pulse query filters on account + local week and groups by location.
            e.HasIndex(x => new { x.AccountId, x.LocalWeekStart })
             .HasDatabaseName("IX_activity_events_account_local_week")
             .IncludeProperties(x => new { x.Location, x.EventType });

            // Deduplication groups on the whole value tuple within an account.
            e.HasIndex(x => new { x.AccountId, x.Location, x.EventType, x.OccurredAt })
             .HasDatabaseName("IX_activity_events_dedup");
        });

        model.Entity<SeedRun>(e =>
        {
            e.ToTable("seed_runs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.SourceFile).HasColumnName("source_file").HasMaxLength(260).IsRequired();
            e.Property(x => x.Sha256).HasColumnName("sha256").HasMaxLength(64).IsRequired();
            e.Property(x => x.AccountRows).HasColumnName("account_rows");
            e.Property(x => x.EventRows).HasColumnName("event_rows");
            e.Property(x => x.AppliedAtUtc).HasColumnName("applied_at_utc").HasColumnType("datetime2(0)");
        });
    }
}
