using DocOps.Domain.Common;

namespace DocOps.Domain.Tenancy;

/// <summary>An isolated customer organisation. Every business entity in the platform is scoped to exactly one tenant.</summary>
public sealed class Tenant : AuditableEntity
{
    public required string Name { get; set; }

    /// <summary>URL/login-safe unique key, e.g. "asteria-group".</summary>
    public required string Slug { get; set; }

    public string? LegalName { get; set; }
    public string? TaxIdentifier { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>BCP-47 culture used as the default UI language for members of this tenant.</summary>
    public string DefaultCulture { get; set; } = "pt-PT";

    /// <summary>IANA time zone used when rendering deadlines and SLA calculations for this tenant.</summary>
    public string TimeZoneId { get; set; } = "Europe/Lisbon";

    public ICollection<Department> Departments { get; set; } = [];
}
