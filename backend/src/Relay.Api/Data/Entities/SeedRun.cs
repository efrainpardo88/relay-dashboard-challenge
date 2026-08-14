namespace Relay.Api.Data.Entities;

/// <summary>Marker row proving the seed already ran. The provided seed.sql is ~12.6k bare
/// INSERTs with explicit ids, so running it twice violates the primary key. The brief says
/// to treat that file as production data, so idempotency is added by wrapping the load and
/// recording it here rather than by editing the file.</summary>
public sealed class SeedRun
{
    public int Id { get; set; }
    public string SourceFile { get; set; } = string.Empty;

    /// <summary>SHA-256 of the seed file, so a changed dataset is detected rather than
    /// silently skipped.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public int AccountRows { get; set; }
    public int EventRows { get; set; }
    public DateTime AppliedAtUtc { get; set; }
}
