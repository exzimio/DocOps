namespace DocOps.Domain.Common;

/// <summary>Raised when an operation violates a business rule. Mapped to HTTP 409/422 by the API error handler.</summary>
public class DomainException : Exception
{
    public DomainException(string message, string? code = null) : base(message) => Code = code ?? "domain_rule_violation";

    public string Code { get; }
}

/// <summary>Raised when an entity is requested that does not exist, or is invisible to the caller's tenant.</summary>
public sealed class EntityNotFoundException : DomainException
{
    public EntityNotFoundException(string entityName, object key)
        : base($"{entityName} '{key}' was not found.", "entity_not_found")
    {
        EntityName = entityName;
        Key = key;
    }

    public string EntityName { get; }
    public object Key { get; }
}

/// <summary>Raised when the caller is authenticated but lacks the permission required for the operation.</summary>
public sealed class ForbiddenOperationException : DomainException
{
    public ForbiddenOperationException(string message) : base(message, "forbidden") { }
}
