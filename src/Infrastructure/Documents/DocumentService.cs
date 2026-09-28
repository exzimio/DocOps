using System.Text;
using DocOps.Application.Abstractions;
using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Domain.Auditing;
using DocOps.Domain.Common;
using DocOps.Domain.Compliance;
using DocOps.Domain.Documents;
using DocOps.Domain.Intelligence;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DocOps.Infrastructure.Observability;

namespace DocOps.Infrastructure.Documents;

public sealed class DocumentService(
    DocOpsDbContext dbContext,
    DocumentAccessResolver accessResolver,
    DocumentReferenceGenerator referenceGenerator,
    DocumentMetadataWriter metadataWriter,
    IFileStorage fileStorage,
    IOptions<FileStorageOptions> storageOptions,
    IAuditService auditService,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    DocOpsMetrics metrics,
    ILogger<DocumentService> logger) : IDocumentService
{
    private const int MaxExtractedTextLength = 100_000;
    private readonly FileStorageOptions _storageOptions = storageOptions.Value;

    // -----------------------------------------------------------------------------------------
    // Read
    // -----------------------------------------------------------------------------------------

    public async Task<PagedResult<DocumentListItemDto>> SearchAsync(
        DocumentSearchRequest request, CancellationToken cancellationToken = default)
    {
        AccessSubject subject = await accessResolver.GetSubjectAsync(cancellationToken);

        IQueryable<Document> query = dbContext.Documents
            .Where(DocumentAccessResolver.BuildVisibilityPredicate(subject, clock.UtcNow));

        query = DocumentFilters.ApplyTextContains(query, request.Query);
        query = DocumentFilters.ApplyFacets(dbContext, query, request, subject.UserId);

        long total = await query.LongCountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult<DocumentListItemDto>.Empty(request.Paging.NormalizedPage, request.Paging.NormalizedPageSize);
        }

        List<DocumentListItemDto> items = await DocumentFilters.ApplySort(query, request)
            .Skip(request.Paging.Skip)
            .Take(request.Paging.NormalizedPageSize)
            .Select(DocumentProjections.ToListItem(dbContext))
            .ToListAsync(cancellationToken);

        return new PagedResult<DocumentListItemDto>(
            items, request.Paging.NormalizedPage, request.Paging.NormalizedPageSize, total);
    }

    public async Task<DocumentDetailDto> GetAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        Document document = await LoadAsync(documentId, cancellationToken);
        EffectiveAccess access = await accessResolver.EnsureAsync(
            document, DocumentAccessLevel.View, clock.UtcNow, cancellationToken);

        await auditService.WriteAsync(
            DocumentAudit(document, AuditActions.DocumentViewed, "Documento consultado"), cancellationToken);

        return await BuildDetailAsync(document, access, cancellationToken);
    }

    public async Task<DocumentDownload> DownloadAsync(
        Guid documentId, int? versionNumber, CancellationToken cancellationToken = default)
    {
        Document document = await LoadAsync(documentId, cancellationToken);
        await accessResolver.EnsureAsync(document, DocumentAccessLevel.Download, clock.UtcNow, cancellationToken);

        DocumentVersion version = versionNumber is { } number
            ? document.Versions.FirstOrDefault(v => v.VersionNumber == number)
                ?? throw new EntityNotFoundException(nameof(DocumentVersion), number)
            : document.Versions.FirstOrDefault(v => v.IsCurrent)
                ?? throw new DomainException("O documento não tem qualquer versão disponível.", "no_current_version");

        Stream content = await fileStorage.OpenReadAsync(version.StorageKey, cancellationToken);

        await auditService.WriteAsync(
            DocumentAudit(
                document,
                AuditActions.DocumentDownloaded,
                $"Versão {version.VersionNumber} descarregada",
                metadata: new Dictionary<string, object?>
                {
                    ["versionNumber"] = version.VersionNumber,
                    ["fileName"] = version.FileName,
                    ["contentHash"] = version.ContentHash
                }),
            cancellationToken);

        return new DocumentDownload(content, version.FileName, version.ContentType, version.FileSizeBytes, version.ContentHash);
    }

    public async Task<DocumentTextDto> GetTextAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        const int MaxLines = 2_000;

        Document document = await LoadAsync(documentId, cancellationToken);

        // Reading the text is reading the content: the same right as downloading it.
        await accessResolver.EnsureAsync(document, DocumentAccessLevel.Download, clock.UtcNow, cancellationToken);

        DocumentVersion version = document.Versions.Single(v => v.IsCurrent);
        DocumentText text = new(version.ExtractedText);

        await auditService.WriteAsync(
            DocumentAudit(document, AuditActions.DocumentPreviewed, $"Conteúdo da versão {version.VersionNumber} pré-visualizado"),
            cancellationToken);

        return new DocumentTextDto(
            version.VersionNumber,
            version.FileName,
            version.ContentType,
            !text.IsEmpty,
            text.Lines.Count > MaxLines,
            [.. text.Lines.Take(MaxLines).Select(l => new DocumentTextLineDto(l.Number, l.Page, l.Section, l.Text))]);
    }

    public async Task<PagedResult<DocumentAuditEntryDto>> GetAuditTrailAsync(
        Guid documentId, PageRequest paging, CancellationToken cancellationToken = default)
    {
        Document document = await LoadAsync(documentId, cancellationToken);
        await accessResolver.EnsureAsync(document, DocumentAccessLevel.View, clock.UtcNow, cancellationToken);

        IQueryable<AuditEvent> query = dbContext.AuditEvents
            .Where(e => e.EntityType == AuditEntityTypes.Document && e.EntityId == documentId);

        long total = await query.LongCountAsync(cancellationToken);

        List<DocumentAuditEntryDto> items = await query
            .OrderByDescending(e => e.OccurredAtUtc)
            .Skip(paging.Skip)
            .Take(paging.NormalizedPageSize)
            .Select(e => new DocumentAuditEntryDto(
                e.Id,
                e.OccurredAtUtc,
                e.Action,
                e.ActorDisplayName,
                e.Summary,
                e.Severity.ToString(),
                e.CorrelationId,
                e.BeforeJson,
                e.AfterJson))
            .ToListAsync(cancellationToken);

        return new PagedResult<DocumentAuditEntryDto>(items, paging.NormalizedPage, paging.NormalizedPageSize, total);
    }

    // -----------------------------------------------------------------------------------------
    // Write
    // -----------------------------------------------------------------------------------------

    public async Task<DocumentDetailDto> CreateAsync(
        CreateDocumentRequest request, DocumentUpload upload, CancellationToken cancellationToken = default)
    {
        ValidateUpload(upload);

        Guid tenantId = tenantContext.TenantId;
        Guid ownerId = currentUser.UserId
            ?? throw new ForbiddenOperationException("É necessário um utilizador autenticado para criar documentos.");

        DocumentType type = await dbContext.DocumentTypes
            .Include(t => t.MetadataFields)
            .FirstOrDefaultAsync(t => t.Id == request.DocumentTypeId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(DocumentType), request.DocumentTypeId);

        if (!type.IsActive)
        {
            throw new DomainException($"O tipo de documento '{type.Name}' está inativo.", "document_type_inactive");
        }

        await EnsureDepartmentExistsAsync(request.DepartmentId, cancellationToken);

        Document document = new()
        {
            TenantId = tenantId,
            Title = request.Title.Trim(),
            Reference = await referenceGenerator.NextAsync(tenantId, type.ReferencePrefix, cancellationToken),
            Summary = request.Description?.Trim(),
            DocumentTypeId = type.Id,
            DepartmentId = request.DepartmentId,
            BusinessEntity = request.BusinessEntity?.Trim(),
            Classification = request.Classification ?? type.DefaultClassification,
            OwnerUserId = ownerId,
            Status = DocumentStatus.Draft,
            RetentionPolicyId = type.DefaultRetentionPolicyId
        };

        dbContext.Documents.Add(document);

        DocumentVersion version = await StoreVersionAsync(
            document, upload, versionNumber: 1, comment: "Versão inicial", cancellationToken);
        document.CurrentVersionNumber = version.VersionNumber;
        document.CurrentVersionId = version.Id;

        metadataWriter.Apply(document, type, request.Metadata);
        await ApplyTagsAsync(document, request.Tags, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        metrics.DocumentCreated();

        logger.LogInformation(
            "Created document {Reference} ({DocumentId}) of type {TypeCode}", document.Reference, document.Id, type.Code);

        await auditService.WriteAsync(
            DocumentAudit(
                document,
                AuditActions.DocumentCreated,
                $"Documento criado a partir de '{upload.FileName}'",
                after: new
                {
                    document.Reference,
                    document.Title,
                    Type = type.Code,
                    Status = document.Status.ToString(),
                    Classification = document.Classification.ToString()
                }),
            cancellationToken);

        Document reloaded = await LoadAsync(document.Id, cancellationToken);
        EffectiveAccess access = await accessResolver.EvaluateAsync(reloaded, clock.UtcNow, null, cancellationToken);

        return await BuildDetailAsync(reloaded, access, cancellationToken);
    }

    public async Task<DocumentDetailDto> UpdateAsync(
        Guid documentId, UpdateDocumentRequest request, CancellationToken cancellationToken = default)
    {
        Document document = await LoadAsync(documentId, cancellationToken);
        await accessResolver.EnsureAsync(document, DocumentAccessLevel.Edit, clock.UtcNow, cancellationToken);
        DocumentLifecycle.EnsureCanChangeMetadata(document.Status);

        await EnsureDepartmentExistsAsync(request.DepartmentId, cancellationToken);

        DocumentType type = await dbContext.DocumentTypes
            .Include(t => t.MetadataFields)
            .FirstAsync(t => t.Id == document.DocumentTypeId, cancellationToken);

        var before = new
        {
            document.Title,
            document.Summary,
            Classification = document.Classification.ToString(),
            document.DepartmentId,
            document.BusinessEntity,
            Tags = document.Tags.Select(t => t.Tag!.Name).OrderBy(n => n).ToArray()
        };

        // Optimistic concurrency: the client echoes the token it received, and a save based on a
        // stale read fails instead of silently overwriting someone else's edit.
        dbContext.Entry(document).Property(d => d.RowVersion).OriginalValue = DecodeToken(request.ConcurrencyToken);

        document.Title = request.Title.Trim();
        document.Summary = request.Description?.Trim();
        document.DepartmentId = request.DepartmentId;
        document.BusinessEntity = request.BusinessEntity?.Trim();
        document.Classification = request.Classification;

        metadataWriter.Apply(document, type, request.Metadata);
        await ApplyTagsAsync(document, request.Tags, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        var after = new
        {
            document.Title,
            document.Summary,
            Classification = document.Classification.ToString(),
            document.DepartmentId,
            document.BusinessEntity,
            Tags = document.Tags.Select(t => t.Tag!.Name).OrderBy(n => n).ToArray()
        };

        await auditService.WriteAsync(
            DocumentAudit(document, AuditActions.DocumentMetadataChanged, "Metadados alterados", before, after),
            cancellationToken);

        Document reloaded = await LoadAsync(documentId, cancellationToken);
        EffectiveAccess access = await accessResolver.EvaluateAsync(reloaded, clock.UtcNow, null, cancellationToken);

        return await BuildDetailAsync(reloaded, access, cancellationToken);
    }

    public async Task<DocumentDetailDto> AddVersionAsync(
        Guid documentId, DocumentUpload upload, string? comment, CancellationToken cancellationToken = default)
    {
        ValidateUpload(upload);

        Document document = await LoadAsync(documentId, cancellationToken);
        await accessResolver.EnsureAsync(document, DocumentAccessLevel.Edit, clock.UtcNow, cancellationToken);
        DocumentLifecycle.EnsureCanChangeContent(document.Status);

        foreach (DocumentVersion existing in document.Versions.Where(v => v.IsCurrent))
        {
            existing.IsCurrent = false;
        }

        int nextNumber = document.Versions.Count == 0 ? 1 : document.Versions.Max(v => v.VersionNumber) + 1;
        DocumentVersion version = await StoreVersionAsync(document, upload, nextNumber, comment, cancellationToken);

        document.CurrentVersionNumber = version.VersionNumber;
        document.CurrentVersionId = version.Id;

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            DocumentAudit(
                document,
                AuditActions.DocumentVersionCreated,
                $"Versão {version.VersionNumber} criada",
                after: new
                {
                    version.VersionNumber,
                    version.FileName,
                    version.FileSizeBytes,
                    version.ContentHash,
                    Comment = comment
                }),
            cancellationToken);

        Document reloaded = await LoadAsync(documentId, cancellationToken);
        EffectiveAccess access = await accessResolver.EvaluateAsync(reloaded, clock.UtcNow, null, cancellationToken);

        return await BuildDetailAsync(reloaded, access, cancellationToken);
    }

    public async Task<DocumentDetailDto> ChangeStatusAsync(
        Guid documentId, ChangeDocumentStatusRequest request, CancellationToken cancellationToken = default)
    {
        Document document = await LoadAsync(documentId, cancellationToken);

        // Deciding an approval is a different right from editing a document.
        DocumentAccessLevel required = request.Status is DocumentStatus.Approved or DocumentStatus.Rejected
            ? DocumentAccessLevel.Approve
            : DocumentAccessLevel.Edit;

        await accessResolver.EnsureAsync(document, required, clock.UtcNow, cancellationToken);

        if (request.Status == DocumentStatus.Archived)
        {
            return await ArchiveAsync(documentId, cancellationToken);
        }

        DocumentLifecycle.EnsureCanTransition(document.Status, request.Status);

        DocumentStatus previous = document.Status;
        document.Status = request.Status;

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            DocumentAudit(
                document,
                AuditActions.DocumentStatusChanged,
                $"Estado alterado de '{previous}' para '{request.Status}'",
                before: new { Status = previous.ToString() },
                after: new { Status = request.Status.ToString(), request.Comment }),
            cancellationToken);

        Document reloaded = await LoadAsync(documentId, cancellationToken);
        EffectiveAccess access = await accessResolver.EvaluateAsync(reloaded, clock.UtcNow, null, cancellationToken);

        return await BuildDetailAsync(reloaded, access, cancellationToken);
    }

    public async Task<DocumentDetailDto> ArchiveAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        Document document = await LoadAsync(documentId, cancellationToken);
        await accessResolver.EnsureAsync(document, DocumentAccessLevel.Edit, clock.UtcNow, cancellationToken);

        if (document.Status == DocumentStatus.Archived)
        {
            throw new DomainException("O documento já se encontra arquivado.", "already_archived");
        }

        DocumentLifecycle.EnsureCanTransition(document.Status, DocumentStatus.Archived);

        DocumentStatus previous = document.Status;
        DateTimeOffset now = clock.UtcNow;

        document.PreArchiveStatus = previous;
        document.Status = DocumentStatus.Archived;
        document.ArchivedAtUtc = now;
        document.RetentionDueAtUtc = await CalculateRetentionDueAsync(document, now, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            DocumentAudit(
                document,
                AuditActions.DocumentArchived,
                "Documento arquivado",
                before: new { Status = previous.ToString() },
                after: new { Status = DocumentStatus.Archived.ToString(), document.RetentionDueAtUtc }),
            cancellationToken);

        Document reloaded = await LoadAsync(documentId, cancellationToken);
        EffectiveAccess access = await accessResolver.EvaluateAsync(reloaded, clock.UtcNow, null, cancellationToken);

        return await BuildDetailAsync(reloaded, access, cancellationToken);
    }

    public async Task<DocumentDetailDto> RestoreAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        Document document = await LoadAsync(documentId, cancellationToken);
        await accessResolver.EnsureAsync(document, DocumentAccessLevel.Edit, clock.UtcNow, cancellationToken);

        if (document.Status != DocumentStatus.Archived)
        {
            throw new DomainException("Só é possível restaurar documentos arquivados.", "not_archived");
        }

        DocumentStatus target = DocumentLifecycle.RestoreTarget(document.PreArchiveStatus);

        document.Status = target;
        document.PreArchiveStatus = null;
        document.ArchivedAtUtc = null;
        document.RetentionDueAtUtc = null;

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            DocumentAudit(
                document,
                AuditActions.DocumentRestored,
                $"Documento restaurado para o estado '{target}'",
                before: new { Status = DocumentStatus.Archived.ToString() },
                after: new { Status = target.ToString() }),
            cancellationToken);

        Document reloaded = await LoadAsync(documentId, cancellationToken);
        EffectiveAccess access = await accessResolver.EvaluateAsync(reloaded, clock.UtcNow, null, cancellationToken);

        return await BuildDetailAsync(reloaded, access, cancellationToken);
    }

    public async Task DeleteAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        Document document = await LoadAsync(documentId, cancellationToken);
        await accessResolver.EnsureAsync(document, DocumentAccessLevel.Delete, clock.UtcNow, cancellationToken);

        // Soft delete: the interceptor turns this into a flag update, so versions, metadata and the
        // audit trail all survive and the document can be recovered by an administrator.
        dbContext.Documents.Remove(document);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            DocumentAudit(
                document,
                AuditActions.DocumentDeleted,
                "Documento eliminado (eliminação reversível)",
                before: new { document.Reference, document.Title, Status = document.Status.ToString() }),
            cancellationToken);
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    private async Task<Document> LoadAsync(Guid documentId, CancellationToken cancellationToken) =>
        await dbContext.Documents
            .Include(d => d.DocumentType)
            .Include(d => d.Department)
            .Include(d => d.RetentionPolicy)
            .Include(d => d.Versions)
            .Include(d => d.Tags).ThenInclude(t => t.Tag)
            .Include(d => d.MetadataValues)
            .Include(d => d.Permissions)
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(Document), documentId);

    private async Task<DocumentDetailDto> BuildDetailAsync(
        Document document, EffectiveAccess access, CancellationToken cancellationToken)
    {
        DocumentType type = document.DocumentType
            ?? await dbContext.DocumentTypes.FirstAsync(t => t.Id == document.DocumentTypeId, cancellationToken);

        List<DocumentMetadataField> fields = await dbContext.DocumentMetadataFields
            .Where(f => f.DocumentTypeId == type.Id)
            .OrderBy(f => f.DisplayOrder)
            .ToListAsync(cancellationToken);

        Guid[] userIds =
        [
            document.OwnerUserId,
            .. document.Versions.Select(v => v.CreatedByUserId).Where(id => id.HasValue).Select(id => id!.Value),
            .. document.Permissions.Select(p => p.GrantedByUserId).Where(id => id.HasValue).Select(id => id!.Value)
        ];

        Dictionary<Guid, string> userNames = await dbContext.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName })
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

        string? ownerEmail = await dbContext.Users
            .Where(u => u.Id == document.OwnerUserId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);

        DocumentListItemDto summary = new()
        {
            Id = document.Id,
            Reference = document.Reference,
            Title = document.Title,
            TypeCode = type.Code,
            TypeName = type.Name,
            TypeIcon = type.Icon,
            Status = document.Status,
            Classification = document.Classification,
            OwnerName = userNames.GetValueOrDefault(document.OwnerUserId, "—"),
            DepartmentName = document.Department?.Name,
            DepartmentCode = document.Department?.Code,
            CurrentVersionNumber = document.CurrentVersionNumber,
            FileSizeBytes = document.Versions.FirstOrDefault(v => v.IsCurrent)?.FileSizeBytes,
            CreatedAtUtc = document.CreatedAtUtc,
            ModifiedAtUtc = document.ModifiedAtUtc,
            RetentionDueAtUtc = document.RetentionDueAtUtc,
            Tags = [.. document.Tags.Where(t => t.Tag is not null).Select(t => new TagDto(t.TagId, t.Tag!.Name, t.Tag.Colour))]
        };

        // The permission list is management information: it is shown only to those who may change it.
        bool canSeePermissions = access.Allows(DocumentAccessLevel.Share) || access.Allows(DocumentAccessLevel.Administer);

        return new DocumentDetailDto
        {
            Summary = summary,
            Description = document.Summary,
            BusinessEntity = document.BusinessEntity,
            DocumentTypeId = document.DocumentTypeId,
            DepartmentId = document.DepartmentId,
            OwnerUserId = document.OwnerUserId,
            OwnerEmail = ownerEmail,
            RetentionPolicyName = document.RetentionPolicy?.Name,
            ArchivedAtUtc = document.ArchivedAtUtc,
            Versions =
            [
                .. document.Versions
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => new DocumentVersionDto(
                        v.Id,
                        v.VersionNumber,
                        v.FileName,
                        v.ContentType,
                        v.FileSizeBytes,
                        v.ContentHash,
                        v.Comment,
                        v.CreatedAtUtc,
                        v.CreatedByUserId is { } creator ? userNames.GetValueOrDefault(creator, "—") : "—",
                        v.IsCurrent))
            ],
            Metadata =
            [
                .. fields.Select(field => DocumentProjections.ToMetadataValue(
                    field, document.MetadataValues.FirstOrDefault(v => v.FieldId == field.Id)))
            ],
            Permissions = canSeePermissions
                ? [.. document.Permissions.Select(p => DocumentProjections.ToPermission(p, userNames))]
                : [],
            EffectiveAccess = DocumentProjections.ToEffectiveAccess(access),
            AllowedNextStatuses = DocumentLifecycle.NextStatuses(document.Status),
            CanChangeContent = DocumentLifecycle.AllowsContentChange(document.Status),
            CanChangeMetadata = DocumentLifecycle.AllowsMetadataChange(document.Status),
            ConcurrencyToken = Convert.ToBase64String(document.RowVersion)
        };
    }

    private async Task<DocumentVersion> StoreVersionAsync(
        Document document, DocumentUpload upload, int versionNumber, string? comment, CancellationToken cancellationToken)
    {
        StoredFile stored = await fileStorage.SaveAsync(
            upload.Content,
            document.TenantId,
            document.Id,
            new FileDescriptor(upload.FileName, upload.ContentType),
            cancellationToken);

        DocumentVersion version = new()
        {
            TenantId = document.TenantId,
            DocumentId = document.Id,
            VersionNumber = versionNumber,
            StorageKey = stored.StorageKey,
            FileName = SanitiseFileName(upload.FileName),
            ContentType = upload.ContentType,
            FileSizeBytes = stored.SizeBytes,
            ContentHash = stored.ContentHash,
            Comment = comment?.Trim(),
            IsCurrent = true,
            ExtractedText = await ExtractTextAsync(stored.StorageKey, upload.ContentType, cancellationToken)
        };

        document.Versions.Add(version);
        return version;
    }

    /// <summary>
    /// Extracts searchable text from formats that carry it natively. Binary formats return null
    /// rather than a guess: an empty extraction is honest, an invented one is not.
    /// </summary>
    private async Task<string?> ExtractTextAsync(string storageKey, string contentType, CancellationToken cancellationToken)
    {
        if (contentType is not ("text/plain" or "text/csv" or "text/markdown"))
        {
            return null;
        }

        await using Stream stream = await fileStorage.OpenReadAsync(storageKey, cancellationToken);
        using StreamReader reader = new(stream, Encoding.UTF8);

        char[] buffer = new char[MaxExtractedTextLength];
        int read = await reader.ReadBlockAsync(buffer, cancellationToken);

        return read == 0 ? null : new string(buffer, 0, read);
    }

    private void ValidateUpload(DocumentUpload upload)
    {
        if (upload.Length <= 0)
        {
            throw new DomainException("O ficheiro está vazio.", "empty_file");
        }

        if (upload.Length > _storageOptions.MaxFileSizeBytes)
        {
            throw new DomainException(
                $"O ficheiro excede o limite de {_storageOptions.MaxFileSizeBytes / (1024 * 1024)} MB.",
                "file_too_large");
        }

        string extension = Path.GetExtension(upload.FileName).ToLowerInvariant();

        if (!_storageOptions.AllowedExtensions.Contains(extension))
        {
            throw new DomainException($"A extensão '{extension}' não é permitida.", "extension_not_allowed");
        }

        if (!_storageOptions.AllowedContentTypes.Contains(upload.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new DomainException($"O tipo de conteúdo '{upload.ContentType}' não é permitido.", "content_type_not_allowed");
        }
    }

    private async Task ApplyTagsAsync(Document document, IReadOnlyList<string> tagNames, CancellationToken cancellationToken)
    {
        string[] normalised = [.. tagNames
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        List<Tag> existing = await dbContext.Tags
            .Where(t => normalised.Contains(t.Name))
            .ToListAsync(cancellationToken);

        List<Tag> resolved = [];

        foreach (string name in normalised)
        {
            Tag? tag = existing.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

            if (tag is null)
            {
                tag = new Tag { TenantId = document.TenantId, Name = name };
                dbContext.Tags.Add(tag);
            }

            resolved.Add(tag);
        }

        foreach (DocumentTag removed in document.Tags.Where(dt => resolved.All(t => t.Id != dt.TagId)).ToList())
        {
            document.Tags.Remove(removed);
            dbContext.DocumentTags.Remove(removed);
        }

        foreach (Tag tag in resolved.Where(t => document.Tags.All(dt => dt.TagId != t.Id)))
        {
            document.Tags.Add(new DocumentTag
            {
                DocumentId = document.Id,
                TagId = tag.Id,
                Tag = tag,
                AssignedAtUtc = clock.UtcNow,
                AssignedByUserId = currentUser.UserId
            });
        }
    }

    private async Task EnsureDepartmentExistsAsync(Guid? departmentId, CancellationToken cancellationToken)
    {
        if (departmentId is not { } id)
        {
            return;
        }

        bool exists = await dbContext.Departments.AnyAsync(d => d.Id == id, cancellationToken);

        if (!exists)
        {
            throw new EntityNotFoundException("Department", id);
        }
    }

    private async Task<DateTimeOffset?> CalculateRetentionDueAsync(
        Document document, DateTimeOffset archivedAt, CancellationToken cancellationToken)
    {
        Guid? policyId = document.RetentionPolicyId
            ?? await dbContext.DocumentTypes
                .Where(t => t.Id == document.DocumentTypeId)
                .Select(t => t.DefaultRetentionPolicyId)
                .FirstOrDefaultAsync(cancellationToken);

        if (policyId is not { } id)
        {
            return null;
        }

        RetentionPolicy? policy = await dbContext.RetentionPolicies.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (policy is null)
        {
            return null;
        }

        document.RetentionPolicyId = policy.Id;

        DateTimeOffset trigger = policy.Trigger switch
        {
            RetentionTrigger.OnCreation => document.CreatedAtUtc,
            RetentionTrigger.OnApproval => document.ModifiedAtUtc ?? archivedAt,
            _ => archivedAt
        };

        return policy.CalculateDueDate(trigger);
    }

    private static byte[] DecodeToken(string token)
    {
        try
        {
            return Convert.FromBase64String(token);
        }
        catch (FormatException)
        {
            throw new DomainException("Token de concorrência inválido. Recarregue o documento.", "invalid_concurrency_token");
        }
    }

    private static string SanitiseFileName(string fileName)
    {
        string name = Path.GetFileName(fileName);
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new([.. name.Where(c => !invalid.Contains(c))]);

        return cleaned.Length == 0 ? "documento" : cleaned[..Math.Min(cleaned.Length, 260)];
    }

    private static AuditEntry DocumentAudit(
        Document document,
        string action,
        string summary,
        object? before = null,
        object? after = null,
        IReadOnlyDictionary<string, object?>? metadata = null) =>
        new()
        {
            Action = action,
            EntityType = AuditEntityTypes.Document,
            EntityId = document.Id,
            EntityDisplayName = $"{document.Reference} — {document.Title}",
            Summary = summary,
            Before = before,
            After = after,
            Metadata = metadata
        };
}
