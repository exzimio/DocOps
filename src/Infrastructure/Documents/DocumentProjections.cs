using System.Linq.Expressions;
using DocOps.Application.Documents;
using DocOps.Domain.Documents;
using DocOps.Infrastructure.Persistence;

namespace DocOps.Infrastructure.Documents;

/// <summary>
/// Projections from entities to DTOs. Everything is expressed as a single translatable expression so
/// a list of documents costs one query — no per-row lookups for owner names, tags or file sizes.
/// </summary>
internal static class DocumentProjections
{
    public static Expression<Func<Document, DocumentListItemDto>> ToListItem(DocOpsDbContext db) =>
        document => new DocumentListItemDto
        {
            Id = document.Id,
            Reference = document.Reference,
            Title = document.Title,
            TypeCode = document.DocumentType!.Code,
            TypeName = document.DocumentType.Name,
            TypeIcon = document.DocumentType.Icon,
            Status = document.Status,
            Classification = document.Classification,
            OwnerName = db.Users
                .Where(u => u.Id == document.OwnerUserId)
                .Select(u => u.FirstName + " " + u.LastName)
                .FirstOrDefault() ?? "-",
            DepartmentName = document.Department != null ? document.Department.Name : null,
            DepartmentCode = document.Department != null ? document.Department.Code : null,
            CurrentVersionNumber = document.CurrentVersionNumber,
            FileSizeBytes = document.Versions
                .Where(v => v.IsCurrent)
                .Select(v => (long?)v.FileSizeBytes)
                .FirstOrDefault(),
            CreatedAtUtc = document.CreatedAtUtc,
            ModifiedAtUtc = document.ModifiedAtUtc,
            RetentionDueAtUtc = document.RetentionDueAtUtc,
            Tags = document.Tags
                .Select(t => new TagDto(t.TagId, t.Tag!.Name, t.Tag.Colour))
                .ToList()
        };

    public static Expression<Func<DocumentVersion, DocumentVersionDto>> ToVersion(DocOpsDbContext db) =>
        version => new DocumentVersionDto(
            version.Id,
            version.VersionNumber,
            version.FileName,
            version.ContentType,
            version.FileSizeBytes,
            version.ContentHash,
            version.Comment,
            version.CreatedAtUtc,
            db.Users
                .Where(u => u.Id == version.CreatedByUserId)
                .Select(u => u.FirstName + " " + u.LastName)
                .FirstOrDefault() ?? "-",
            version.IsCurrent);

    /// <summary>
    /// Mapped in memory rather than in SQL: the access level is a flags enum that has to be expanded
    /// into names, which no database can do. Permission lists are per document and short.
    /// </summary>
    public static DocumentPermissionDto ToPermission(
        DocumentPermission permission, IReadOnlyDictionary<Guid, string> userNames) =>
        new(
            permission.Id,
            permission.SubjectType,
            permission.SubjectId,
            permission.RoleName,
            permission.SubjectDisplayName,
            AccessLevelNames.ToNames(permission.AccessLevel),
            permission.GrantedByUserId is { } grantedBy && userNames.TryGetValue(grantedBy, out string? name)
                ? name
                : null,
            permission.GrantedAtUtc,
            permission.ExpiresAtUtc);

    public static DocumentMetadataValueDto ToMetadataValue(DocumentMetadataField field, DocumentMetadataValue? value) =>
        new(
            field.Id,
            field.Code,
            field.Label,
            field.FieldType,
            field.IsRequired,
            FormatValue(field.FieldType, value),
            value?.AiConfidence);

    public static EffectiveAccessDto ToEffectiveAccess(EffectiveAccess access) =>
        new(
            AccessLevelNames.ToNames(access.Level),
            [.. access.Reasons.Select(r => new AccessReasonDto(
                r.Source.ToString(), AccessLevelNames.ToNames(r.Granted), r.Description))],
            [.. access.Restrictions.Select(r => new AccessRestrictionDto(
                AccessLevelNames.ToNames(r.Removed), r.Description))]);

    private static string? FormatValue(MetadataFieldType fieldType, DocumentMetadataValue? value)
    {
        if (value is null)
        {
            return null;
        }

        return fieldType switch
        {
            MetadataFieldType.Number or MetadataFieldType.Currency =>
                value.NumberValue?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            MetadataFieldType.Date => value.DateValue?.ToString("O"),
            MetadataFieldType.Boolean => value.BooleanValue?.ToString(),
            _ => value.TextValue
        };
    }
}
