using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Relay.Api.Analytics;
using Relay.Api.Data;
using Relay.Api.Data.Seeding;

namespace Relay.Api.Tests;

/// <summary>
/// Brings up a migrated, seeded database for the integration tests. Runs the real
/// SeedLoader rather than a test-only fixture, so these tests exercise the same load path
/// the application uses — and, because the loader is idempotent, running them against an
/// already-seeded database is a no-op rather than a failure.
///
/// Requires SQL Server: `docker compose up -d --wait` from the repository root.
/// </summary>
public sealed class SeededDatabaseFixture : IAsyncLifetime
{
    private const string DefaultConnection =
        "Server=localhost,11433;Database=Relay;User Id=sa;Password=Relay!Local2026;TrustServerCertificate=True;Encrypt=False";

    public RelayDbContext Db { get; private set; } = null!;
    public PulseService Pulse { get; private set; } = null!;
    public SeedOutcome SeedOutcome { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("RELAY_TEST_CONNECTION") ?? DefaultConnection;

        Db = new RelayDbContext(new DbContextOptionsBuilder<RelayDbContext>()
            .UseSqlServer(connectionString)
            .Options);

        try
        {
            await Db.Database.MigrateAsync();
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException(
                "The integration tests need SQL Server. From the repository root run:" +
                Environment.NewLine + "  docker compose up -d --wait" +
                Environment.NewLine + "Override the connection with RELAY_TEST_CONNECTION if you host it elsewhere.",
                ex);
        }

        SeedOutcome = await new SeedLoader(Db, NullLogger<SeedLoader>.Instance).EnsureSeededAsync(FindSeedFile());
        Pulse = new PulseService(Db, new WeeklyTotalsQuery(Db));
    }

    public async Task DisposeAsync() => await Db.DisposeAsync();

    private static string FindSeedFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "seed.sql");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"seed.sql not found above '{AppContext.BaseDirectory}'.");
    }
}

[CollectionDefinition(Name)]
public sealed class SeededDatabaseCollection : ICollectionFixture<SeededDatabaseFixture>
{
    public const string Name = "seeded-database";
}
