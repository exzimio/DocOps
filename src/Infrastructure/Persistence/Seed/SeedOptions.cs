namespace DocOps.Infrastructure.Persistence.Seed;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>When true, demo organisations, users and business data are created on start-up if missing.</summary>
    public bool Enabled { get; set; }

    /// <summary>Applies pending EF Core migrations on start-up. Intended for local development only.</summary>
    public bool ApplyMigrations { get; set; }

    /// <summary>
    /// Password assigned to every demo account. Override through configuration or the
    /// DOCOPS_SEED_PASSWORD environment variable; the default exists only so that a fresh clone runs.
    /// </summary>
    public string DefaultPassword { get; set; } = "Demo!Pass123";
}
