namespace DocOps.Domain.Common;

/// <summary>Identifier-carrying root of every persisted entity. Ids are UUIDv7 so clustered index inserts stay sequential.</summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}

public interface IHasTimestamps
{
    DateTimeOffset CreatedAtUtc { get; set; }
    Guid? CreatedByUserId { get; set; }
    DateTimeOffset? ModifiedAtUtc { get; set; }
    Guid? ModifiedByUserId { get; set; }
}

public interface ITenantScoped
{
    Guid TenantId { get; set; }
}

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAtUtc { get; set; }
    Guid? DeletedByUserId { get; set; }
}

public interface IHasConcurrencyToken
{
    byte[] RowVersion { get; set; }
}

public abstract class AuditableEntity : BaseEntity, IHasTimestamps, IHasConcurrencyToken
{
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ModifiedAtUtc { get; set; }
    public Guid? ModifiedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public abstract class TenantEntity : AuditableEntity, ITenantScoped
{
    public Guid TenantId { get; set; }
}
