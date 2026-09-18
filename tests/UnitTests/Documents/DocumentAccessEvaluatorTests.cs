using DocOps.Domain.Documents;
using DocOps.Domain.Identity;

namespace DocOps.UnitTests.Documents;

/// <summary>
/// The access rules are the security core of the document module, so they are tested exhaustively and
/// in isolation: no database, no HTTP, no clock beyond the value passed in.
/// </summary>
public class DocumentAccessEvaluatorTests
{
    private static readonly Guid TenantId = Guid.Parse("0192d3a0-0000-7000-8000-00000000aaaa");
    private static readonly Guid OtherTenantId = Guid.Parse("0192d3a0-0000-7000-8000-00000000bbbb");
    private static readonly Guid DocumentId = Guid.Parse("0192d3a0-0000-7000-8000-00000000cccc");
    private static readonly Guid OwnerId = Guid.Parse("0192d3a0-0000-7000-8000-00000000dddd");
    private static readonly Guid OtherUserId = Guid.Parse("0192d3a0-0000-7000-8000-00000000eeee");
    private static readonly Guid FinanceDepartmentId = Guid.Parse("0192d3a0-0000-7000-8000-00000000f111");
    private static readonly Guid LegalDepartmentId = Guid.Parse("0192d3a0-0000-7000-8000-00000000f222");
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);

    private static readonly string[] FullDocumentPermissions =
    [
        Permissions.DocumentsView, Permissions.DocumentsDownload, Permissions.DocumentsEdit,
        Permissions.DocumentsDelete, Permissions.DocumentsShare, Permissions.DocumentsApprove
    ];

    private static DocumentAccessFacts Document(
        SecurityClassification classification = SecurityClassification.Internal,
        DocumentStatus status = DocumentStatus.Draft,
        Guid? departmentId = null,
        Guid? ownerId = null,
        Guid? tenantId = null,
        bool isDeleted = false) =>
        new(DocumentId, tenantId ?? TenantId, ownerId ?? OwnerId, departmentId, classification, status, isDeleted);

    private static AccessSubject Subject(
        Guid? userId = null,
        UserType userType = UserType.Internal,
        Guid[]? departments = null,
        string[]? roles = null,
        string[]? permissions = null,
        Guid? tenantId = null) =>
        new(
            userId ?? OtherUserId,
            tenantId ?? TenantId,
            userType,
            (departments ?? []).ToHashSet(),
            (roles ?? []).ToHashSet(StringComparer.Ordinal),
            (permissions ?? FullDocumentPermissions).ToHashSet(StringComparer.Ordinal));

    private static DocumentPermission Grant(
        PermissionSubjectType subjectType,
        DocumentAccessLevel level,
        Guid? subjectId = null,
        string? roleName = null,
        DateTimeOffset? expiresAt = null) =>
        new()
        {
            DocumentId = DocumentId,
            SubjectType = subjectType,
            SubjectId = subjectId,
            RoleName = roleName,
            SubjectDisplayName = roleName ?? subjectId?.ToString() ?? "—",
            AccessLevel = level,
            ExpiresAtUtc = expiresAt
        };

    [Fact]
    public void Cross_tenant_access_is_never_granted()
    {
        EffectiveAccess access = DocumentAccessEvaluator.Evaluate(
            Document(tenantId: OtherTenantId),
            Subject(permissions: [.. FullDocumentPermissions, Permissions.DocumentsAdminister]),
            [],
            Now);

        Assert.Equal(DocumentAccessLevel.None, access.Level);
        Assert.Empty(access.Reasons);
    }

    [Fact]
    public void Owner_receives_full_ownership_rights_and_the_reason_says_so()
    {
        EffectiveAccess access = DocumentAccessEvaluator.Evaluate(
            Document(), Subject(userId: OwnerId), [], Now);

        Assert.True(access.Allows(DocumentAccessLevel.Edit));
        Assert.True(access.Allows(DocumentAccessLevel.Delete));
        Assert.True(access.Allows(DocumentAccessLevel.Share));
        Assert.Contains(access.Reasons, reason => reason.Source == AccessSource.Ownership);
    }

    [Fact]
    public void Document_administrator_receives_everything_including_administer()
    {
        EffectiveAccess access = DocumentAccessEvaluator.Evaluate(
            Document(SecurityClassification.Restricted),
            Subject(permissions: [.. FullDocumentPermissions, Permissions.DocumentsAdminister]),
            [],
            Now);

        Assert.True(access.Allows(DocumentAccessLevel.Administer));
        Assert.Contains(access.Reasons, reason => reason.Source == AccessSource.TenantAdministration);
    }

    [Theory]
    [InlineData(SecurityClassification.Public, true)]
    [InlineData(SecurityClassification.Internal, true)]
    [InlineData(SecurityClassification.Confidential, false)]
    [InlineData(SecurityClassification.Restricted, false)]
    public void Classification_decides_the_default_reach_of_an_internal_colleague(
        SecurityClassification classification, bool expectedVisible)
    {
        EffectiveAccess access = DocumentAccessEvaluator.Evaluate(
            Document(classification, departmentId: FinanceDepartmentId),
            Subject(departments: [LegalDepartmentId]),
            [],
            Now);

        Assert.Equal(expectedVisible, access.Allows(DocumentAccessLevel.View));
    }

    [Fact]
    public void Confidential_document_is_readable_by_the_owning_department()
    {
        EffectiveAccess access = DocumentAccessEvaluator.Evaluate(
            Document(SecurityClassification.Confidential, departmentId: FinanceDepartmentId),
            Subject(departments: [FinanceDepartmentId]),
            [],
            Now);

        Assert.True(access.Allows(DocumentAccessLevel.Reader));
        Assert.Contains(access.Reasons, reason => reason.Source == AccessSource.ClassificationDefault);
    }

    [Fact]
    public void Restricted_document_requires_an_explicit_grant_even_inside_the_department()
    {
        DocumentAccessFacts document = Document(SecurityClassification.Restricted, departmentId: FinanceDepartmentId);
        AccessSubject subject = Subject(departments: [FinanceDepartmentId]);

        Assert.False(DocumentAccessEvaluator.Evaluate(document, subject, [], Now).Allows(DocumentAccessLevel.View));

        EffectiveAccess withGrant = DocumentAccessEvaluator.Evaluate(
            document,
            subject,
            [Grant(PermissionSubjectType.Department, DocumentAccessLevel.Reader, FinanceDepartmentId)],
            Now);

        Assert.True(withGrant.Allows(DocumentAccessLevel.Reader));
    }

    [Fact]
    public void External_user_never_gains_access_from_a_classification_default()
    {
        EffectiveAccess access = DocumentAccessEvaluator.Evaluate(
            Document(SecurityClassification.Public),
            Subject(userType: UserType.External),
            [],
            Now);

        Assert.Equal(DocumentAccessLevel.None, access.Level);
        Assert.DoesNotContain(access.Reasons, reason => reason.Source == AccessSource.ClassificationDefault);
    }

    [Fact]
    public void External_user_reaches_a_document_only_through_an_explicit_grant()
    {
        EffectiveAccess access = DocumentAccessEvaluator.Evaluate(
            Document(SecurityClassification.Confidential),
            Subject(userType: UserType.External, permissions: [Permissions.DocumentsView, Permissions.DocumentsDownload]),
            [Grant(PermissionSubjectType.User, DocumentAccessLevel.Reader, OtherUserId)],
            Now);

        Assert.True(access.Allows(DocumentAccessLevel.Reader));
        Assert.Contains(access.Reasons, reason => reason.Source == AccessSource.UserGrant);
    }

    [Fact]
    public void Department_grant_applies_only_to_members_of_that_department()
    {
        DocumentPermission[] grants = [Grant(PermissionSubjectType.Department, DocumentAccessLevel.Contributor, LegalDepartmentId)];
        DocumentAccessFacts document = Document(SecurityClassification.Restricted);

        Assert.True(DocumentAccessEvaluator
            .Evaluate(document, Subject(departments: [LegalDepartmentId]), grants, Now)
            .Allows(DocumentAccessLevel.Edit));

        Assert.False(DocumentAccessEvaluator
            .Evaluate(document, Subject(departments: [FinanceDepartmentId]), grants, Now)
            .Allows(DocumentAccessLevel.View));
    }

    [Fact]
    public void Role_grant_applies_only_to_holders_of_that_role()
    {
        DocumentPermission[] grants =
            [Grant(PermissionSubjectType.Role, DocumentAccessLevel.Reader, roleName: RoleNames.Manager)];

        DocumentAccessFacts document = Document(SecurityClassification.Restricted);

        Assert.True(DocumentAccessEvaluator
            .Evaluate(document, Subject(roles: [RoleNames.Manager]), grants, Now)
            .Allows(DocumentAccessLevel.View));

        Assert.False(DocumentAccessEvaluator
            .Evaluate(document, Subject(roles: [RoleNames.Employee]), grants, Now)
            .Allows(DocumentAccessLevel.View));
    }

    [Fact]
    public void Expired_grant_stops_granting_access()
    {
        DocumentPermission expired = Grant(
            PermissionSubjectType.User, DocumentAccessLevel.Reader, OtherUserId, expiresAt: Now.AddDays(-1));

        EffectiveAccess access = DocumentAccessEvaluator.Evaluate(
            Document(SecurityClassification.Restricted), Subject(), [expired], Now);

        Assert.Equal(DocumentAccessLevel.None, access.Level);
    }

    [Fact]
    public void Grant_still_valid_at_the_evaluation_moment_is_honoured()
    {
        DocumentPermission valid = Grant(
            PermissionSubjectType.User, DocumentAccessLevel.Reader, OtherUserId, expiresAt: Now.AddHours(1));

        EffectiveAccess access = DocumentAccessEvaluator.Evaluate(
            Document(SecurityClassification.Restricted), Subject(), [valid], Now);

        Assert.True(access.Allows(DocumentAccessLevel.View));
    }

    [Fact]
    public void A_document_grant_cannot_exceed_what_the_user_role_allows()
    {
        // Sharing a document with an employee must not turn that employee into an approver.
        EffectiveAccess access = DocumentAccessEvaluator.Evaluate(
            Document(SecurityClassification.Restricted),
            Subject(permissions: [Permissions.DocumentsView, Permissions.DocumentsDownload]),
            [Grant(PermissionSubjectType.User, DocumentAccessLevel.Owner, OtherUserId)],
            Now);

        Assert.True(access.Allows(DocumentAccessLevel.Reader));
        Assert.False(access.Allows(DocumentAccessLevel.Approve));
        Assert.False(access.Allows(DocumentAccessLevel.Edit));
        Assert.Contains(access.Restrictions, restriction => restriction.Removed.HasFlag(DocumentAccessLevel.Approve));
    }

    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.PendingApproval)]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Signed)]
    public void Status_does_not_change_who_may_act_on_the_document(DocumentStatus status)
    {
        // Access answers "who may act"; DocumentLifecycle answers "what may be done in this state".
        // Folding the two together once made an approved document impossible to archive, because
        // archiving asked for an edit right that the approved state had stripped away.
        EffectiveAccess access = DocumentAccessEvaluator.Evaluate(
            Document(status: status), Subject(userId: OwnerId), [], Now);

        Assert.True(access.Allows(DocumentAccessLevel.Edit));
        Assert.True(access.Allows(DocumentAccessLevel.Reader));
    }

    [Fact]
    public void Deleted_document_is_invisible_to_everyone_except_document_administration()
    {
        DocumentAccessFacts deleted = Document(isDeleted: true);

        Assert.Equal(
            DocumentAccessLevel.None,
            DocumentAccessEvaluator.Evaluate(deleted, Subject(userId: OwnerId), [], Now).Level);

        EffectiveAccess adminAccess = DocumentAccessEvaluator.Evaluate(
            deleted,
            Subject(permissions: [.. FullDocumentPermissions, Permissions.DocumentsAdminister]),
            [],
            Now);

        Assert.True(adminAccess.Allows(DocumentAccessLevel.View));
    }

    [Fact]
    public void Grants_from_several_sources_add_up()
    {
        EffectiveAccess access = DocumentAccessEvaluator.Evaluate(
            Document(SecurityClassification.Restricted, departmentId: FinanceDepartmentId),
            Subject(departments: [FinanceDepartmentId], roles: [RoleNames.Manager]),
            [
                Grant(PermissionSubjectType.Department, DocumentAccessLevel.Reader, FinanceDepartmentId),
                Grant(PermissionSubjectType.Role, DocumentAccessLevel.Approver, roleName: RoleNames.Manager),
                Grant(PermissionSubjectType.User, DocumentAccessLevel.Contributor, OtherUserId)
            ],
            Now);

        Assert.True(access.Allows(DocumentAccessLevel.Edit));
        Assert.True(access.Allows(DocumentAccessLevel.Approve));
        Assert.Equal(3, access.Reasons.Count(r => r.Source is AccessSource.DepartmentGrant or AccessSource.RoleGrant or AccessSource.UserGrant));
    }

    [Fact]
    public void A_grant_on_another_document_is_ignored()
    {
        DocumentPermission foreignGrant = Grant(PermissionSubjectType.User, DocumentAccessLevel.Owner, OtherUserId);
        foreignGrant.DocumentId = Guid.Parse("0192d3a0-0000-7000-8000-0000000fffff");

        EffectiveAccess access = DocumentAccessEvaluator.Evaluate(
            Document(SecurityClassification.Restricted), Subject(), [foreignGrant], Now);

        Assert.Equal(DocumentAccessLevel.None, access.Level);
    }

    [Fact]
    public void A_user_without_the_view_permission_sees_nothing_even_as_owner()
    {
        EffectiveAccess access = DocumentAccessEvaluator.Evaluate(
            Document(), Subject(userId: OwnerId, permissions: []), [], Now);

        Assert.Equal(DocumentAccessLevel.None, access.Level);
        Assert.NotEmpty(access.Restrictions);
    }
}
