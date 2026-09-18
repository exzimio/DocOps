using System.Linq.Expressions;
using DocOps.Application.Abstractions;
using DocOps.Domain.Common;
using DocOps.Domain.Documents;
using DocOps.Domain.Identity;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Documents;

/// <summary>
/// Bridges the pure access rules in <see cref="DocumentAccessEvaluator"/> to the database.
///
/// It serves two shapes of the same question. For a single document it evaluates the full rules in
/// memory, including the reasoning shown in the effective-access panel. For a list it produces an
/// equivalent SQL predicate, so filtering happens in the database and paging counts stay correct —
/// filtering a page after fetching it would report wrong totals and leak the existence of documents.
/// </summary>
public sealed class DocumentAccessResolver(DocOpsDbContext dbContext, ICurrentUser currentUser)
{
    private AccessSubject? _cachedSubject;

    public async Task<AccessSubject> GetSubjectAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedSubject is not null)
        {
            return _cachedSubject;
        }

        Guid userId = currentUser.UserId
            ?? throw new ForbiddenOperationException("Operação sobre documentos exige um utilizador autenticado.");

        Guid tenantId = currentUser.TenantId
            ?? throw new ForbiddenOperationException("Operação sobre documentos exige uma organização.");

        List<Guid> departmentIds = await dbContext.UserDepartments
            .Where(ud => ud.UserId == userId)
            .Select(ud => ud.DepartmentId)
            .ToListAsync(cancellationToken);

        _cachedSubject = new AccessSubject(
            userId,
            tenantId,
            currentUser.UserType,
            departmentIds.ToHashSet(),
            currentUser.Roles.ToHashSet(StringComparer.Ordinal),
            currentUser.Permissions.ToHashSet(StringComparer.Ordinal));

        return _cachedSubject;
    }

    public async Task<EffectiveAccess> EvaluateAsync(
        Document document, DateTimeOffset now, Guid? forUserId = null, CancellationToken cancellationToken = default)
    {
        AccessSubject subject = forUserId is { } otherUserId
            ? await BuildSubjectForAsync(otherUserId, cancellationToken)
            : await GetSubjectAsync(cancellationToken);

        List<DocumentPermission> permissions = document.Permissions.Count > 0
            ? [.. document.Permissions]
            : await dbContext.DocumentPermissions
                .Where(p => p.DocumentId == document.Id)
                .ToListAsync(cancellationToken);

        return DocumentAccessEvaluator.Evaluate(ToFacts(document), subject, permissions, now);
    }

    /// <summary>Throws when the caller lacks the required rights, after recording the refusal.</summary>
    public async Task<EffectiveAccess> EnsureAsync(
        Document document, DocumentAccessLevel required, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        EffectiveAccess access = await EvaluateAsync(document, now, forUserId: null, cancellationToken);

        if (!access.Allows(required))
        {
            throw new ForbiddenOperationException(
                $"Sem permissão '{required}' sobre o documento '{document.Reference}'.");
        }

        return access;
    }

    /// <summary>
    /// SQL-translatable predicate for "documents this subject may see". It mirrors the read path of
    /// <see cref="DocumentAccessEvaluator"/>; the grant rule relies on the invariant that every
    /// document permission includes at least <see cref="DocumentAccessLevel.View"/>.
    /// </summary>
    public static Expression<Func<Document, bool>> BuildVisibilityPredicate(AccessSubject subject, DateTimeOffset now)
    {
        if (subject.TenantPermissions.Contains(Permissions.DocumentsAdminister))
        {
            return _ => true;
        }

        Guid userId = subject.UserId;
        Guid[] departmentIds = [.. subject.DepartmentIds];
        string[] roles = [.. subject.Roles];
        bool isInternal = subject.UserType == UserType.Internal;

        return document =>
            document.OwnerUserId == userId
            || document.Permissions.Any(p =>
                (p.ExpiresAtUtc == null || p.ExpiresAtUtc > now)
                && ((p.SubjectType == PermissionSubjectType.User && p.SubjectId == userId)
                    || (p.SubjectType == PermissionSubjectType.Department
                        && p.SubjectId != null
                        && departmentIds.Contains(p.SubjectId.Value))
                    || (p.SubjectType == PermissionSubjectType.Role
                        && p.RoleName != null
                        && roles.Contains(p.RoleName))))
            || (isInternal
                && (document.Classification == SecurityClassification.Public
                    || document.Classification == SecurityClassification.Internal))
            || (isInternal
                && document.Classification == SecurityClassification.Confidential
                && document.DepartmentId != null
                && departmentIds.Contains(document.DepartmentId.Value));
    }

    public static DocumentAccessFacts ToFacts(Document document) => new(
        document.Id,
        document.TenantId,
        document.OwnerUserId,
        document.DepartmentId,
        document.Classification,
        document.Status,
        document.IsDeleted);

    private async Task<AccessSubject> BuildSubjectForAsync(Guid userId, CancellationToken cancellationToken)
    {
        ApplicationUser user = await dbContext.Users
            .Include(u => u.Departments)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(ApplicationUser), userId);

        List<string> roles = await (
            from userRole in dbContext.UserRoles
            join role in dbContext.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == userId
            select role.Name!).ToListAsync(cancellationToken);

        List<string> permissions = await (
            from userRole in dbContext.UserRoles
            join roleClaim in dbContext.RoleClaims on userRole.RoleId equals roleClaim.RoleId
            where userRole.UserId == userId && roleClaim.ClaimType == DocOpsClaimTypes.Permission
            select roleClaim.ClaimValue!).Distinct().ToListAsync(cancellationToken);

        return new AccessSubject(
            user.Id,
            user.TenantId,
            user.UserType,
            user.Departments.Select(d => d.DepartmentId).ToHashSet(),
            roles.ToHashSet(StringComparer.Ordinal),
            permissions.ToHashSet(StringComparer.Ordinal));
    }
}
