using DocOps.Application.Common;
using DocOps.Domain.Documents;

namespace DocOps.Application.Documents;

/// <summary>
/// Document repository operations. Every method authorises the caller against the specific document
/// before acting, and writes the corresponding audit record.
/// </summary>
public interface IDocumentService
{
    /// <summary>Paged, filtered list restricted to the documents the caller is allowed to see.</summary>
    Task<PagedResult<DocumentListItemDto>> SearchAsync(
        DocumentSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>Full detail, including versions, metadata and the caller's effective access. Audited as a view.</summary>
    Task<DocumentDetailDto> GetAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task<DocumentDetailDto> CreateAsync(
        CreateDocumentRequest request, DocumentUpload upload, CancellationToken cancellationToken = default);

    Task<DocumentDetailDto> UpdateAsync(
        Guid documentId, UpdateDocumentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Adds a new version. Previous versions are never overwritten.</summary>
    Task<DocumentDetailDto> AddVersionAsync(
        Guid documentId, DocumentUpload upload, string? comment, CancellationToken cancellationToken = default);

    Task<DocumentDetailDto> ChangeStatusAsync(
        Guid documentId, ChangeDocumentStatusRequest request, CancellationToken cancellationToken = default);

    Task<DocumentDetailDto> ArchiveAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task<DocumentDetailDto> RestoreAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Soft delete: the row and its versions remain for audit and recovery.</summary>
    Task DeleteAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Opens the stored content of a version (the current one when no number is given).</summary>
    Task<DocumentDownload> DownloadAsync(
        Guid documentId, int? versionNumber, CancellationToken cancellationToken = default);

    Task<PagedResult<DocumentAuditEntryDto>> GetAuditTrailAsync(
        Guid documentId, PageRequest paging, CancellationToken cancellationToken = default);
}

public interface IDocumentPermissionService
{
    Task<IReadOnlyList<DocumentPermissionDto>> ListAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task<DocumentPermissionDto> GrantAsync(
        Guid documentId, GrantDocumentPermissionRequest request, CancellationToken cancellationToken = default);

    Task RevokeAsync(Guid documentId, Guid permissionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Effective access of a user on a document, with the reasoning. Callers may inspect another
    /// user's access only when they hold document administration rights.
    /// </summary>
    Task<EffectiveAccessDto> GetEffectiveAccessAsync(
        Guid documentId, Guid? userId = null, CancellationToken cancellationToken = default);
}

/// <summary>Reference data used by the document forms and filters.</summary>
public interface IDocumentCatalogue
{
    Task<IReadOnlyList<DocumentTypeDto>> GetTypesAsync(
        bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TagDto>> GetTagsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RetentionPolicyDto>> GetRetentionPoliciesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Maps the flags enum to and from the string list the API exposes.</summary>
public static class AccessLevelNames
{
    private static readonly (DocumentAccessLevel Level, string Name)[] Map =
    [
        (DocumentAccessLevel.View, "view"),
        (DocumentAccessLevel.Download, "download"),
        (DocumentAccessLevel.Edit, "edit"),
        (DocumentAccessLevel.Delete, "delete"),
        (DocumentAccessLevel.Share, "share"),
        (DocumentAccessLevel.Approve, "approve"),
        (DocumentAccessLevel.Administer, "administer")
    ];

    public static IReadOnlyList<string> ToNames(DocumentAccessLevel level) =>
        [.. Map.Where(entry => (level & entry.Level) == entry.Level).Select(entry => entry.Name)];

    public static DocumentAccessLevel FromNames(IEnumerable<string> names)
    {
        DocumentAccessLevel level = DocumentAccessLevel.None;

        foreach (string name in names)
        {
            foreach ((DocumentAccessLevel candidate, string candidateName) in Map)
            {
                if (string.Equals(candidateName, name, StringComparison.OrdinalIgnoreCase))
                {
                    level |= candidate;
                }
            }
        }

        return level;
    }

    public static bool IsKnown(string name) =>
        Map.Any(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
}
