using Microsoft.Extensions.Configuration;

namespace DocOps.Infrastructure.Persistence.Seed;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>
    /// The same name the Docker Compose stack and the <c>.env</c> file use, so one variable sets the
    /// demo password wherever the seeder runs.
    /// </summary>
    public const string PasswordVariable = "DOCOPS_SEED_PASSWORD";

    /// <summary>When true, demo organisations, users and business data are created on start-up if missing.</summary>
    public bool Enabled { get; set; }

    /// <summary>Applies pending EF Core migrations on start-up. Intended for local development only.</summary>
    public bool ApplyMigrations { get; set; }

    /// <summary>
    /// Password assigned to every demo account. <c>DOCOPS_SEED_PASSWORD</c>, when set, wins over
    /// <c>Seed:DefaultPassword</c>; the default exists only so that a fresh clone runs.
    /// </summary>
    public string DefaultPassword { get; set; } = "Demo!Pass123";

    public static void Bind(IConfiguration configuration, SeedOptions options)
    {
        configuration.GetSection(SectionName).Bind(options);

        if (configuration[PasswordVariable] is { Length: > 0 } password)
        {
            options.DefaultPassword = password;
        }
    }
}
