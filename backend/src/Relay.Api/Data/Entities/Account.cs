namespace Relay.Api.Data.Entities;

/// <summary>One Relay customer. Table and column names match the provided seed.sql
/// exactly so that file can be executed verbatim — see SeedLoader.</summary>
public sealed class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;

    /// <summary>IANA zone, e.g. "America/Chicago". Not a Windows zone id.</summary>
    public string Timezone { get; set; } = string.Empty;

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public ICollection<ActivityEvent> Events { get; set; } = new List<ActivityEvent>();
}
