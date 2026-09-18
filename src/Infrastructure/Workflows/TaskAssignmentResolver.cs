using DocOps.Domain.Common;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Workflows;

/// <summary>Who a task goes to, resolved at the moment the instance reaches the node.</summary>
public sealed record ResolvedAssignment(
    AssignmentStrategy Strategy,
    Guid? UserId,
    Guid? DepartmentId,
    string? Role,
    string DisplayName);

/// <summary>
/// Turns a node's assignment configuration into a concrete assignee. Department and role assignments
/// stay unassigned on purpose: the task belongs to the group until someone claims it by acting on it.
/// </summary>
public sealed class TaskAssignmentResolver(DocOpsDbContext dbContext)
{
    public async Task<ResolvedAssignment> ResolveAsync(
        AssignmentConfiguration? configuration,
        WorkflowInstance instance,
        CancellationToken cancellationToken = default)
    {
        AssignmentConfiguration config = configuration ?? new AssignmentConfiguration();

        return config.Strategy switch
        {
            AssignmentStrategy.User => await ResolveUserAsync(config.UserId, cancellationToken),
            AssignmentStrategy.Department => await ResolveDepartmentAsync(config.DepartmentCode, instance.TenantId, cancellationToken),
            AssignmentStrategy.Role => ResolveRole(config.Role),
            AssignmentStrategy.DocumentOwner => await ResolveDocumentOwnerAsync(instance, cancellationToken),
            AssignmentStrategy.DepartmentManager => await ResolveDepartmentManagerAsync(instance, cancellationToken),
            _ => throw new DomainException($"Estratégia de atribuição '{config.Strategy}' desconhecida.", "unknown_assignment_strategy")
        };
    }

    public async Task<ResolvedAssignment> ForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await ResolveUserAsync(userId, cancellationToken);

    private async Task<ResolvedAssignment> ResolveUserAsync(Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is not { } id)
        {
            throw new DomainException("O nó não indica o utilizador a quem atribuir a tarefa.", "assignee_not_configured");
        }

        var user = await dbContext.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == id && !u.IsDeleted)
            .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("User", id);

        return new ResolvedAssignment(AssignmentStrategy.User, user.Id, null, null, user.Name);
    }

    private async Task<ResolvedAssignment> ResolveDepartmentAsync(
        string? departmentCode, Guid tenantId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(departmentCode))
        {
            throw new DomainException("O nó não indica o departamento a quem atribuir a tarefa.", "assignee_not_configured");
        }

        var department = await dbContext.Departments
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && d.Code == departmentCode && !d.IsDeleted)
            .Select(d => new { d.Id, d.Name })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("Department", departmentCode);

        return new ResolvedAssignment(AssignmentStrategy.Department, null, department.Id, null, department.Name);
    }

    private static ResolvedAssignment ResolveRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new DomainException("O nó não indica o perfil a quem atribuir a tarefa.", "assignee_not_configured");
        }

        return new ResolvedAssignment(AssignmentStrategy.Role, null, null, role, role);
    }

    private async Task<ResolvedAssignment> ResolveDocumentOwnerAsync(
        WorkflowInstance instance, CancellationToken cancellationToken)
    {
        if (instance.DocumentId is not { } documentId)
        {
            throw new DomainException(
                "A instância não está ligada a um documento, pelo que não tem proprietário.", "document_not_linked");
        }

        Guid ownerId = await dbContext.Documents
            .IgnoreQueryFilters()
            .Where(d => d.Id == documentId)
            .Select(d => d.OwnerUserId)
            .FirstOrDefaultAsync(cancellationToken);

        return await ResolveUserAsync(ownerId, cancellationToken);
    }

    /// <summary>Falls back to the department itself when it has no head, so work is never stranded.</summary>
    private async Task<ResolvedAssignment> ResolveDepartmentManagerAsync(
        WorkflowInstance instance, CancellationToken cancellationToken)
    {
        if (instance.DocumentId is not { } documentId)
        {
            throw new DomainException("A instância não está ligada a um documento.", "document_not_linked");
        }

        var department = await dbContext.Documents
            .IgnoreQueryFilters()
            .Where(d => d.Id == documentId && d.DepartmentId != null)
            .Select(d => new { d.Department!.Id, d.Department.Name, d.Department.ManagerUserId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(
                "O documento não tem departamento responsável.", "document_department_missing");

        if (department.ManagerUserId is { } managerId)
        {
            return await ResolveUserAsync(managerId, cancellationToken);
        }

        return new ResolvedAssignment(AssignmentStrategy.Department, null, department.Id, null, department.Name);
    }
}
