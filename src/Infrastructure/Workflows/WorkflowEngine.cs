using DocOps.Application.Abstractions;
using DocOps.Domain.Auditing;
using DocOps.Domain.Common;
using DocOps.Domain.Notifications;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DocOps.Infrastructure.Observability;

namespace DocOps.Infrastructure.Workflows;

/// <summary>
/// Walks a workflow instance from node to node.
///
/// One walk is one transaction: the engine executes nodes until every active branch is either parked
/// (a person has to act, a timer has to elapse) or finished, and only then persists. That keeps the
/// instance, its tasks, its timeline and any document status change consistent with each other — a
/// crash mid-walk leaves the instance exactly where it was.
/// </summary>
public sealed class WorkflowEngine(
    DocOpsDbContext dbContext,
    IEnumerable<INodeExecutor> executors,
    DocumentStatusEffect documentStatusEffect,
    IAuditService auditService,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    DocOpsMetrics metrics,
    ILogger<WorkflowEngine> logger)
{
    /// <summary>Guards against a definition whose edges form a cycle with no waiting node in it.</summary>
    private const int MaxNodeVisitsPerWalk = 200;

    private readonly Dictionary<WorkflowNodeType, INodeExecutor> _executors =
        executors.ToDictionary(executor => executor.NodeType);

    public async Task<WorkflowInstance> StartAsync(
        Guid definitionId,
        Guid? documentId,
        IReadOnlyDictionary<string, string?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        WorkflowDefinition definition = await LoadDefinitionAsync(definitionId, cancellationToken);

        if (!definition.IsActive)
        {
            throw new DomainException(
                $"O workflow '{definition.Name}' não está ativo.", "workflow_not_active");
        }

        WorkflowNode start = definition.Nodes.FirstOrDefault(n => n.Type == WorkflowNodeType.Start)
            ?? throw new DomainException(
                $"O workflow '{definition.Name}' não tem nó de início.", "workflow_without_start");

        Dictionary<string, string?> instanceVariables = await BuildInitialVariablesAsync(documentId, variables, cancellationToken);

        WorkflowInstance instance = new()
        {
            TenantId = definition.TenantId,
            WorkflowDefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            DocumentId = documentId,
            Reference = await NextReferenceAsync(definition.TenantId, cancellationToken),
            Status = WorkflowInstanceStatus.Running,
            StartedByUserId = currentUser.UserId,
            StartedAtUtc = clock.UtcNow,
            ActiveNodeKeysJson = WorkflowVariables.ToNodeKeysJson([start.Key]),
            VariablesJson = WorkflowVariables.ToJson(instanceVariables)
        };

        metrics.WorkflowInstance("started");

        dbContext.WorkflowInstances.Add(instance);

        WorkflowExecutionContext context = CreateContext(instance, definition, instanceVariables);
        context.AddEvent(WorkflowEventTypes.InstanceStarted, $"Processo '{definition.Name}' iniciado", start.Key);

        await WalkAsync(context, [start.Key], cancellationToken);
        await PersistAsync(context, cancellationToken);

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = AuditActions.WorkflowInstanceStarted,
                EntityType = AuditEntityTypes.WorkflowInstance,
                EntityId = instance.Id,
                EntityDisplayName = $"{instance.Reference} — {definition.Name}",
                Summary = $"Processo iniciado sobre {instanceVariables.GetValueOrDefault(WorkflowVariables.DocumentReference) ?? "nenhum documento"}",
                After = new { instance.Reference, Definition = definition.Code, DocumentId = documentId }
            },
            cancellationToken);

        logger.LogInformation(
            "Started workflow {Reference} ({Definition}) on document {DocumentId}",
            instance.Reference, definition.Code, documentId);

        return instance;
    }

    /// <summary>
    /// Closes a task and resumes the instance from its node. The decision becomes the branch the
    /// engine follows, so an approval and a rejection can lead to completely different paths.
    /// </summary>
    public async Task<WorkflowInstance> CompleteTaskAsync(
        WorkflowTask task,
        WorkflowTaskDecision decision,
        string? comment,
        Guid actingUserId,
        string actingUserDisplayName,
        CancellationToken cancellationToken = default)
    {
        WorkflowInstance instance = await LoadInstanceAsync(task.WorkflowInstanceId, cancellationToken);
        WorkflowDefinition definition = await LoadDefinitionAsync(instance.WorkflowDefinitionId, cancellationToken);

        if (instance.IsFinished)
        {
            throw new DomainException(
                $"O processo {instance.Reference} já terminou.", "workflow_already_finished");
        }

        WorkflowTask tracked = instance.Tasks.First(t => t.Id == task.Id);

        if (!tracked.IsOpen)
        {
            throw new DomainException("Esta tarefa já foi concluída.", "task_already_closed");
        }

        DateTimeOffset now = clock.UtcNow;

        tracked.Status = decision == WorkflowTaskDecision.Rejected
            ? WorkflowTaskStatus.Rejected
            : WorkflowTaskStatus.Completed;
        tracked.Decision = decision;
        tracked.DecisionComment = comment;
        tracked.CompletedAtUtc = now;
        tracked.CompletedByUserId = actingUserId;

        // Acting on a group task claims it: the record must show who actually decided.
        tracked.AssignedToUserId ??= actingUserId;
        tracked.AssigneeDisplayName = actingUserDisplayName;

        Dictionary<string, string?> variables = WorkflowVariables.Parse(instance.VariablesJson);
        variables[WorkflowVariables.Decision] = decision.ToString().ToLowerInvariant();
        variables[$"decision:{tracked.NodeKey}"] = decision.ToString().ToLowerInvariant();

        WorkflowExecutionContext context = CreateContext(instance, definition, variables, actingUserId, actingUserDisplayName);

        context.AddEvent(
            WorkflowEventTypes.TaskCompleted,
            $"Tarefa '{tracked.Title}' concluída por {actingUserDisplayName} ({DecisionLabel(decision)})",
            tracked.NodeKey,
            comment);

        // "Information requested" keeps the step open: the process must not advance on a question.
        if (decision == WorkflowTaskDecision.InformationRequested)
        {
            tracked.Status = WorkflowTaskStatus.InProgress;
            tracked.CompletedAtUtc = null;

            // The question gets its own timeline entry. The task's decision comment only ever holds
            // the latest one, and the external portal has to be able to show what was asked even
            // after a second question replaces it.
            context.AddEvent(
                WorkflowEventTypes.InformationRequested,
                $"Informação solicitada em '{tracked.Title}'",
                tracked.NodeKey,
                comment);

            await PersistAsync(context, cancellationToken);
            await AuditTaskAsync(tracked, instance, decision, comment, cancellationToken);

            return instance;
        }

        string branch = decision == WorkflowTaskDecision.Rejected ? "rejected" : "approved";
        List<string> nextKeys = ResolveNextNodes(definition, tracked.NodeKey, branch, out bool hasBranchEdges);

        List<string> active = [.. WorkflowVariables.ParseNodeKeys(instance.ActiveNodeKeysJson)
            .Where(key => !string.Equals(key, tracked.NodeKey, StringComparison.Ordinal))];

        if (decision == WorkflowTaskDecision.Rejected && !hasBranchEdges)
        {
            // A rejection with nowhere to go ends the process — that is the outcome, not a failure.
            await CancelRemainingTasksAsync(instance, "Processo rejeitado", now);
            instance.Status = WorkflowInstanceStatus.Rejected;
            instance.CompletedAtUtc = now;
            instance.Outcome = "Rejeitado";
            instance.ActiveNodeKeysJson = WorkflowVariables.ToNodeKeysJson([]);

            context.AddEvent(WorkflowEventTypes.InstanceRejected, "Processo rejeitado", tracked.NodeKey, comment);
            await PersistAsync(context, cancellationToken);
            await AuditTaskAsync(tracked, instance, decision, comment, cancellationToken);

            return instance;
        }

        instance.ActiveNodeKeysJson = WorkflowVariables.ToNodeKeysJson(active);

        await WalkAsync(context, nextKeys, cancellationToken);
        await PersistAsync(context, cancellationToken);
        await AuditTaskAsync(tracked, instance, decision, comment, cancellationToken);

        return instance;
    }

    /// <summary>Resumes an instance parked on a timer. Called by the background worker.</summary>
    public async Task<WorkflowInstance> ResumeTimerAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        WorkflowInstance instance = await LoadInstanceAsync(instanceId, cancellationToken);
        WorkflowDefinition definition = await LoadDefinitionAsync(instance.WorkflowDefinitionId, cancellationToken);

        if (instance.IsFinished || instance.ResumeAtUtc is not { } resumeAt || resumeAt > clock.UtcNow)
        {
            return instance;
        }

        Dictionary<string, string?> variables = WorkflowVariables.Parse(instance.VariablesJson);
        WorkflowExecutionContext context = CreateContext(instance, definition, variables, null, "Sistema");

        string[] active = WorkflowVariables.ParseNodeKeys(instance.ActiveNodeKeysJson);

        await WalkAsync(context, active, cancellationToken);
        await PersistAsync(context, cancellationToken);

        return instance;
    }

    /// <summary>
    /// Walks a waiting instance again from its active nodes, after something outside a person's action
    /// completed — an AI analysis the worker finished, for instance. Each active node decides for
    /// itself whether it is done: a node whose work is not finished simply waits again.
    /// </summary>
    public async Task<WorkflowInstance> ResumeAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        WorkflowInstance instance = await LoadInstanceAsync(instanceId, cancellationToken);

        if (instance.IsFinished)
        {
            return instance;
        }

        WorkflowDefinition definition = await LoadDefinitionAsync(instance.WorkflowDefinitionId, cancellationToken);
        Dictionary<string, string?> variables = WorkflowVariables.Parse(instance.VariablesJson);
        WorkflowExecutionContext context = CreateContext(instance, definition, variables, null, "Sistema");

        await WalkAsync(context, WorkflowVariables.ParseNodeKeys(instance.ActiveNodeKeysJson), cancellationToken);
        await PersistAsync(context, cancellationToken);

        return instance;
    }

    public async Task<WorkflowInstance> CancelAsync(
        Guid instanceId, string reason, CancellationToken cancellationToken = default)
    {
        WorkflowInstance instance = await LoadInstanceAsync(instanceId, cancellationToken);
        WorkflowDefinition definition = await LoadDefinitionAsync(instance.WorkflowDefinitionId, cancellationToken);

        if (instance.IsFinished)
        {
            throw new DomainException($"O processo {instance.Reference} já terminou.", "workflow_already_finished");
        }

        DateTimeOffset now = clock.UtcNow;
        Dictionary<string, string?> variables = WorkflowVariables.Parse(instance.VariablesJson);
        WorkflowExecutionContext context = CreateContext(instance, definition, variables);

        await CancelRemainingTasksAsync(instance, reason, now);

        instance.Status = WorkflowInstanceStatus.Cancelled;
        instance.CompletedAtUtc = now;
        instance.StatusReason = reason;
        instance.ActiveNodeKeysJson = WorkflowVariables.ToNodeKeysJson([]);

        context.AddEvent(WorkflowEventTypes.InstanceCancelled, "Processo cancelado", detail: reason);

        await PersistAsync(context, cancellationToken);

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = AuditActions.WorkflowInstanceCancelled,
                EntityType = AuditEntityTypes.WorkflowInstance,
                EntityId = instance.Id,
                EntityDisplayName = instance.Reference,
                Severity = AuditSeverity.Warning,
                Summary = reason
            },
            cancellationToken);

        return instance;
    }

    // -----------------------------------------------------------------------------------------
    // Execution core
    // -----------------------------------------------------------------------------------------

    private async Task WalkAsync(
        WorkflowExecutionContext context, IEnumerable<string> entryNodes, CancellationToken cancellationToken)
    {
        Queue<string> pending = new(entryNodes);
        List<string> waiting = [.. WorkflowVariables.ParseNodeKeys(context.Instance.ActiveNodeKeysJson)
            .Where(key => !pending.Contains(key))];

        int visits = 0;

        while (pending.Count > 0)
        {
            if (++visits > MaxNodeVisitsPerWalk)
            {
                Fail(context, $"Execução interrompida após {MaxNodeVisitsPerWalk} nós: possível ciclo na definição.");
                return;
            }

            string nodeKey = pending.Dequeue();
            WorkflowNode? node = context.Definition.FindNode(nodeKey);

            if (node is null)
            {
                Fail(context, $"O nó '{nodeKey}' não existe na definição.");
                return;
            }

            if (!_executors.TryGetValue(node.Type, out INodeExecutor? executor))
            {
                Fail(context, $"Não existe executor para o tipo de nó '{node.Type}'.");
                return;
            }

            NodeResult result;

            try
            {
                result = await executor.ExecuteAsync(context, node, cancellationToken);
                await documentStatusEffect.ApplyAsync(context, node, cancellationToken);
                metrics.WorkflowStep(node.Type.ToString(), succeeded: true);
            }
            catch (DomainException exception)
            {
                // Contado antes de terminar o processo: é o passo que falhou, e é por tipo de nó
                // que se descobre qual deles falha sempre.
                metrics.WorkflowStep(node.Type.ToString(), succeeded: false);
                Fail(context, $"Nó '{node.Key}': {exception.Message}");
                return;
            }

            switch (result.Kind)
            {
                case NodeResultKind.Wait:
                    waiting.Add(node.Key);
                    break;

                case NodeResultKind.Finish:
                    Finish(context, result);
                    return;

                default:
                    List<string> next = ResolveNextNodes(context.Definition, node.Key, result.Branch, out bool hasEdges);

                    if (!hasEdges)
                    {
                        Fail(context, $"O nó '{node.Key}' não tem ligação de saída para o ramo '{result.Branch ?? "predefinido"}'.");
                        return;
                    }

                    foreach (string key in next)
                    {
                        pending.Enqueue(key);
                    }

                    break;
            }
        }

        context.Instance.ActiveNodeKeysJson = WorkflowVariables.ToNodeKeysJson(waiting);
        context.Instance.Status = waiting.Count > 0 ? WorkflowInstanceStatus.Waiting : WorkflowInstanceStatus.Running;
        context.Instance.VariablesJson = WorkflowVariables.ToJson(context.Variables);
    }

    /// <summary>
    /// Picks the outgoing edges to follow. An edge without a branch is unconditional; when the node
    /// produced a branch, only edges labelled with it are taken.
    /// </summary>
    private static List<string> ResolveNextNodes(
        WorkflowDefinition definition, string nodeKey, string? branch, out bool hasEdges)
    {
        List<WorkflowEdge> outgoing = [.. definition.OutgoingEdges(nodeKey)];

        // With no branch chosen, only unconditional edges are followed. A branch edge is followed
        // only when its branch is chosen: otherwise a step with a normal exit and a "failed" exit
        // would take both, and the process would end wherever the database happened to list first.
        List<WorkflowEdge> matching = branch is null
            ? [.. outgoing.Where(edge => string.IsNullOrWhiteSpace(edge.Branch))]
            : [.. outgoing.Where(edge => string.Equals(edge.Branch, branch, StringComparison.OrdinalIgnoreCase))];

        if (matching.Count == 0 && branch is not null)
        {
            // Fall back to unconditional edges so a definition need not spell out every branch.
            matching = [.. outgoing.Where(edge => string.IsNullOrWhiteSpace(edge.Branch))];
        }

        hasEdges = matching.Count > 0;

        return [.. matching.Select(edge => edge.ToNodeKey)];
    }

    private void Finish(WorkflowExecutionContext context, NodeResult result)
    {
        WorkflowInstance instance = context.Instance;

        instance.Status = result.EndsAsRejected ? WorkflowInstanceStatus.Rejected : WorkflowInstanceStatus.Completed;

        // Contado onde o estado é decidido, e não onde é gravado: gravar pode falhar, e um processo
        // que não chegou a terminar não deve aparecer como terminado em lado nenhum.
        metrics.WorkflowInstance(result.EndsAsRejected ? "rejected" : "completed");
        instance.CompletedAtUtc = context.Now;
        instance.Outcome = result.Outcome;
        instance.ActiveNodeKeysJson = WorkflowVariables.ToNodeKeysJson([]);
        instance.VariablesJson = WorkflowVariables.ToJson(context.Variables);

        context.AddEvent(
            result.EndsAsRejected ? WorkflowEventTypes.InstanceRejected : WorkflowEventTypes.InstanceCompleted,
            result.EndsAsRejected ? "Processo terminado com rejeição" : "Processo concluído",
            detail: result.Outcome);
    }

    private void Fail(WorkflowExecutionContext context, string reason)
    {
        WorkflowInstance instance = context.Instance;

        instance.Status = WorkflowInstanceStatus.Failed;
        instance.StatusReason = reason;

        metrics.WorkflowInstance("failed");
        instance.CompletedAtUtc = context.Now;
        instance.VariablesJson = WorkflowVariables.ToJson(context.Variables);

        context.AddEvent(WorkflowEventTypes.InstanceFailed, "Execução interrompida", detail: reason);

        logger.LogError("Workflow {Reference} failed: {Reason}", instance.Reference, reason);
    }

    private async Task PersistAsync(WorkflowExecutionContext context, CancellationToken cancellationToken)
    {
        context.Instance.VariablesJson = WorkflowVariables.ToJson(context.Variables);

        await dbContext.SaveChangesAsync(cancellationToken);
        await NotifyAssigneesAsync(context, cancellationToken);
    }

    /// <summary>Tells the people a new task landed on that it is waiting for them.</summary>
    private async Task NotifyAssigneesAsync(WorkflowExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.CreatedTasks.Count == 0)
        {
            return;
        }

        foreach (WorkflowTask task in context.CreatedTasks)
        {
            Guid[] recipients = await ResolveTaskRecipientsAsync(task, cancellationToken);

            foreach (Guid recipient in recipients)
            {
                dbContext.Notifications.Add(new Notification
                {
                    TenantId = task.TenantId,
                    RecipientUserId = recipient,
                    Kind = NotificationKind.TaskAssigned,
                    Title = task.Title,
                    Body = task.DueAtUtc is { } due
                        ? $"Prazo: {due:dd/MM/yyyy HH:mm}"
                        : "Sem prazo definido",
                    Link = "/app/tasks",
                    WorkflowInstanceId = task.WorkflowInstanceId,
                    WorkflowTaskId = task.Id,
                    DocumentId = context.Instance.DocumentId,
                    CreatedAtUtc = context.Now
                });
            }
        }

        context.CreatedTasks.Clear();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Guid[]> ResolveTaskRecipientsAsync(WorkflowTask task, CancellationToken cancellationToken)
    {
        if (task.AssignedToUserId is { } userId)
        {
            return [userId];
        }

        if (task.AssignedToDepartmentId is { } departmentId)
        {
            return [.. await dbContext.UserDepartments
                .IgnoreQueryFilters()
                .Where(ud => ud.DepartmentId == departmentId && ud.User!.IsActive && !ud.User.IsDeleted)
                .Select(ud => ud.UserId)
                .ToListAsync(cancellationToken)];
        }

        if (task.AssignedToRole is { } role)
        {
            return [.. await (
                from userRole in dbContext.UserRoles
                join identityRole in dbContext.Roles on userRole.RoleId equals identityRole.Id
                join user in dbContext.Users.IgnoreQueryFilters() on userRole.UserId equals user.Id
                where identityRole.Name == role && user.TenantId == task.TenantId && user.IsActive && !user.IsDeleted
                select user.Id).Distinct().ToListAsync(cancellationToken)];
        }

        return [];
    }

    private static async Task CancelRemainingTasksAsync(WorkflowInstance instance, string reason, DateTimeOffset now)
    {
        foreach (WorkflowTask task in instance.Tasks.Where(t => t.IsOpen))
        {
            task.Status = WorkflowTaskStatus.Cancelled;
            task.CompletedAtUtc = now;
            task.DecisionComment = reason;
        }

        await Task.CompletedTask;
    }

    private WorkflowExecutionContext CreateContext(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        Dictionary<string, string?> variables,
        Guid? actorUserId = null,
        string? actorDisplayName = null) =>
        new()
        {
            Instance = instance,
            Definition = definition,
            Variables = variables,
            Now = clock.UtcNow,
            ActorUserId = actorUserId ?? currentUser.UserId,
            ActorDisplayName = actorDisplayName ?? currentUser.DisplayName ?? "Sistema"
        };

    private async Task<WorkflowDefinition> LoadDefinitionAsync(Guid definitionId, CancellationToken cancellationToken) =>
        await dbContext.WorkflowDefinitions
            .IgnoreQueryFilters()
            .Include(d => d.Nodes)
            .Include(d => d.Edges)
            .FirstOrDefaultAsync(d => d.Id == definitionId && !d.IsDeleted, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(WorkflowDefinition), definitionId);

    private async Task<WorkflowInstance> LoadInstanceAsync(Guid instanceId, CancellationToken cancellationToken) =>
        await dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .Include(i => i.Tasks)
            .Include(i => i.Events)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(WorkflowInstance), instanceId);

    private async Task<Dictionary<string, string?>> BuildInitialVariablesAsync(
        Guid? documentId, IReadOnlyDictionary<string, string?>? provided, CancellationToken cancellationToken)
    {
        Dictionary<string, string?> variables = new(StringComparer.OrdinalIgnoreCase);

        if (documentId is { } id)
        {
            var document = await dbContext.Documents
                .IgnoreQueryFilters()
                .Where(d => d.Id == id)
                .Select(d => new
                {
                    d.Reference,
                    d.Title,
                    TypeCode = d.DocumentType!.Code,
                    DepartmentCode = d.Department != null ? d.Department.Code : null,
                    Metadata = d.MetadataValues.Select(v => new { v.Field!.Code, v.TextValue }).ToList()
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (document is not null)
            {
                variables[WorkflowVariables.DocumentReference] = document.Reference;
                variables[WorkflowVariables.DocumentTitle] = document.Title;
                variables[WorkflowVariables.DocumentType] = document.TypeCode;
                variables[WorkflowVariables.DepartmentCode] = document.DepartmentCode;

                // Document metadata becomes routing input: a condition can branch on the invoice total.
                foreach (var value in document.Metadata)
                {
                    variables[value.Code] = value.TextValue;
                }
            }
        }

        foreach ((string key, string? value) in provided ?? new Dictionary<string, string?>())
        {
            variables[key] = value;
        }

        return variables;
    }

    private async Task<string> NextReferenceAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        int year = clock.UtcNow.Year;
        string prefix = $"WF-{year}-";

        List<string> existing = await dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId && i.Reference.StartsWith(prefix))
            .Select(i => i.Reference)
            .ToListAsync(cancellationToken);

        int highest = existing
            .Select(reference => int.TryParse(reference[prefix.Length..], out int number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}{highest + 1:D5}";
    }

    private Task AuditTaskAsync(
        WorkflowTask task,
        WorkflowInstance instance,
        WorkflowTaskDecision decision,
        string? comment,
        CancellationToken cancellationToken) =>
        auditService.WriteAsync(
            new AuditEntry
            {
                Action = AuditActions.WorkflowTaskCompleted,
                EntityType = AuditEntityTypes.WorkflowTask,
                EntityId = task.Id,
                EntityDisplayName = $"{instance.Reference} — {task.Title}",
                Summary = $"{DecisionLabel(decision)}: {task.Title}",
                After = new
                {
                    Decision = decision.ToString(),
                    Comment = comment,
                    Instance = instance.Reference,
                    InstanceStatus = instance.Status.ToString()
                }
            },
            cancellationToken);

    private static string DecisionLabel(WorkflowTaskDecision decision) => decision switch
    {
        WorkflowTaskDecision.Approved => "Aprovado",
        WorkflowTaskDecision.Rejected => "Rejeitado",
        WorkflowTaskDecision.InformationRequested => "Informação solicitada",
        _ => "Concluído"
    };
}
