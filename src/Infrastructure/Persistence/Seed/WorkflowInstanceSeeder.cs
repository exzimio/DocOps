using DocOps.Domain.Notifications;
using DocOps.Domain.Tenancy;
using DocOps.Domain.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocOps.Infrastructure.Persistence.Seed;

/// <summary>
/// Creates the demonstration workflow instances, back-dated so the task centre and the dashboards
/// have a believable mix: work waiting, work overdue, work finished and one process rejected.
///
/// Instances are written directly rather than replayed through the engine, because the engine would
/// stamp everything with the current moment. The shapes it produces are mirrored exactly: the same
/// timeline events, the same task states and the same active node keys.
/// </summary>
public sealed class WorkflowInstanceSeeder(
    DocOpsDbContext dbContext,
    ILogger<WorkflowInstanceSeeder> logger)
{
    private sealed record StepSeed(
        string NodeKey,
        string Title,
        WorkflowTaskStatus Status,
        int HoursAfterStart,
        string? AssigneeEmail = null,
        string? AssignedRole = null,
        string? AssignedDepartmentCode = null,
        WorkflowTaskDecision? Decision = null,
        int? DueInHours = null,
        bool RequiresDecision = false,
        WorkflowTaskPriority Priority = WorkflowTaskPriority.Normal,
        string? Comment = null,
        int? CompletedAfterHours = null,
        bool Escalated = false);

    private sealed record InstanceSeed(
        string DefinitionCode,
        string DocumentTitle,
        int DaysAgo,
        WorkflowInstanceStatus Status,
        string[] ActiveNodeKeys,
        StepSeed[] Steps,
        string? Outcome = null,
        string? StatusReason = null,
        (string NodeKey, string Type, string Title, int HoursAfterStart)[]? ExtraEvents = null);

    private const string Invoice = WorkflowCatalogueSeeder.InvoiceApprovalCode;
    private const string Contract = WorkflowCatalogueSeeder.ContractReviewCode;
    private const string Onboarding = WorkflowCatalogueSeeder.OnboardingCode;

    private static readonly InstanceSeed[] Instances =
    [
        // ------------------------------------------------------------------ Faturas em aprovação
        new(Invoice, "Fatura Northstar Supplies 2026/1184", 3, WorkflowInstanceStatus.Waiting,
            ["finance-approval"],
            [
                new("validation", "Validar dados da fatura", WorkflowTaskStatus.Completed, 0,
                    AssigneeEmail: "ana.ferreira@asteria.example", Decision: WorkflowTaskDecision.Completed,
                    DueInHours: 24, CompletedAfterHours: 5, Comment: "Dados conferidos com a encomenda ENC-2026-00031."),
                new("finance-approval", "Aprovar fatura", WorkflowTaskStatus.Pending, 5,
                    AssignedRole: "FinanceApprover", DueInHours: 24, RequiresDecision: true)
            ],
            ExtraEvents:
            [
                ("submitted", WorkflowEventTypes.NotificationSent, "Fatura submetida a aprovação", 5),
                ("amount-check", WorkflowEventTypes.ConditionEvaluated, "Condição 'Valor acima de 10 000 €?' avaliada como verdadeira", 5)
            ]),

        new(Invoice, "Fatura Ibérica Logística 2026/0442", 6, WorkflowInstanceStatus.Waiting,
            ["finance-approval"],
            [
                new("validation", "Validar dados da fatura", WorkflowTaskStatus.Completed, 0,
                    AssigneeEmail: "ana.ferreira@asteria.example", Decision: WorkflowTaskDecision.Completed,
                    DueInHours: 24, CompletedAfterHours: 9),
                new("finance-approval", "Aprovar fatura", WorkflowTaskStatus.InProgress, 9,
                    AssigneeEmail: "ricardo.lopes@asteria.example", DueInHours: 192, RequiresDecision: true)
            ]),

        new(Invoice, "Fatura Atlas Tecnologia 2026/0771", 2, WorkflowInstanceStatus.Waiting,
            ["management-approval"],
            [
                new("validation", "Validar dados da fatura", WorkflowTaskStatus.Completed, 0,
                    AssigneeEmail: "ana.ferreira@asteria.example", Decision: WorkflowTaskDecision.Completed,
                    DueInHours: 24, CompletedAfterHours: 3),
                new("management-approval", "Aprovar fatura de valor elevado", WorkflowTaskStatus.Pending, 3,
                    AssignedRole: "Manager", DueInHours: 72, RequiresDecision: true,
                    Priority: WorkflowTaskPriority.High)
            ],
            ExtraEvents:
            [
                ("amount-check", WorkflowEventTypes.ConditionEvaluated,
                    "Condição 'Valor acima de 10 000 €?' avaliada como verdadeira (30 504,00 € > 10 000 €)", 3)
            ]),

        // ------------------------------------------------------------------- Faturas concluídas
        new(Invoice, "Fatura Verdemar Serviços 2026/0219", 18, WorkflowInstanceStatus.Completed,
            [],
            [
                new("validation", "Validar dados da fatura", WorkflowTaskStatus.Completed, 0,
                    AssigneeEmail: "ana.ferreira@asteria.example", Decision: WorkflowTaskDecision.Completed,
                    DueInHours: 24, CompletedAfterHours: 4),
                new("finance-approval", "Aprovar fatura", WorkflowTaskStatus.Completed, 4,
                    AssigneeEmail: "ricardo.lopes@asteria.example", Decision: WorkflowTaskDecision.Approved,
                    DueInHours: 48, RequiresDecision: true, CompletedAfterHours: 11,
                    Comment: "Serviço confirmado pela equipa de operações.")
            ],
            Outcome: "Aprovada e arquivada"),

        new(Invoice, "Fatura Lusitânia Papelaria 2026/0090", 26, WorkflowInstanceStatus.Completed,
            [],
            [
                new("validation", "Validar dados da fatura", WorkflowTaskStatus.Completed, 0,
                    AssigneeEmail: "ana.ferreira@asteria.example", Decision: WorkflowTaskDecision.Completed,
                    DueInHours: 24, CompletedAfterHours: 2),
                new("finance-approval", "Aprovar fatura", WorkflowTaskStatus.Completed, 2,
                    AssigneeEmail: "ricardo.lopes@asteria.example", Decision: WorkflowTaskDecision.Approved,
                    DueInHours: 48, RequiresDecision: true, CompletedAfterHours: 6)
            ],
            Outcome: "Aprovada e arquivada"),

        new(Invoice, "Fatura Nordvik Systems INV-8842", 96, WorkflowInstanceStatus.Completed,
            [],
            [
                new("validation", "Validar dados da fatura", WorkflowTaskStatus.Completed, 0,
                    AssigneeEmail: "ana.ferreira@asteria.example", Decision: WorkflowTaskDecision.Completed,
                    DueInHours: 24, CompletedAfterHours: 7),
                new("management-approval", "Aprovar fatura de valor elevado", WorkflowTaskStatus.Completed, 7,
                    AssigneeEmail: "admin@asteria.example", Decision: WorkflowTaskDecision.Approved,
                    DueInHours: 72, RequiresDecision: true, Priority: WorkflowTaskPriority.High,
                    CompletedAfterHours: 29)
            ],
            Outcome: "Aprovada e arquivada"),

        new(Invoice, "Fatura Ibérica Logística 2026/0388", 120, WorkflowInstanceStatus.Completed,
            [],
            [
                new("validation", "Validar dados da fatura", WorkflowTaskStatus.Completed, 0,
                    AssigneeEmail: "ana.ferreira@asteria.example", Decision: WorkflowTaskDecision.Completed,
                    DueInHours: 24, CompletedAfterHours: 3),
                new("finance-approval", "Aprovar fatura", WorkflowTaskStatus.Completed, 3,
                    AssigneeEmail: "ricardo.lopes@asteria.example", Decision: WorkflowTaskDecision.Approved,
                    DueInHours: 48, RequiresDecision: true, CompletedAfterHours: 8)
            ],
            Outcome: "Aprovada e arquivada"),

        // --------------------------------------------------------------------- Fatura rejeitada
        new(Invoice, "Fatura Atlas Tecnologia 2026/0655 (valores incorretos)", 34, WorkflowInstanceStatus.Rejected,
            [],
            [
                new("validation", "Validar dados da fatura", WorkflowTaskStatus.Rejected, 0,
                    AssigneeEmail: "ana.ferreira@asteria.example", Decision: WorkflowTaskDecision.Rejected,
                    DueInHours: 24, CompletedAfterHours: 6,
                    Comment: "O valor não corresponde à encomenda associada. Devolvida ao fornecedor.")
            ],
            Outcome: "Rejeitada"),

        // ------------------------------------------------------------------------- Contratos
        new(Contract, "Contrato de parceria — Nordvik Systems", 8, WorkflowInstanceStatus.Waiting,
            ["legal-review"],
            [
                new("legal-review", "Rever cláusulas contratuais", WorkflowTaskStatus.InProgress, 0,
                    AssigneeEmail: "carla.nunes@asteria.example", DueInHours: 216)
            ]),

        new(Contract, "Contrato de prestação de serviços — Atlas Tecnologia", 12, WorkflowInstanceStatus.Waiting,
            ["signature-preparation"],
            [
                new("legal-review", "Rever cláusulas contratuais", WorkflowTaskStatus.Completed, 0,
                    AssigneeEmail: "carla.nunes@asteria.example", Decision: WorkflowTaskDecision.Completed,
                    DueInHours: 72, CompletedAfterHours: 26),
                new("management-approval", "Aprovar contrato", WorkflowTaskStatus.Completed, 26,
                    AssigneeEmail: "admin@asteria.example", Decision: WorkflowTaskDecision.Approved,
                    DueInHours: 48, RequiresDecision: true, Priority: WorkflowTaskPriority.High,
                    CompletedAfterHours: 45, Comment: "Aprovado com a condição de revisão anual de preços."),
                new("signature-preparation", "Preparar pedido de assinatura", WorkflowTaskStatus.Pending, 45,
                    AssigneeEmail: "carla.nunes@asteria.example", DueInHours: 120)
            ]),

        new(Contract, "Contrato de arrendamento — Armazém de Alverca", 9, WorkflowInstanceStatus.Waiting,
            ["management-approval"],
            [
                new("legal-review", "Rever cláusulas contratuais", WorkflowTaskStatus.Completed, 0,
                    AssigneeEmail: "carla.nunes@asteria.example", Decision: WorkflowTaskDecision.Completed,
                    DueInHours: 72, CompletedAfterHours: 30),
                new("management-approval", "Aprovar contrato", WorkflowTaskStatus.Pending, 30,
                    AssignedRole: "Manager", DueInHours: 48, RequiresDecision: true,
                    Priority: WorkflowTaskPriority.Critical, Escalated: true)
            ]),

        new(Contract, "Contrato de fornecimento — Northstar Supplies", 210, WorkflowInstanceStatus.Completed,
            [],
            [
                new("legal-review", "Rever cláusulas contratuais", WorkflowTaskStatus.Completed, 0,
                    AssigneeEmail: "carla.nunes@asteria.example", Decision: WorkflowTaskDecision.Completed,
                    DueInHours: 72, CompletedAfterHours: 40),
                new("management-approval", "Aprovar contrato", WorkflowTaskStatus.Completed, 40,
                    AssigneeEmail: "admin@asteria.example", Decision: WorkflowTaskDecision.Approved,
                    DueInHours: 48, RequiresDecision: true, Priority: WorkflowTaskPriority.High, CompletedAfterHours: 58),
                new("signature-preparation", "Preparar pedido de assinatura", WorkflowTaskStatus.Completed, 58,
                    AssigneeEmail: "carla.nunes@asteria.example", Decision: WorkflowTaskDecision.Completed,
                    DueInHours: 120, CompletedAfterHours: 96)
            ],
            Outcome: "Contrato aprovado"),

        new(Contract, "Acordo de confidencialidade — Verdemar Serviços", 64, WorkflowInstanceStatus.Completed,
            [],
            [
                new("legal-review", "Rever cláusulas contratuais", WorkflowTaskStatus.Completed, 0,
                    AssigneeEmail: "carla.nunes@asteria.example", Decision: WorkflowTaskDecision.Completed,
                    DueInHours: 72, CompletedAfterHours: 18),
                new("management-approval", "Aprovar contrato", WorkflowTaskStatus.Completed, 18,
                    AssigneeEmail: "sofia.martins@asteria.example", Decision: WorkflowTaskDecision.Approved,
                    DueInHours: 48, RequiresDecision: true, Priority: WorkflowTaskPriority.High, CompletedAfterHours: 27),
                new("signature-preparation", "Preparar pedido de assinatura", WorkflowTaskStatus.Completed, 27,
                    AssigneeEmail: "carla.nunes@asteria.example", Decision: WorkflowTaskDecision.Completed,
                    DueInHours: 120, CompletedAfterHours: 50)
            ],
            Outcome: "Contrato aprovado"),

        // ------------------------------------------------------------------------- Integração
        new(Onboarding, "Plano de integração — Marta Oliveira", 4, WorkflowInstanceStatus.Waiting,
            ["hr-file", "it-setup"],
            [
                new("hr-file", "Instruir processo individual", WorkflowTaskStatus.Pending, 0,
                    AssignedDepartmentCode: "HR", DueInHours: 48),
                new("it-setup", "Preparar equipamento e acessos", WorkflowTaskStatus.InProgress, 0,
                    AssigneeEmail: "sofia.martins@asteria.example", DueInHours: 48)
            ],
            ExtraEvents:
            [
                ("split", WorkflowEventTypes.NodeEntered, "Ramificação paralela 'Preparar integração'", 0)
            ]),

        new(Onboarding, "Contrato de trabalho — Marta Oliveira", 75, WorkflowInstanceStatus.Completed,
            [],
            [
                new("hr-file", "Instruir processo individual", WorkflowTaskStatus.Completed, 0,
                    AssigneeEmail: "joao.pereira@asteria.example", Decision: WorkflowTaskDecision.Completed,
                    DueInHours: 48, CompletedAfterHours: 12),
                new("it-setup", "Preparar equipamento e acessos", WorkflowTaskStatus.Completed, 0,
                    AssigneeEmail: "sofia.martins@asteria.example", Decision: WorkflowTaskDecision.Completed,
                    DueInHours: 48, CompletedAfterHours: 20)
            ],
            Outcome: "Integração concluída",
            ExtraEvents:
            [
                ("join", WorkflowEventTypes.NodeEntered, "Junção 'Integração preparada': todos os ramos concluídos", 20),
                ("welcome", WorkflowEventTypes.NotificationSent, "Notificação enviada a Human Resources", 20)
            ]),

        new(Onboarding, "Registo de formação — Segurança da informação", 55, WorkflowInstanceStatus.Completed,
            [],
            [
                new("hr-file", "Instruir processo individual", WorkflowTaskStatus.Completed, 0,
                    AssigneeEmail: "joao.pereira@asteria.example", Decision: WorkflowTaskDecision.Completed,
                    DueInHours: 48, CompletedAfterHours: 8),
                new("it-setup", "Preparar equipamento e acessos", WorkflowTaskStatus.Completed, 0,
                    AssigneeEmail: "sofia.martins@asteria.example", Decision: WorkflowTaskDecision.Completed,
                    DueInHours: 48, CompletedAfterHours: 14)
            ],
            Outcome: "Integração concluída")
    ];

    public async Task SeedAsync(Tenant tenant, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        bool alreadySeeded = await dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .AnyAsync(i => i.TenantId == tenant.Id, cancellationToken);

        if (alreadySeeded)
        {
            return;
        }

        Dictionary<string, WorkflowDefinition> definitions = await dbContext.WorkflowDefinitions
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenant.Id)
            .ToDictionaryAsync(d => d.Code, cancellationToken);

        Dictionary<string, (Guid Id, string Title, string Reference)> documents = await dbContext.Documents
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenant.Id)
            .ToDictionaryAsync(d => d.Title, d => new ValueTuple<Guid, string, string>(d.Id, d.Title, d.Reference), cancellationToken);

        Dictionary<string, (Guid Id, string Name)> users = await dbContext.Users
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == tenant.Id && u.Email != null)
            .ToDictionaryAsync(u => u.Email!, u => new ValueTuple<Guid, string>(u.Id, u.FirstName + " " + u.LastName), cancellationToken);

        Dictionary<string, (Guid Id, string Name)> departments = await dbContext.Departments
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenant.Id)
            .ToDictionaryAsync(d => d.Code, d => new ValueTuple<Guid, string>(d.Id, d.Name), cancellationToken);

        int sequence = 0;
        int created = 0;

        foreach (InstanceSeed seed in Instances)
        {
            if (!definitions.TryGetValue(seed.DefinitionCode, out WorkflowDefinition? definition)
                || !documents.TryGetValue(seed.DocumentTitle, out (Guid Id, string Title, string Reference) document))
            {
                logger.LogWarning(
                    "Skipping demo workflow instance for '{Document}': definition or document missing", seed.DocumentTitle);
                continue;
            }

            DateTimeOffset startedAt = now.AddDays(-seed.DaysAgo);
            sequence++;

            WorkflowInstance instance = new()
            {
                TenantId = tenant.Id,
                WorkflowDefinitionId = definition.Id,
                DefinitionVersion = definition.Version,
                DocumentId = document.Id,
                Reference = $"WF-{startedAt.Year}-{sequence:D5}",
                Status = seed.Status,
                ActiveNodeKeysJson = WorkflowVariables.ToNodeKeysJson(seed.ActiveNodeKeys),
                VariablesJson = WorkflowVariables.ToJson(new Dictionary<string, string?>
                {
                    [WorkflowVariables.DocumentReference] = document.Reference,
                    [WorkflowVariables.DocumentTitle] = document.Title
                }),
                StartedByUserId = users.TryGetValue("ana.ferreira@asteria.example", out (Guid Id, string Name) starter)
                    ? starter.Id
                    : null,
                StartedAtUtc = startedAt,
                Outcome = seed.Outcome,
                StatusReason = seed.StatusReason,
                CreatedAtUtc = startedAt
            };

            instance.Events.Add(BuildEvent(
                instance, startedAt, WorkflowEventTypes.InstanceStarted,
                $"Processo '{definition.Name}' iniciado", "start", starter.Name));

            foreach (StepSeed step in seed.Steps)
            {
                AddStep(instance, definition, step, startedAt, users, departments);
            }

            foreach ((string nodeKey, string type, string title, int hours) in seed.ExtraEvents ?? [])
            {
                instance.Events.Add(BuildEvent(
                    instance, startedAt.AddHours(hours), type, title, nodeKey, "Sistema"));
            }

            if (seed.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Rejected)
            {
                int lastHour = seed.Steps.Max(s => s.CompletedAfterHours ?? s.HoursAfterStart);
                DateTimeOffset finishedAt = startedAt.AddHours(lastHour + 1);

                instance.CompletedAtUtc = finishedAt;

                instance.Events.Add(BuildEvent(
                    instance,
                    finishedAt,
                    seed.Status == WorkflowInstanceStatus.Completed
                        ? WorkflowEventTypes.InstanceCompleted
                        : WorkflowEventTypes.InstanceRejected,
                    seed.Status == WorkflowInstanceStatus.Completed ? "Processo concluído" : "Processo rejeitado",
                    null,
                    "Sistema",
                    seed.Outcome));
            }

            dbContext.WorkflowInstances.Add(instance);
            created++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await SeedNotificationsAsync(tenant, now, cancellationToken);

        logger.LogInformation("Seeded {Count} demo workflow instances for tenant {Tenant}", created, tenant.Name);
    }

    private static void AddStep(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        StepSeed step,
        DateTimeOffset startedAt,
        IReadOnlyDictionary<string, (Guid Id, string Name)> users,
        IReadOnlyDictionary<string, (Guid Id, string Name)> departments)
    {
        DateTimeOffset assignedAt = startedAt.AddHours(step.HoursAfterStart);

        (Guid? userId, Guid? departmentId, string? role, string displayName, AssignmentStrategy strategy) = step switch
        {
            { AssigneeEmail: { } email } when users.TryGetValue(email, out (Guid Id, string Name) user) =>
                ((Guid?)user.Id, (Guid?)null, (string?)null, user.Name, AssignmentStrategy.User),

            { AssignedDepartmentCode: { } code } when departments.TryGetValue(code, out (Guid Id, string Name) department) =>
                (null, department.Id, null, department.Name, AssignmentStrategy.Department),

            { AssignedRole: { } assignedRole } =>
                (null, null, assignedRole, assignedRole, AssignmentStrategy.Role),

            _ => (null, null, null, "Não atribuída", AssignmentStrategy.Role)
        };

        DateTimeOffset? completedAt = step.CompletedAfterHours is { } hours ? startedAt.AddHours(hours) : null;

        WorkflowTask task = new()
        {
            TenantId = instance.TenantId,
            WorkflowInstanceId = instance.Id,
            NodeKey = step.NodeKey,
            Title = step.Title,
            Description = definition.FindNode(step.NodeKey)?.Description,
            Status = step.Status,
            Priority = step.Priority,
            RequiresDecision = step.RequiresDecision,
            AssignmentStrategy = strategy,
            AssignedToUserId = userId,
            AssignedToDepartmentId = departmentId,
            AssignedToRole = role,
            AssigneeDisplayName = displayName,
            AssignedAtUtc = assignedAt,
            DueAtUtc = step.DueInHours is { } due ? assignedAt.AddHours(due) : null,
            StartedAtUtc = step.Status == WorkflowTaskStatus.InProgress ? assignedAt.AddHours(1) : null,
            CompletedAtUtc = completedAt,
            CompletedByUserId = completedAt is not null ? userId : null,
            Decision = step.Decision,
            DecisionComment = step.Comment,
            EscalatedAtUtc = step.Escalated && step.DueInHours is { } escalationWindow
                ? assignedAt.AddHours(escalationWindow + 1)
                : null,
            CreatedAtUtc = assignedAt
        };

        instance.Tasks.Add(task);

        instance.Events.Add(BuildEvent(
            instance, assignedAt, WorkflowEventTypes.TaskCreated,
            $"Tarefa '{task.Title}' atribuída a {displayName}", step.NodeKey, "Sistema"));

        if (task.EscalatedAtUtc is { } escalatedAt)
        {
            instance.Events.Add(BuildEvent(
                instance, escalatedAt, WorkflowEventTypes.TaskEscalated,
                $"Tarefa '{task.Title}' escalada por incumprimento do prazo", step.NodeKey, "Sistema"));
        }

        if (completedAt is { } finishedAt)
        {
            string decisionLabel = step.Decision switch
            {
                WorkflowTaskDecision.Approved => "Aprovado",
                WorkflowTaskDecision.Rejected => "Rejeitado",
                WorkflowTaskDecision.InformationRequested => "Informação solicitada",
                _ => "Concluído"
            };

            instance.Events.Add(BuildEvent(
                instance, finishedAt, WorkflowEventTypes.TaskCompleted,
                $"Tarefa '{task.Title}' concluída por {displayName} ({decisionLabel})",
                step.NodeKey, displayName, step.Comment));
        }
    }

    private static WorkflowEvent BuildEvent(
        WorkflowInstance instance,
        DateTimeOffset occurredAt,
        string eventType,
        string title,
        string? nodeKey,
        string actorDisplayName,
        string? detail = null) =>
        new()
        {
            TenantId = instance.TenantId,
            WorkflowInstanceId = instance.Id,
            OccurredAtUtc = occurredAt,
            NodeKey = nodeKey,
            EventType = eventType,
            Title = title,
            Detail = detail,
            ActorDisplayName = actorDisplayName,
            CreatedAtUtc = occurredAt
        };

    /// <summary>Gives the open tasks the notifications they would have produced when they were created.</summary>
    private async Task SeedNotificationsAsync(Tenant tenant, DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<WorkflowTask> openTasks = await dbContext.WorkflowTasks
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenant.Id
                && (t.Status == WorkflowTaskStatus.Pending || t.Status == WorkflowTaskStatus.InProgress))
            .ToListAsync(cancellationToken);

        foreach (WorkflowTask task in openTasks.Where(t => t.AssignedToUserId is not null))
        {
            dbContext.Notifications.Add(new Notification
            {
                TenantId = tenant.Id,
                RecipientUserId = task.AssignedToUserId!.Value,
                Kind = task.DueAtUtc < now ? NotificationKind.TaskOverdue : NotificationKind.TaskAssigned,
                Title = task.Title,
                Body = task.DueAtUtc is { } due
                    ? $"Prazo: {due:dd/MM/yyyy HH:mm}"
                    : "Sem prazo definido",
                Link = "/app/tasks",
                WorkflowInstanceId = task.WorkflowInstanceId,
                WorkflowTaskId = task.Id,
                CreatedAtUtc = task.AssignedAtUtc
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
