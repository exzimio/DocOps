using DocOps.Domain.Common;

namespace DocOps.Domain.Compliance;

/// <summary>Event that starts the retention clock.</summary>
public enum RetentionTrigger
{
    OnCreation = 0,
    OnApproval = 1,
    OnArchive = 2
}

/// <summary>
/// How long a class of document must be kept before it may be disposed of. Policies are per tenant,
/// because retention obligations follow the organisation's jurisdiction and sector.
/// </summary>
public sealed class RetentionPolicy : TenantEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Legal or contractual basis, shown in the compliance centre so the rule is explainable.</summary>
    public string? LegalBasis { get; set; }

    public int RetentionMonths { get; set; }
    public RetentionTrigger Trigger { get; set; } = RetentionTrigger.OnArchive;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When retention expires for an event that happened at <paramref name="triggerOccurredAtUtc"/>.
    /// </summary>
    public DateTimeOffset CalculateDueDate(DateTimeOffset triggerOccurredAtUtc) =>
        triggerOccurredAtUtc.AddMonths(RetentionMonths);
}
