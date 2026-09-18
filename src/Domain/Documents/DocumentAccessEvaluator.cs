using DocOps.Domain.Identity;

namespace DocOps.Domain.Documents;

public enum AccessSource
{
    TenantAdministration,
    Ownership,
    UserGrant,
    DepartmentGrant,
    RoleGrant,
    ClassificationDefault
}

/// <summary>One rule that contributed rights, so the interface can explain *why* access exists.</summary>
public sealed record AccessReason(AccessSource Source, DocumentAccessLevel Granted, string Description);

/// <summary>One rule that removed rights the subject would otherwise have had.</summary>
public sealed record AccessRestriction(DocumentAccessLevel Removed, string Description);

public sealed record EffectiveAccess(
    DocumentAccessLevel Level,
    IReadOnlyList<AccessReason> Reasons,
    IReadOnlyList<AccessRestriction> Restrictions)
{
    public static readonly EffectiveAccess None = new(DocumentAccessLevel.None, [], []);

    public bool Allows(DocumentAccessLevel required) =>
        required != DocumentAccessLevel.None && (Level & required) == required;
}

/// <summary>The facts about a document that the access rules depend on.</summary>
public sealed record DocumentAccessFacts(
    Guid DocumentId,
    Guid TenantId,
    Guid OwnerUserId,
    Guid? DepartmentId,
    SecurityClassification Classification,
    DocumentStatus Status,
    bool IsDeleted);

/// <summary>The facts about the caller that the access rules depend on.</summary>
public sealed record AccessSubject(
    Guid UserId,
    Guid TenantId,
    UserType UserType,
    IReadOnlySet<Guid> DepartmentIds,
    IReadOnlySet<string> Roles,
    IReadOnlySet<string> TenantPermissions);

/// <summary>
/// Decides what a user may do with a document, and records the reasoning.
///
/// The rules are additive then capped: grants from ownership, explicit permissions and the
/// sensitivity label are unioned, and the result is intersected with what the user's tenant-level
/// permissions allow. A document grant can therefore never hand someone a right their role does not
/// include — sharing a document with an employee cannot turn them into an approver.
///
/// What the lifecycle allows *right now* is deliberately not folded in here. Access answers "who may
/// act on this document"; <see cref="DocumentLifecycle"/> answers "what may be done to it in its
/// current state". Mixing them made an approved document impossible to archive, because archiving
/// asked for an edit right that the approved state had stripped away.
///
/// This type is pure: no database, no clock beyond the value passed in, no HTTP. That is what makes
/// the authorisation rules exhaustively testable.
/// </summary>
public static class DocumentAccessEvaluator
{
    private static readonly (string Permission, DocumentAccessLevel Level)[] PermissionCeiling =
    [
        (Permissions.DocumentsView, DocumentAccessLevel.View),
        (Permissions.DocumentsDownload, DocumentAccessLevel.Download),
        (Permissions.DocumentsEdit, DocumentAccessLevel.Edit),
        (Permissions.DocumentsDelete, DocumentAccessLevel.Delete),
        (Permissions.DocumentsShare, DocumentAccessLevel.Share),
        (Permissions.DocumentsApprove, DocumentAccessLevel.Approve),
        (Permissions.DocumentsAdminister, DocumentAccessLevel.Administer)
    ];

    public static EffectiveAccess Evaluate(
        DocumentAccessFacts document,
        AccessSubject subject,
        IReadOnlyCollection<DocumentPermission> permissions,
        DateTimeOffset now)
    {
        // Cross-tenant access is never granted here. A platform administrator acting on another
        // organisation goes through a dedicated, audited service — not through this evaluator.
        if (document.TenantId != subject.TenantId)
        {
            return EffectiveAccess.None;
        }

        List<AccessReason> reasons = [];
        List<AccessRestriction> restrictions = [];
        DocumentAccessLevel granted = DocumentAccessLevel.None;

        if (subject.TenantPermissions.Contains(Permissions.DocumentsAdminister))
        {
            granted |= DocumentAccessLevel.Full;
            reasons.Add(new AccessReason(
                AccessSource.TenantAdministration,
                DocumentAccessLevel.Full,
                "Administração documental da organização"));
        }

        if (document.OwnerUserId == subject.UserId)
        {
            granted |= DocumentAccessLevel.Owner;
            reasons.Add(new AccessReason(
                AccessSource.Ownership,
                DocumentAccessLevel.Owner,
                "É o proprietário do documento"));
        }

        foreach (DocumentPermission permission in permissions)
        {
            if (permission.DocumentId != document.DocumentId || permission.IsExpired(now))
            {
                continue;
            }

            switch (permission.SubjectType)
            {
                case PermissionSubjectType.User when permission.SubjectId == subject.UserId:
                    granted |= permission.AccessLevel;
                    reasons.Add(new AccessReason(
                        AccessSource.UserGrant,
                        permission.AccessLevel,
                        "Permissão atribuída diretamente ao utilizador"));
                    break;

                case PermissionSubjectType.Department
                    when permission.SubjectId is { } departmentId && subject.DepartmentIds.Contains(departmentId):
                    granted |= permission.AccessLevel;
                    reasons.Add(new AccessReason(
                        AccessSource.DepartmentGrant,
                        permission.AccessLevel,
                        $"Permissão do departamento {permission.SubjectDisplayName}"));
                    break;

                case PermissionSubjectType.Role
                    when permission.RoleName is { } role && subject.Roles.Contains(role):
                    granted |= permission.AccessLevel;
                    reasons.Add(new AccessReason(
                        AccessSource.RoleGrant,
                        permission.AccessLevel,
                        $"Permissão do perfil {permission.SubjectDisplayName}"));
                    break;
            }
        }

        // External users reach documents only through explicit grants: no sensitivity label ever
        // opens a document to a supplier or partner by default.
        if (subject.UserType == UserType.Internal)
        {
            ApplyClassificationDefault(document, subject, ref granted, reasons);
        }

        if (document.IsDeleted && !subject.TenantPermissions.Contains(Permissions.DocumentsAdminister))
        {
            return new EffectiveAccess(
                DocumentAccessLevel.None,
                reasons,
                [.. restrictions, new AccessRestriction(granted, "Documento eliminado: visível apenas à administração documental")]);
        }

        granted = ApplyPermissionCeiling(subject, granted, restrictions);

        return new EffectiveAccess(granted, reasons, restrictions);
    }

    private static void ApplyClassificationDefault(
        DocumentAccessFacts document, AccessSubject subject, ref DocumentAccessLevel granted, List<AccessReason> reasons)
    {
        bool inOwningDepartment = document.DepartmentId is { } departmentId && subject.DepartmentIds.Contains(departmentId);

        (DocumentAccessLevel level, string? description) = document.Classification switch
        {
            SecurityClassification.Public =>
                (DocumentAccessLevel.Reader, "Classificação pública: leitura para toda a organização"),

            SecurityClassification.Internal =>
                (DocumentAccessLevel.Reader, "Classificação interna: leitura para colaboradores da organização"),

            SecurityClassification.Confidential when inOwningDepartment =>
                (DocumentAccessLevel.Reader, "Classificação confidencial: leitura para o departamento responsável"),

            // Restricted, and confidential outside the owning department, require an explicit grant.
            _ => (DocumentAccessLevel.None, null)
        };

        if (description is not null)
        {
            granted |= level;
            reasons.Add(new AccessReason(AccessSource.ClassificationDefault, level, description));
        }
    }

    private static DocumentAccessLevel ApplyPermissionCeiling(
        AccessSubject subject, DocumentAccessLevel granted, List<AccessRestriction> restrictions)
    {
        DocumentAccessLevel ceiling = DocumentAccessLevel.None;

        foreach ((string permission, DocumentAccessLevel level) in PermissionCeiling)
        {
            if (subject.TenantPermissions.Contains(permission))
            {
                ceiling |= level;
            }
        }

        DocumentAccessLevel removed = granted & ~ceiling;

        if (removed is not DocumentAccessLevel.None)
        {
            restrictions.Add(new AccessRestriction(
                removed, "O perfil do utilizador não inclui estas permissões na organização"));
        }

        return granted & ceiling;
    }
}
