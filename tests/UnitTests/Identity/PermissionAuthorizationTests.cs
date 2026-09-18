using System.Security.Claims;
using DocOps.Domain.Identity;
using DocOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;

namespace DocOps.UnitTests.Identity;

public class PermissionAuthorizationHandlerTests
{
    private static AuthorizationHandlerContext ContextFor(PermissionRequirement requirement, params string[] permissions)
    {
        ClaimsIdentity identity = new(
            permissions.Select(p => new Claim(DocOpsClaimTypes.Permission, p)),
            authenticationType: "TestCookie");

        return new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(identity), resource: null);
    }

    [Fact]
    public async Task Succeeds_when_the_principal_carries_the_required_permission()
    {
        PermissionRequirement requirement = new(Permissions.DocumentsApprove);
        AuthorizationHandlerContext context = ContextFor(requirement, Permissions.DocumentsView, Permissions.DocumentsApprove);

        await new PermissionAuthorizationHandler().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Fails_when_the_principal_has_other_permissions_only()
    {
        PermissionRequirement requirement = new(Permissions.DocumentsApprove);
        AuthorizationHandlerContext context = ContextFor(requirement, Permissions.DocumentsView, Permissions.SearchUse);

        await new PermissionAuthorizationHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Fails_for_an_anonymous_principal()
    {
        PermissionRequirement requirement = new(Permissions.DocumentsView);
        AuthorizationHandlerContext context = new([requirement], new ClaimsPrincipal(new ClaimsIdentity()), resource: null);

        await new PermissionAuthorizationHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Does_not_match_a_permission_by_prefix()
    {
        // "documents.view" must not satisfy "documents.view.all"-style broader grants by accident.
        PermissionRequirement requirement = new("documents.view.all");
        AuthorizationHandlerContext context = ContextFor(requirement, Permissions.DocumentsView);

        await new PermissionAuthorizationHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}

public class PermissionPolicyProviderTests
{
    private static PermissionPolicyProvider CreateProvider() =>
        new(Microsoft.Extensions.Options.Options.Create(new AuthorizationOptions()));

    [Fact]
    public async Task Builds_a_policy_on_demand_for_a_permission_attribute()
    {
        AuthorizationPolicy? policy = await CreateProvider()
            .GetPolicyAsync(HasPermissionAttribute.PolicyPrefix + Permissions.AuditExport);

        Assert.NotNull(policy);
        PermissionRequirement requirement = Assert.Single(policy.Requirements.OfType<PermissionRequirement>());
        Assert.Equal(Permissions.AuditExport, requirement.Permission);
    }

    [Fact]
    public async Task Returns_null_for_an_unknown_policy_name()
    {
        Assert.Null(await CreateProvider().GetPolicyAsync("some-other-policy"));
    }

    [Fact]
    public void Attribute_produces_the_policy_name_the_provider_understands()
    {
        HasPermissionAttribute attribute = new(Permissions.WorkflowsDesign);

        Assert.Equal(HasPermissionAttribute.PolicyPrefix + Permissions.WorkflowsDesign, attribute.Policy);
    }
}
