using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DocOps.Application.Abstractions;
using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Application.Identity;
using DocOps.Application.Workflows;
using DocOps.Domain.Auditing;
using DocOps.Domain.Documents;
using DocOps.Domain.Notifications;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Persistence;
using DocOps.Infrastructure.Workflows;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DocOps.IntegrationTests;

/// <summary>Fails any save that adds a notification, while armed.</summary>
file sealed class FailingNotificationSave : SaveChangesInterceptor
{
    public bool Armed { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Armed && eventData.Context!.ChangeTracker.Entries<Notification>().Any(e => e.State == EntityState.Added))
        {
            throw new InvalidOperationException("Gravação interrompida pelo teste.");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

/// <summary>
/// The engine against a real database, each case on a process designed for it or on the seeded
/// invoice approval.
/// </summary>
[Collection(ApiCollection.Name)]
public class WorkflowExecutionTests(DocOpsApiFactory factory)
{
    private const string Administrator = "admin@asteria.example";
    private const string Manager = "sofia.martins@asteria.example";
    private const string FinanceApprover = "ricardo.lopes@asteria.example";
    private const string Procurement = "ana.ferreira@asteria.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // -----------------------------------------------------------------------------------------
    // Timer
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_timer_holds_the_process_until_its_moment_and_the_resume_then_moves_it_on()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        Guid definitionId = await DesignAsync(admin, "Espera de uma hora",
            [Node("inicio", WorkflowNodeType.Start), Node("espera", WorkflowNodeType.Delay, """{"delayHours":1}"""), End("fim")],
            [Edge("inicio", "espera"), Edge("espera", "fim")]);

        DateTimeOffset before = DateTimeOffset.UtcNow;
        WorkflowInstanceDetailDto started = await StartAsync(admin, definitionId);

        Assert.Equal(WorkflowInstanceStatus.Waiting, started.Summary.Status);
        Assert.Equal(["espera"], started.ActiveNodeKeys);
        Assert.NotNull(started.ResumeAtUtc);
        Assert.InRange(started.ResumeAtUtc.Value, before.AddMinutes(59), DateTimeOffset.UtcNow.AddMinutes(61));

        // Resuming before the moment changes nothing, and does not restart the timer from zero.
        await ResumeTimerAsync(started.Summary.Id);

        WorkflowInstanceDetailDto early = await GetInstanceAsync(admin, started.Summary.Id);
        Assert.Equal(WorkflowInstanceStatus.Waiting, early.Summary.Status);
        Assert.Equal(started.ResumeAtUtc, early.ResumeAtUtc);

        await MoveTimerToThePastAsync(started.Summary.Id);
        await ResumeTimerAsync(started.Summary.Id);

        WorkflowInstanceDetailDto finished = await GetInstanceAsync(admin, started.Summary.Id);
        Assert.Equal(WorkflowInstanceStatus.Completed, finished.Summary.Status);
        Assert.Null(finished.ResumeAtUtc);
        Assert.Contains(finished.Timeline, e => e.EventType == WorkflowEventTypes.TimerElapsed && e.NodeKey == "espera");
    }

    // -----------------------------------------------------------------------------------------
    // Join
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_join_does_not_wait_for_a_branch_that_a_condition_skipped()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        HttpClient manager = await factory.SignInAsync(Manager);

        Guid definitionId = await DesignAsync(admin, "Junção com ramo opcional", ConditionalBranchNodes(), ConditionalBranchEdges());
        WorkflowInstanceDetailDto instance = await StartAsync(admin, definitionId, new() { ["rota"] = "curta" });

        // The condition sent its branch straight to the join; only the manager's task is left.
        Assert.Equal(WorkflowInstanceStatus.Waiting, instance.Summary.Status);

        await ActAsync(manager, await OpenTaskAsync(manager, instance.Summary.Id, "gestor"), "approve");

        Assert.Equal(WorkflowInstanceStatus.Completed, (await GetInstanceAsync(admin, instance.Summary.Id)).Summary.Status);
    }

    [Fact]
    public async Task A_join_waits_for_every_branch_that_actually_runs()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        HttpClient manager = await factory.SignInAsync(Manager);
        HttpClient finance = await factory.SignInAsync(FinanceApprover);

        Guid definitionId = await DesignAsync(admin, "Junção com os dois ramos", ConditionalBranchNodes(), ConditionalBranchEdges());
        WorkflowInstanceDetailDto instance = await StartAsync(admin, definitionId, new() { ["rota"] = "completa" });

        await ActAsync(manager, await OpenTaskAsync(manager, instance.Summary.Id, "gestor"), "approve");

        WorkflowInstanceDetailDto halfway = await GetInstanceAsync(admin, instance.Summary.Id);
        Assert.Equal(WorkflowInstanceStatus.Waiting, halfway.Summary.Status);
        Assert.Contains("juncao", halfway.ActiveNodeKeys);

        await ActAsync(finance, await OpenTaskAsync(finance, instance.Summary.Id, "financeiro"), "approve");

        Assert.Equal(WorkflowInstanceStatus.Completed, (await GetInstanceAsync(admin, instance.Summary.Id)).Summary.Status);
    }

    [Fact]
    public async Task A_timer_resuming_the_process_does_not_count_as_an_arrival_at_a_waiting_join()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        HttpClient manager = await factory.SignInAsync(Manager);

        Guid definitionId = await DesignAsync(admin, "Junção com temporizador",
            [
                Node("inicio", WorkflowNodeType.Start),
                Node("divisao", WorkflowNodeType.ParallelBranch),
                Approval("gestor", "Manager"),
                Node("espera", WorkflowNodeType.Delay, """{"delayHours":1}"""),
                Node("juncao", WorkflowNodeType.Join),
                End("fim")
            ],
            [
                Edge("inicio", "divisao"),
                Edge("divisao", "juncao"),
                Edge("divisao", "gestor"),
                Edge("divisao", "espera"),
                Edge("gestor", "juncao", "approved"),
                Edge("espera", "juncao"),
                Edge("juncao", "fim")
            ]);

        WorkflowInstanceDetailDto instance = await StartAsync(admin, definitionId);

        // The timer's resume walks every active node again, the waiting join included. Counted as
        // an arrival, that walk let the join through while the manager's task was still open.
        await MoveTimerToThePastAsync(instance.Summary.Id);
        await ResumeTimerAsync(instance.Summary.Id);

        WorkflowInstanceDetailDto afterTimer = await GetInstanceAsync(admin, instance.Summary.Id);
        Assert.Equal(WorkflowInstanceStatus.Waiting, afterTimer.Summary.Status);
        Assert.Contains(afterTimer.Tasks, task => task.NodeKey == "gestor" && task.Status == WorkflowTaskStatus.Pending);

        await ActAsync(manager, await OpenTaskAsync(manager, instance.Summary.Id, "gestor"), "approve");

        Assert.Equal(WorkflowInstanceStatus.Completed, (await GetInstanceAsync(admin, instance.Summary.Id)).Summary.Status);
    }

    // -----------------------------------------------------------------------------------------
    // One walk, one transaction
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_walk_whose_save_fails_leaves_the_process_the_document_and_the_audit_as_they_were()
    {
        FailingNotificationSave failingSave = new();

        using WebApplicationFactory<Program> api = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.ConfigureDbContext<DocOpsDbContext>(options => options.AddInterceptors(failingSave))));

        HttpClient buyer = await SignInAsync(api, Procurement);

        DocumentDetailDto document = await CreateInvoiceAsync(buyer, "Fatura cuja aprovação não grava");
        Guid definitionId = await InvoiceApprovalIdAsync(buyer);

        HttpResponseMessage started = await buyer.PostAsJsonAsync(
            "/api/workflows/instances", new StartWorkflowRequest { DefinitionId = definitionId, DocumentId = document.Summary.Id });
        started.EnsureSuccessStatusCode();
        WorkflowInstanceDetailDto instance = (await started.Content.ReadFromJsonAsync<WorkflowInstanceDetailDto>(Json))!;

        TaskListItemDto validation = await OpenTaskAsync(buyer, instance.Summary.Id, "validation");

        // Completing validation moves the document to approval and creates the finance task, whose
        // notification is part of the same save. That save now fails.
        failingSave.Armed = true;
        HttpResponseMessage refused = await buyer.PostAsJsonAsync($"/api/tasks/{validation.Id}/complete", new { comment = (string?)null });
        failingSave.Armed = false;

        Assert.Equal(HttpStatusCode.InternalServerError, refused.StatusCode);

        WorkflowInstanceDetailDto after = (await buyer.GetFromJsonAsync<WorkflowInstanceDetailDto>(
            $"/api/workflows/instances/{instance.Summary.Id}", Json))!;

        Assert.Equal(["validation"], after.ActiveNodeKeys);
        Assert.Contains(after.Tasks, task => task.NodeKey == "validation" && task.Status == WorkflowTaskStatus.Pending);
        Assert.DoesNotContain(after.Tasks, task => task.NodeKey != "validation");

        DocumentDetailDto unchanged = (await buyer.GetFromJsonAsync<DocumentDetailDto>(
            $"/api/documents/{document.Summary.Id}", Json))!;
        Assert.Equal(DocumentStatus.InReview, unchanged.Summary.Status);

        using ISystemScope scope = factory.CreateServiceScope();
        DocOpsDbContext context = scope.Services.GetRequiredService<DocOpsDbContext>();

        Assert.False(await context.AuditEvents
            .IgnoreQueryFilters()
            .AnyAsync(e =>
                e.EntityId == document.Summary.Id
                && e.Action == AuditActions.DocumentStatusChanged
                && e.AfterJson!.Contains(nameof(DocumentStatus.PendingApproval))));
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Two parallel branches into one join: a manager's approval, and a condition on «rota» that
    /// either adds a finance approval or goes straight to the join.
    /// </summary>
    private static List<SaveWorkflowNodeRequest> ConditionalBranchNodes() =>
    [
        Node("inicio", WorkflowNodeType.Start),
        Node("divisao", WorkflowNodeType.ParallelBranch),
        Approval("gestor", "Manager"),
        Node("rota", WorkflowNodeType.Condition, """{"condition":{"field":"rota","operator":"Equals","value":"completa"}}"""),
        Approval("financeiro", "FinanceApprover"),
        Node("juncao", WorkflowNodeType.Join),
        End("fim")
    ];

    private static List<SaveWorkflowEdgeRequest> ConditionalBranchEdges() =>
    [
        Edge("inicio", "divisao"),
        Edge("divisao", "gestor"),
        Edge("divisao", "rota"),
        Edge("gestor", "juncao", "approved"),
        Edge("rota", "financeiro", "true"),
        Edge("rota", "juncao", "false"),
        Edge("financeiro", "juncao", "approved"),
        Edge("juncao", "fim")
    ];

    private static SaveWorkflowNodeRequest Node(string key, WorkflowNodeType type, string? configJson = null) => new()
    {
        Key = key,
        Name = key,
        Type = type,
        ConfigJson = configJson,
        PositionX = 0,
        PositionY = 0
    };

    private static SaveWorkflowNodeRequest Approval(string key, string role) => Node(
        key,
        WorkflowNodeType.Approval,
        JsonSerializer.Serialize(new { taskTitle = $"Aprovar {key}", assignment = new { strategy = "Role", role } }));

    private static SaveWorkflowNodeRequest End(string key) =>
        Node(key, WorkflowNodeType.End, """{"outcome":"Concluído"}""");

    private static SaveWorkflowEdgeRequest Edge(string from, string to, string? branch = null) =>
        new() { FromNodeKey = from, ToNodeKey = to, Branch = branch };

    private static async Task<Guid> DesignAsync(
        HttpClient admin, string name, List<SaveWorkflowNodeRequest> nodes, List<SaveWorkflowEdgeRequest> edges)
    {
        HttpResponseMessage created = await admin.PostAsJsonAsync(
            "/api/workflows", new CreateWorkflowRequest { Name = $"{name} {Guid.NewGuid():N}"[..40] });
        created.EnsureSuccessStatusCode();

        WorkflowDefinitionDetailDto draft = (await created.Content.ReadFromJsonAsync<WorkflowDefinitionDetailDto>(Json))!;

        HttpResponseMessage saved = await admin.PutAsJsonAsync($"/api/workflows/{draft.Summary.Id}", new SaveWorkflowRequest
        {
            Name = draft.Summary.Name,
            Nodes = nodes,
            Edges = edges,
            ConcurrencyToken = draft.ConcurrencyToken
        });

        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());

        HttpResponseMessage activated = await admin.PostAsync($"/api/workflows/{draft.Summary.Id}/activate", null);
        Assert.True(activated.IsSuccessStatusCode, await activated.Content.ReadAsStringAsync());

        return draft.Summary.Id;
    }

    private static async Task<WorkflowInstanceDetailDto> StartAsync(
        HttpClient client, Guid definitionId, Dictionary<string, string?>? variables = null)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/workflows/instances",
            new StartWorkflowRequest { DefinitionId = definitionId, Variables = variables ?? [] });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<WorkflowInstanceDetailDto>(Json))!;
    }

    private static async Task<WorkflowInstanceDetailDto> GetInstanceAsync(HttpClient client, Guid instanceId) =>
        (await client.GetFromJsonAsync<WorkflowInstanceDetailDto>($"/api/workflows/instances/{instanceId}", Json))!;

    private static async Task<TaskListItemDto> OpenTaskAsync(HttpClient client, Guid instanceId, string nodeKey)
    {
        PagedResult<TaskListItemDto> tasks = (await client.GetFromJsonAsync<PagedResult<TaskListItemDto>>(
            $"/api/tasks?scope=Mine&instanceId={instanceId}&statuses=0&statuses=1", Json))!;

        return tasks.Items.Single(task => task.NodeKey == nodeKey);
    }

    private static async Task ActAsync(HttpClient client, TaskListItemDto task, string action)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync($"/api/tasks/{task.Id}/{action}", new { comment = (string?)null });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The worker's path: a system scope and the engine, with no request behind it.</summary>
    private async Task ResumeTimerAsync(Guid instanceId)
    {
        using ISystemScope scope = factory.CreateServiceScope();
        await scope.Services.GetRequiredService<WorkflowEngine>().ResumeTimerAsync(instanceId);
    }

    private async Task MoveTimerToThePastAsync(Guid instanceId)
    {
        using ISystemScope scope = factory.CreateServiceScope();
        DocOpsDbContext context = scope.Services.GetRequiredService<DocOpsDbContext>();

        WorkflowInstance instance = await context.WorkflowInstances.IgnoreQueryFilters().SingleAsync(i => i.Id == instanceId);
        Assert.NotNull(instance.ResumeAtUtc);

        instance.ResumeAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();
    }

    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> api, string email)
    {
        HttpClient client = api.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri(DocOpsApiFactory.TestOrigin)
        });

        client.DefaultRequestHeaders.Add("Origin", DocOpsApiFactory.TestOrigin);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, DocOpsApiFactory.SeedPassword));
        response.EnsureSuccessStatusCode();

        return client;
    }

    private static async Task<Guid> InvoiceApprovalIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<IReadOnlyList<WorkflowDefinitionSummaryDto>>("/api/workflows", Json))!
            .Single(definition => definition.Code == "invoice-approval").Id;

    private static async Task<DocumentDetailDto> CreateInvoiceAsync(HttpClient client, string title)
    {
        IReadOnlyList<DocumentTypeDto> types =
            (await client.GetFromJsonAsync<IReadOnlyList<DocumentTypeDto>>("/api/document-catalogue/types", Json))!;

        using MultipartFormDataContent form = [];
        ByteArrayContent file = new(Encoding.UTF8.GetBytes($"FATURA\n{title}\nTotal: 4500.00"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(file, "File", "fatura.txt");
        form.Add(new StringContent(title), "Title");
        form.Add(new StringContent(types.Single(type => type.Code == "SupplierInvoice").Id.ToString()), "DocumentTypeId");
        form.Add(new StringContent("supplier=Fornecedor de Teste"), "Metadata");
        form.Add(new StringContent("invoiceNumber=TEST-0002"), "Metadata");
        form.Add(new StringContent("invoiceDate=2026-09-01"), "Metadata");
        form.Add(new StringContent("totalAmount=4500.00"), "Metadata");

        HttpResponseMessage response = await client.PostAsync("/api/documents", form);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<DocumentDetailDto>(Json))!;
    }
}
