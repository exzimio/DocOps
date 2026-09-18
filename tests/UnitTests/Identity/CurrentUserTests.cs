using System.Security.Claims;
using DocOps.Application.Abstractions;
using DocOps.Domain.Identity;
using DocOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;

namespace DocOps.UnitTests.Identity;

public class CurrentUserTests
{
    private static readonly Guid UserId = Guid.Parse("0192d3a0-1111-7000-8000-000000000001");
    private static readonly Guid TenantId = Guid.Parse("0192d3a0-2222-7000-8000-000000000002");

    private static CurrentUser CreateCurrentUser(params Claim[] claims)
    {
        DefaultHttpContext httpContext = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: claims.Length > 0 ? "TestCookie" : null))
        };

        return new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });
    }

    private static Claim[] AuthenticatedClaims() =>
    [
        new(ClaimTypes.NameIdentifier, UserId.ToString()),
        new(ClaimTypes.Email, "sofia.martins@asteria.example"),
        new(ClaimTypes.GivenName, "Sofia Martins"),
        new(DocOpsClaimTypes.TenantId, TenantId.ToString()),
        new(DocOpsClaimTypes.UserType, nameof(UserType.Internal)),
        new(ClaimTypes.Role, RoleNames.Manager),
        new(DocOpsClaimTypes.Permission, Permissions.DocumentsView),
        new(DocOpsClaimTypes.Permission, Permissions.TasksReassign)
    ];

    [Fact]
    public void Reads_identity_tenant_and_permissions_from_the_principal()
    {
        CurrentUser currentUser = CreateCurrentUser(AuthenticatedClaims());

        Assert.True(currentUser.IsAuthenticated);
        Assert.Equal(UserId, currentUser.UserId);
        Assert.Equal(TenantId, currentUser.TenantId);
        Assert.Equal("sofia.martins@asteria.example", currentUser.Email);
        Assert.Equal("Sofia Martins", currentUser.DisplayName);
        Assert.Equal(UserType.Internal, currentUser.UserType);
        Assert.True(currentUser.IsInRole(RoleNames.Manager));
        Assert.True(currentUser.HasPermission(Permissions.TasksReassign));
        Assert.False(currentUser.HasPermission(Permissions.AdminTenants));
        Assert.Equal(2, currentUser.Permissions.Count);
    }

    [Fact]
    public void Reports_nothing_for_an_anonymous_request()
    {
        CurrentUser currentUser = CreateCurrentUser();

        Assert.False(currentUser.IsAuthenticated);
        Assert.Null(currentUser.UserId);
        Assert.Null(currentUser.TenantId);
        Assert.Empty(currentUser.Permissions);
        Assert.False(currentUser.HasPermission(Permissions.DocumentsView));
    }

    [Fact]
    public void Platform_administrator_flag_defaults_to_false_when_the_claim_is_absent()
    {
        Assert.False(CreateCurrentUser(AuthenticatedClaims()).IsPlatformAdministrator);

        CurrentUser platformAdmin = CreateCurrentUser(
            [.. AuthenticatedClaims(), new Claim(DocOpsClaimTypes.PlatformAdministrator, bool.TrueString)]);

        Assert.True(platformAdmin.IsPlatformAdministrator);
    }

    [Fact]
    public void Tenant_context_exposes_the_tenant_of_the_principal()
    {
        ITenantContext tenantContext = new TenantContext(CreateCurrentUser(AuthenticatedClaims()));

        Assert.True(tenantContext.HasTenant);
        Assert.Equal(TenantId, tenantContext.TenantId);
        Assert.Equal(TenantId, tenantContext.TenantIdOrNull);
    }

    [Fact]
    public void Tenant_context_refuses_to_invent_a_tenant_for_an_anonymous_caller()
    {
        ITenantContext tenantContext = new TenantContext(CreateCurrentUser());

        Assert.False(tenantContext.HasTenant);
        Assert.Null(tenantContext.TenantIdOrNull);
        Assert.Throws<InvalidOperationException>(() => tenantContext.TenantId);
    }

    [Fact]
    public void Ignores_a_malformed_tenant_claim_instead_of_trusting_it()
    {
        CurrentUser currentUser = CreateCurrentUser(
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
            new Claim(DocOpsClaimTypes.TenantId, "not-a-guid"));

        Assert.Null(currentUser.TenantId);
    }
}
