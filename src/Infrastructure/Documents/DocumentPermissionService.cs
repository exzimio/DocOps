using DocOps.Application.Abstractions;
using DocOps.Application.Documents;
using DocOps.Domain.Auditing;
using DocOps.Domain.Common;
using DocOps.Domain.Documents;
using DocOps.Domain.Identity;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Documents;

public sealed class DocumentPermissionService(
    DocOpsDbContext dbContext,
    DocumentAccessResolver accessResolver,
    IAuditService auditService,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IDocumentPermissionService
{
    public async Task<IReadOnlyList<DocumentPermissionDto>> ListAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        Document document = await LoadAsync(documentId, cancellationToken);
        await accessResolver.EnsureAsync(document, DocumentAccessLevel.Share, clock.UtcNow, cancellationToken);

        return await MapAsync(document.Permissions, cancellationToken);
    }

    public async Task<DocumentPermissionDto> GrantAsync(
        Guid documentId, GrantDocumentPermissionRequest request, CancellationToken cancellationToken = default)
    {
        Document document = await LoadAsync(documentId, cancellationToken);
        await accessResolver.EnsureAsync(document, DocumentAccessLevel.Share, clock.UtcNow, cancellationToken);

        // Every grant carries at least View: a right to edit or approve something you cannot open is
        // meaningless, and the invariant lets the document list filter access in SQL.
        DocumentAccessLevel level = AccessLevelNames.FromNames(request.AccessLevels) | DocumentAccessLevel.View;

        (Guid? subjectId, string? roleName, string displayName) =
            await ResolveSubjectAsync(request, cancellationToken);

        DocumentPermission? permission = document.Permissions.FirstOrDefault(p =>
            p.SubjectType == request.SubjectType
            && p.SubjectId == subjectId
            && string.Equals(p.RoleName, roleName, StringComparison.Ordinal));

        DocumentAccessLevel? previousLevel = permission?.AccessLevel;

        if (permission is null)
        {
            permission = new DocumentPermission
            {
                TenantId = document.TenantId,
                DocumentId = document.Id,
                SubjectType = request.SubjectType,
                SubjectId = subjectId,
                RoleName = roleName,
                SubjectDisplayName = displayName,
                AccessLevel = level,
                GrantedByUserId = currentUser.UserId,
                GrantedAtUtc = clock.UtcNow,
                ExpiresAtUtc = request.ExpiresAtUtc
            };

            document.Permissions.Add(permission);
        }
        else
        {
            permission.AccessLevel = level;
            permission.SubjectDisplayName = displayName;
            permission.ExpiresAtUtc = request.ExpiresAtUtc;
            permission.GrantedByUserId = currentUser.UserId;
            permission.GrantedAtUtc = clock.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = AuditActions.DocumentPermissionGranted,
                EntityType = AuditEntityTypes.Document,
                EntityId = document.Id,
                EntityDisplayName = $"{document.Reference} — {document.Title}",
                Severity = AuditSeverity.Security,
                Summary = $"Acesso concedido a {displayName}",
                Before = previousLevel is { } previous
                    ? new { Subject = displayName, Levels = AccessLevelNames.ToNames(previous) }
                    : null,
                After = new
                {
                    Subject = displayName,
                    SubjectType = request.SubjectType.ToString(),
                    Levels = AccessLevelNames.ToNames(level),
                    request.ExpiresAtUtc
                }
            },
            cancellationToken);

        IReadOnlyList<DocumentPermissionDto> mapped = await MapAsync([permission], cancellationToken);
        return mapped[0];
    }

    public async Task RevokeAsync(Guid documentId, Guid permissionId, CancellationToken cancellationToken = default)
    {
        Document document = await LoadAsync(documentId, cancellationToken);
        await accessResolver.EnsureAsync(document, DocumentAccessLevel.Share, clock.UtcNow, cancellationToken);

        DocumentPermission permission = document.Permissions.FirstOrDefault(p => p.Id == permissionId)
            ?? throw new EntityNotFoundException(nameof(DocumentPermission), permissionId);

        dbContext.DocumentPermissions.Remove(permission);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = AuditActions.DocumentPermissionRevoked,
                EntityType = AuditEntityTypes.Document,
                EntityId = document.Id,
                EntityDisplayName = $"{document.Reference} — {document.Title}",
                Severity = AuditSeverity.Security,
                Summary = $"Acesso revogado a {permission.SubjectDisplayName}",
                Before = new
                {
                    Subject = permission.SubjectDisplayName,
                    SubjectType = permission.SubjectType.ToString(),
                    Levels = AccessLevelNames.ToNames(permission.AccessLevel)
                }
            },
            cancellationToken);
    }

    public async Task<EffectiveAccessDto> GetEffectiveAccessAsync(
        Guid documentId, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        Document document = await LoadAsync(documentId, cancellationToken);

        // Inspecting someone else's access is an administrative action, not a normal read.
        if (userId is { } target && target != currentUser.UserId)
        {
            await accessResolver.EnsureAsync(document, DocumentAccessLevel.Share, clock.UtcNow, cancellationToken);
            EffectiveAccess other = await accessResolver.EvaluateAsync(document, clock.UtcNow, target, cancellationToken);
            return DocumentProjections.ToEffectiveAccess(other);
        }

        EffectiveAccess own = await accessResolver.EvaluateAsync(document, clock.UtcNow, null, cancellationToken);

        if (!own.Allows(DocumentAccessLevel.View))
        {
            throw new ForbiddenOperationException($"Sem acesso ao documento '{document.Reference}'.");
        }

        return DocumentProjections.ToEffectiveAccess(own);
    }

    private async Task<Document> LoadAsync(Guid documentId, CancellationToken cancellationToken) =>
        await dbContext.Documents
            .Include(d => d.Permissions)
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(Document), documentId);

    private async Task<(Guid? SubjectId, string? RoleName, string DisplayName)> ResolveSubjectAsync(
        GrantDocumentPermissionRequest request, CancellationToken cancellationToken)
    {
        switch (request.SubjectType)
        {
            case PermissionSubjectType.User:
            {
                Guid id = request.SubjectId ?? throw new DomainException("Indique o utilizador.", "subject_required");

                // The tenant query filter is what stops a grant to a user of another organisation.
                var user = await dbContext.Users
                    .Where(u => u.Id == id)
                    .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName })
                    .FirstOrDefaultAsync(cancellationToken)
                    ?? throw new EntityNotFoundException(nameof(ApplicationUser), id);

                return (user.Id, null, user.Name);
            }

            case PermissionSubjectType.Department:
            {
                Guid id = request.SubjectId ?? throw new DomainException("Indique o departamento.", "subject_required");

                var department = await dbContext.Departments
                    .Where(d => d.Id == id)
                    .Select(d => new { d.Id, d.Name })
                    .FirstOrDefaultAsync(cancellationToken)
                    ?? throw new EntityNotFoundException("Department", id);

                return (department.Id, null, department.Name);
            }

            case PermissionSubjectType.Role:
            {
                string roleName = request.RoleName ?? throw new DomainException("Indique o perfil.", "subject_required");

                bool exists = await dbContext.Roles.AnyAsync(r => r.Name == roleName, cancellationToken);

                if (!exists)
                {
                    throw new EntityNotFoundException(nameof(ApplicationRole), roleName);
                }

                return (null, roleName, roleName);
            }

            default:
                throw new DomainException("Tipo de destinatário inválido.", "invalid_subject_type");
        }
    }

    private async Task<IReadOnlyList<DocumentPermissionDto>> MapAsync(
        IEnumerable<DocumentPermission> permissions, CancellationToken cancellationToken)
    {
        DocumentPermission[] list = [.. permissions];

        Guid[] grantedBy = [.. list.Select(p => p.GrantedByUserId).Where(id => id.HasValue).Select(id => id!.Value).Distinct()];

        Dictionary<Guid, string> names = await dbContext.Users
            .Where(u => grantedBy.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName })
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);

        return [.. list
            .OrderBy(p => p.SubjectType)
            .ThenBy(p => p.SubjectDisplayName)
            .Select(p => DocumentProjections.ToPermission(p, names))];
    }
}
