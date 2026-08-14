using Microsoft.EntityFrameworkCore;
using Relay.Api.Analytics;
using Relay.Api.Data;
using Relay.Api.Data.Seeding;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Relay")
    ?? throw new InvalidOperationException("Missing connection string 'Relay'.");

builder.Services.AddDbContext<RelayDbContext>(o => o.UseSqlServer(connectionString));
builder.Services.AddScoped<SeedLoader>();
builder.Services.AddScoped<WeeklyTotalsQuery>();
builder.Services.AddScoped<PulseService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

const string DevCors = "angular-dev";
builder.Services.AddCors(o => o.AddPolicy(DevCors, p => p
    .WithOrigins("http://localhost:4200")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// Fail fast on a moved or renamed .sql resource, with the available names in the message,
// rather than at the first request (PLAN.md Amendment 4).
WeeklyTotalsQuery.EnsureSqlLoaded();

app.UseCors(DevCors);
app.UseSwagger();
app.UseSwaggerUI();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RelayDbContext>();
    await db.Database.MigrateAsync();

    var seedPath = ResolveSeedPath(builder.Configuration["Seed:FilePath"], app.Environment.ContentRootPath);
    var loader = scope.ServiceProvider.GetRequiredService<SeedLoader>();
    var outcome = await loader.EnsureSeededAsync(seedPath);

    app.Logger.LogInformation("Seed {Result}: {Accounts} accounts, {Events} events.",
        outcome.Result, outcome.AccountRows, outcome.EventRows);
}

app.MapGet("/api/accounts", async (PulseService pulse, CancellationToken ct)
    => Results.Ok(await pulse.ListAccountsAsync(ct)))
   .WithName("ListAccounts")
   .WithSummary("Accounts available to the dashboard, with whether each has any activity.");

app.MapGet("/api/accounts/{accountId:int}/weekly-pulse", async (
    int accountId,
    DateOnly? weekStart,
    int? baselineWeeks,
    string? eventType,
    PulseService pulse,
    CancellationToken ct) =>
{
    var weeks = baselineWeeks ?? 8;
    if (!PulseService.AllowedBaselineWeeks.Contains(weeks))
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["baselineWeeks"] = [$"Must be one of {string.Join(", ", PulseService.AllowedBaselineWeeks)}."]
        });

    var type = eventType ?? "all";
    if (!WeeklyTotalsQuery.AllowedEventTypes.Contains(type))
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["eventType"] = [$"Must be one of {string.Join(", ", WeeklyTotalsQuery.AllowedEventTypes)}."]
        });

    var result = await pulse.GetWeeklyPulseAsync(accountId, weekStart, weeks, type, ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
})
   .WithName("GetWeeklyPulse")
   .WithSummary("Last complete Monday-Sunday week against the account's own recent norm, per location.");

app.Run();

/// <summary>seed.sql lives at the repository root, which is several levels above whichever
/// directory the app was launched from. Walk up and find it rather than hard-coding a
/// relative path that only works from one working directory.</summary>
static string ResolveSeedPath(string? configured, string contentRoot)
{
    if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);

    var dir = new DirectoryInfo(contentRoot);
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "seed.sql");
        if (File.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }

    throw new FileNotFoundException(
        $"Could not find seed.sql by walking up from '{contentRoot}'. Set ConnectionStrings/Seed:FilePath explicitly.");
}

public partial class Program;
