using DocOps.Application.Abstractions;
using DocOps.Application.Common;
using DocOps.Application.Workflows;
using DocOps.Domain.Auditing;
using DocOps.Domain.Common;
using DocOps.Domain.Documents;
using DocOps.Domain.Identity;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Documents;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Workflows;

public sealed class WorkflowService(
    DocOpsDbContext dbContext,
    WorkflowEngine engine,
    WorkflowDesignService designService,
    DocumentAccessResolver accessResolver,
    IAuditService auditService,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IWorkflowService
{
    public async Task<IReadOnlyList<WorkflowDefinitionSummaryDto>> GetDefinitionsAsync(
        bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        // Only the newest version of each process is listed. Older versions stay in the database so
        // their running instances resolve, but they are history, not choices.
        var latestVersions = dbContext.WorkflowDefinitions
            .GroupBy(d => d.Code)
            .Select(group => new { Code = group.Key, Version = group.Max(d => d.Version) });

        var definitions = await dbContext.WorkflowDefinitions
            .Where(d => latestVersions.Any(latest => latest.Code == d.Code && latest.Version == d.Version))
            .Where(d => includeInactive || d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new
            {
                d.Id,
                d.Code,
                d.Name,
                d.Description,
                d.Version,
                d.IsActive,
                d.Trigger,
                d.DocumentTypeId,
                DocumentTypeName = d.DocumentType != null ? d.DocumentType.Name : null,
                NodeCount = d.Nodes.Count,
                Running = dbContext.WorkflowInstances.Count(i =>
                    i.WorkflowDefinitionId == d.Id
                    && (i.Status == WorkflowInstanceStatus.Running || i.Status == WorkflowInstanceStatus.Waiting)),
                Completed = dbContext.WorkflowInstances.Count(i =>
                    i.WorkflowDefinitionId == d.Id && i.Status == WorkflowInstanceStatus.Completed),
                LastStarted = dbContext.WorkflowInstances
                    .Where(i => i.WorkflowDefinitionId == d.Id)
                    .Max(i => (DateTimeOffset?)i.StartedAtUtc)
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. definitions.Select(d => new WorkflowDefinitionSummaryDto
            {
                Id = d.Id,
                Code = d.Code,
                Name = d.Name,
                Description = d.Description,
                Version = d.Version,
                IsActive = d.IsActive,
                Trigger = d.Trigger,
                DocumentTypeId = d.DocumentTypeId,
                DocumentTypeName = d.DocumentTypeName,
                NodeCount = d.NodeCount,
                RunningInstances = d.Running,
                CompletedInstances = d.Completed,
                LastStartedAtUtc = d.LastStarted,
                IsLatestVersion = true,
                IsEditableInPlace = d.Running + d.Completed == 0
            })
        ];
    }

    public async Task<WorkflowDefinitionDetailDto> CreateAsync(
        CreateWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        WorkflowDefinition definition = await designService.CreateAsync(request, cancellationToken);

        return await BuildDefinitionDetailAsync(definition, cancellationToken);
    }

    public async Task<WorkflowDefinitionDetailDto> SaveAsync(
        Guid definitionId, SaveWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        WorkflowDefinition definition = await designService.SaveAsync(definitionId, request, cancellationToken);

        return await BuildDefinitionDetailAsync(definition, cancellationToken);
    }

    public async Task<WorkflowDefinitionDetailDto> DuplicateAsync(
        Guid definitionId, DuplicateWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        WorkflowDefinition copy = await designService.DuplicateAsync(definitionId, request, cancellationToken);

        return await BuildDefinitionDetailAsync(copy, cancellationToken);
    }

    public Task DeleteAsync(Guid definitionId, CancellationToken cancellationToken = default) =>
        designService.DeleteAsync(definitionId, cancellationToken);

    public async Task<WorkflowDefinitionDetailDto> GetDefinitionAsync(
        Guid definitionId, CancellationToken cancellationToken = default)
    {
        WorkflowDefinition definition = await LoadDefinitionAsync(definitionId, cancellationToken);

        return await BuildDefinitionDetailAsync(definition, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowValidationIssueDto>> ValidateDefinitionAsync(
        Guid definitionId, CancellationToken cancellationToken = default)
    {
        WorkflowDefinition definition = await LoadDefinitionAsync(definitionId, cancellationToken);

        return MapIssues(WorkflowDefinitionValidator.Validate(definition));
    }

    public async Task<WorkflowDefinitionDetailDto> SetActiveAsync(
        Guid definitionId, bool isActive, CancellationToken cancellationToken = default)
    {
        WorkflowDefinition definition = await LoadDefinitionAsync(definitionId, cancellationToken);

        if (isActive && !WorkflowDefinitionValidator.CanActivate(definition))
        {
            throw new DomainException(
                "O workflow tem erros de validação e não pode ser ativado.", "workflow_validation_failed");
        }

        definition.IsActive = isActive;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = isActive ? AuditActions.WorkflowDefinitionActivated : AuditActions.WorkflowDefinitionDeactivated,
                EntityType = AuditEntityTypes.WorkflowDefinition,
                EntityId = definition.Id,
                EntityDisplayName = definition.Name,
                Summary = isActive ? "Workflow ativado" : "Workflow desativado"
            },
            cancellationToken);

        return await BuildDefinitionDetailAsync(definition, cancellationToken);
    }

    public async Task<PagedResult<WorkflowInstanceListItemDto>> SearchInstancesAsync(
        WorkflowInstanceSearchRequest request, CancellationToken cancellationToken = default)
    {
        IQueryable<WorkflowInstance> query = await VisibleInstancesAsync(cancellationToken);

        if (request.DefinitionId is { } definitionId)
        {
            query = query.Where(i => i.WorkflowDefinitionId == definitionId);
        }

        if (request.Statuses.Count > 0)
        {
            WorkflowInstanceStatus[] statuses = [.. request.Statuses];
            query = query.Where(i => statuses.Contains(i.Status));
        }

        if (request.DocumentId is { } documentId)
        {
            query = query.Where(i => i.DocumentId == documentId);
        }

        if (request.OnlyMine && currentUser.UserId is { } userId)
        {
            query = query.Where(i =>
                i.StartedByUserId == userId || i.Tasks.Any(t => t.AssignedToUserId == userId));
        }

        long total = await query.LongCountAsync(cancellationToken);

        List<WorkflowInstanceListItemDto> items = await query
            .OrderByDescending(i => i.StartedAtUtc)

            // Desempate final: várias instâncias arrancam no mesmo instante quando um lote entra
            // de uma vez, e sem último critério a paginação pode repetir ou perder uma.
            .ThenBy(i => i.Id)
            .Skip(request.Paging.Skip)
            .Take(request.Paging.NormalizedPageSize)
            .Select(InstanceProjection(dbContext, clock.UtcNow))
            .ToListAsync(cancellationToken);

        return new WorkflowInstanceListResult(items, request.Paging, total).ToPagedResult();
    }

    public async Task<WorkflowInstanceDetailDto> GetInstanceAsync(
        Guid instanceId, CancellationToken cancellationToken = default)
    {
        WorkflowInstance instance = await LoadInstanceAsync(instanceId, cancellationToken);
        WorkflowDefinition definition = await LoadDefinitionAsync(instance.WorkflowDefinitionId, cancellationToken);

        return await BuildInstanceDetailAsync(instance, definition, cancellationToken);
    }

    public async Task<WorkflowInstanceDetailDto> StartAsync(
        StartWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        // Starting a process on a document is an action *on that document*: the caller must be able
        // to edit it, not merely to see it.
        if (request.DocumentId is { } documentId)
        {
            Document document = await dbContext.Documents
                .Include(d => d.Permissions)
                .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Document), documentId);

            await accessResolver.EnsureAsync(document, DocumentAccessLevel.Edit, clock.UtcNow, cancellationToken);
        }

        WorkflowInstance instance = await engine.StartAsync(
            request.DefinitionId, request.DocumentId, request.Variables, cancellationToken);

        WorkflowDefinition definition = await LoadDefinitionAsync(instance.WorkflowDefinitionId, cancellationToken);

        return await BuildInstanceDetailAsync(instance, definition, cancellationToken);
    }

    public async Task<WorkflowInstanceDetailDto> CancelAsync(
        Guid instanceId, CancelWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        WorkflowInstance instance = await LoadInstanceAsync(instanceId, cancellationToken);

        if (!CanCancel(instance))
        {
            throw new ForbiddenOperationException(
                "Só quem iniciou o processo, ou quem tem permissão de cancelamento, pode cancelá-lo.");
        }

        WorkflowInstance cancelled = await engine.CancelAsync(instanceId, request.Reason, cancellationToken);
        WorkflowDefinition definition = await LoadDefinitionAsync(cancelled.WorkflowDefinitionId, cancellationToken);

        return await BuildInstanceDetailAsync(cancelled, definition, cancellationToken);
    }

    // -----------------------------------------------------------------------------------------
    // Mapping
    // -----------------------------------------------------------------------------------------

    private async Task<WorkflowDefinitionDetailDto> BuildDefinitionDetailAsync(
        WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        (bool isLatest, bool editableInPlace) = await designService.GetEditStateAsync(definition, cancellationToken);
        IReadOnlyList<WorkflowDefinitionSummaryDto> summaries = await GetDefinitionsAsync(true, cancellationToken);

        WorkflowDefinitionSummaryDto summary = summaries.FirstOrDefault(s => s.Id == definition.Id)
            ?? new WorkflowDefinitionSummaryDto
            {
                Id = definition.Id,
                Code = definition.Code,
                Name = definition.Name,
                Description = definition.Description,
                Version = definition.Version,
                IsActive = definition.IsActive,
                Trigger = definition.Trigger,
                DocumentTypeId = definition.DocumentTypeId,
                NodeCount = definition.Nodes.Count,
                RunningInstances = 0,
                CompletedInstances = 0
            };

        summary = summary with { IsLatestVersion = isLatest, IsEditableInPlace = editableInPlace };

        return new WorkflowDefinitionDetailDto
        {
            Summary = summary,
            DocumentTypeId = definition.DocumentTypeId,
            Nodes = MapNodes(definition),
            Edges = MapEdges(definition),
            ConcurrencyToken = Convert.ToBase64String(definition.RowVersion),
            ValidationIssues = MapIssues(WorkflowDefinitionValidator.Validate(definition))
        };
    }

    private async Task<WorkflowInstanceDetailDto> BuildInstanceDetailAsync(
        WorkflowInstance instance, WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        WorkflowInstanceListItemDto summary = await dbContext.WorkflowInstances
            .Where(i => i.Id == instance.Id)
            .Select(InstanceProjection(dbContext, clock.UtcNow))
            .FirstAsync(cancellationToken);

        List<TaskListItemDto> tasks = await dbContext.WorkflowTasks
            .Where(t => t.WorkflowInstanceId == instance.Id)
            .OrderBy(t => t.AssignedAtUtc)
            .Select(TaskProjections.ToListItem(dbContext, clock.UtcNow))
            .ToListAsync(cancellationToken);

        List<WorkflowEventDto> timeline = await dbContext.WorkflowEvents
            .Where(e => e.WorkflowInstanceId == instance.Id)
            .OrderBy(e => e.OccurredAtUtc)
            .Select(e => new WorkflowEventDto(
                e.Id, e.OccurredAtUtc, e.EventType, e.Title, e.Detail, e.NodeKey, e.ActorDisplayName))
            .ToListAsync(cancellationToken);

        return new WorkflowInstanceDetailDto
        {
            Summary = summary,
            StatusReason = instance.StatusReason,
            ResumeAtUtc = instance.ResumeAtUtc,
            ActiveNodeKeys = WorkflowVariables.ParseNodeKeys(instance.ActiveNodeKeysJson),
            Variables = WorkflowVariables.Parse(instance.VariablesJson),
            Timeline = timeline,
            Tasks = tasks,
            Nodes = MapNodes(definition),
            Edges = MapEdges(definition),
            CanCancel = !instance.IsFinished && CanCancel(instance)
        };
    }

    private bool CanCancel(WorkflowInstance instance) =>
        currentUser.HasPermission(Permissions.WorkflowsCancel)
        || (instance.StartedByUserId is { } starter && starter == currentUser.UserId);

    private static IReadOnlyList<WorkflowNodeDto> MapNodes(WorkflowDefinition definition) =>
    [
        .. definition.Nodes
            .OrderBy(n => n.PositionY)
            .ThenBy(n => n.PositionX)
            .Select(n => new WorkflowNodeDto(
                n.Id, n.Key, n.Name, n.Description, n.Type, n.ConfigJson, n.PositionX, n.PositionY))
    ];

    private static IReadOnlyList<WorkflowEdgeDto> MapEdges(WorkflowDefinition definition) =>
    [
        .. definition.Edges.Select(e => new WorkflowEdgeDto(e.Id, e.FromNodeKey, e.ToNodeKey, e.Label, e.Branch))
    ];

    private static IReadOnlyList<WorkflowValidationIssueDto> MapIssues(
        IReadOnlyList<WorkflowValidationIssue> issues) =>
    [
        .. issues.Select(issue => new WorkflowValidationIssueDto(
            issue.Severity.ToString(), issue.Code, issue.Message, issue.NodeKey))
    ];

    internal static System.Linq.Expressions.Expression<Func<WorkflowInstance, WorkflowInstanceListItemDto>>
        InstanceProjection(DocOpsDbContext db, DateTimeOffset now) =>
        instance => new WorkflowInstanceListItemDto
        {
            Id = instance.Id,
            Reference = instance.Reference,
            DefinitionId = instance.WorkflowDefinitionId,
            DefinitionName = instance.Definition!.Name,
            Status = instance.Status,
            DocumentId = instance.DocumentId,
            DocumentReference = instance.Document != null ? instance.Document.Reference : null,
            DocumentTitle = instance.Document != null ? instance.Document.Title : null,
            StartedAtUtc = instance.StartedAtUtc,
            CompletedAtUtc = instance.CompletedAtUtc,
            StartedByName = db.Users
                .Where(u => u.Id == instance.StartedByUserId)
                .Select(u => u.FirstName + " " + u.LastName)
                .FirstOrDefault(),
            OpenTaskCount = instance.Tasks.Count(t =>
                t.Status == WorkflowTaskStatus.Pending || t.Status == WorkflowTaskStatus.InProgress),
            OverdueTaskCount = instance.Tasks.Count(t =>
                (t.Status == WorkflowTaskStatus.Pending || t.Status == WorkflowTaskStatus.InProgress)
                && t.DueAtUtc != null && t.DueAtUtc < now),
            CurrentStepName = instance.Tasks
                .Where(t => t.Status == WorkflowTaskStatus.Pending || t.Status == WorkflowTaskStatus.InProgress)
                .OrderBy(t => t.AssignedAtUtc)
                .Select(t => t.Title)
                .FirstOrDefault(),
            Outcome = instance.Outcome,
            ElapsedHours = (double)EF.Functions.DateDiffMinute(
                instance.StartedAtUtc, instance.CompletedAtUtc ?? now) / 60d
        };

    private async Task<WorkflowDefinition> LoadDefinitionAsync(Guid definitionId, CancellationToken cancellationToken) =>
        await dbContext.WorkflowDefinitions
            .Include(d => d.Nodes)
            .Include(d => d.Edges)
            .FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(WorkflowDefinition), definitionId);

    private async Task<WorkflowInstance> LoadInstanceAsync(Guid instanceId, CancellationToken cancellationToken) =>
        await (await VisibleInstancesAsync(cancellationToken))
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(WorkflowInstance), instanceId);

    /// <summary>Hidden instances answer 404, as the document list would. See <see cref="WorkflowVisibility"/>.</summary>
    private async Task<IQueryable<WorkflowInstance>> VisibleInstancesAsync(CancellationToken cancellationToken) =>
        WorkflowVisibility.VisibleInstances(
            dbContext, await accessResolver.GetSubjectAsync(cancellationToken), clock.UtcNow);

    private sealed record WorkflowInstanceListResult(
        IReadOnlyList<WorkflowInstanceListItemDto> Items, PageRequest Paging, long Total)
    {
        public PagedResult<WorkflowInstanceListItemDto> ToPagedResult() =>
            new(Items, Paging.NormalizedPage, Paging.NormalizedPageSize, Total);
    }
}
