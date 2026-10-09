using DocOps.Application.Abstractions;
using DocOps.Domain.Identity;
using DocOps.Infrastructure.Persistence;
using DocOps.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DocOps.IntegrationTests;

/// <summary>
/// Creates a <see cref="DocOpsDbContext"/> that acts on behalf of a chosen tenant and user, with the
/// production interceptor chain attached. Used to test persistence-level guarantees that cannot be
/// reached through the HTTP surface alone.
/// </summary>
public static class TenantScopedContext
{
    public static DocOpsDbContext Create(
        ISystemScope scope, string connectionString, Guid tenantId, Guid? actingUserId = null)
    {
        FixedPrincipal principal = new(tenantId, actingUserId);
        IDateTimeProvider clock = scope.Services.GetRequiredService<IDateTimeProvider>();
        ILoggerFactory loggerFactory = scope.Services.GetRequiredService<ILoggerFactory>();

        DbContextOptions<DocOpsDbContext> options = new DbContextOptionsBuilder<DocOpsDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(
                // Sem este, a organização nunca chega ao contexto da sessão e a política de
                // segurança ao nível da linha nega tudo.
                new TenantSessionContextInterceptor(principal),
                new AuditableEntityInterceptor(principal, clock),
                new TenantGuardInterceptor(principal, loggerFactory.CreateLogger<TenantGuardInterceptor>()),
                new SoftDeleteInterceptor(principal, clock),
                new AuditImmutabilityInterceptor())
            .Options;

        return new DocOpsDbContext(options, principal);
    }

    private sealed class FixedPrincipal(Guid tenantId, Guid? userId) : ICurrentUser, ITenantContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = userId;
        public Guid? TenantId { get; } = tenantId;
        public string? Email => "integration-test@docops.local";
        public string? DisplayName => "Integration Test";
        public UserType UserType => UserType.Internal;
        public bool IsPlatformAdministrator => false;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];

        public bool HasPermission(string permission) => false;
        public bool IsInRole(string role) => false;

        Guid ITenantContext.TenantId => tenantId;
        public Guid? TenantIdOrNull => tenantId;
        public bool HasTenant => true;
    }
}
