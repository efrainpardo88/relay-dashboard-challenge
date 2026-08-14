using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Relay.Api.Data;

/// <summary>Design-time only. Lets `dotnet ef migrations add` work without booting the app
/// or reaching a live database.</summary>
public sealed class RelayDbContextFactory : IDesignTimeDbContextFactory<RelayDbContext>
{
    public RelayDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("RELAY_CONNECTION")
            ?? "Server=localhost,11433;Database=Relay;User Id=sa;Password=Relay!Local2026;TrustServerCertificate=True;Encrypt=False";

        var options = new DbContextOptionsBuilder<RelayDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new RelayDbContext(options);
    }
}
