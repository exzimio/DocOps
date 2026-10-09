using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Application.Workflows;
using DocOps.Domain.Auditing;
using DocOps.Domain.Documents;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Persistence;
using DocOps.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DocOps.Application.Abstractions;

namespace DocOps.IntegrationTests;

/// <summary>
/// Exercises the engine through the API, against a real database: a process is started on a real
/// document, tasks appear for the right people, decisions route to the right branch, and the
/// document's own state machine is respected on the way.
/// </summary>
[Collection(ApiCollection.Name)]
public class WorkflowEngineTests(DocOpsApiFactory factory)
{
    private const string Procurement = "ana.ferreira@asteria.example";
    private const string FinanceApprover = "ricardo.lopes@asteria.example";
    private const string Manager = "sofia.martins@asteria.example";
    private const string VerticeAdmin = "admin@vertice.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task An_approved_invoice_walks_the_whole_process_and_archives_the_document()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);
        HttpClient approver = await factory.SignInAsync(FinanceApprover);

        // A small invoice: the amount condition must route it to finance, not to management.
        DocumentDetailDto document = await CreateInvoiceAsync(buyer, "Fatura para aprovação financeira", total: "4500.00");
        WorkflowInstanceDetailDto instance = await StartAsync(buyer, document.Summary.Id);

        Assert.Equal(WorkflowInstanceStatus.Waiting, instance.Summary.Status);
        Assert.Equal(["validation"], instance.ActiveNodeKeys);

        // Entering the process puts the document under review.
        Assert.Equal(DocumentStatus.InReview, (await GetDocumentAsync(buyer, document.Summary.Id)).Summary.Status);

        TaskListItemDto validation = await SingleOpenTaskAsync(buyer, instance.Summary.Id);
        Assert.Equal("validation", validation.NodeKey);
        Assert.False(validation.RequiresDecision);
        Assert.Contains("complete", validation.AvailableActions);

        await ActAsync(buyer, validation.Id, "complete", "Dados conferidos.");

        // The condition routed to finance approval, and the document is now awaiting a decision.
        TaskListItemDto approval = await SingleOpenTaskAsync(approver, instance.Summary.Id);
        Assert.Equal("finance-approval", approval.NodeKey);
        Assert.True(approval.RequiresDecision);
        Assert.Equal(DocumentStatus.PendingApproval, (await GetDocumentAsync(buyer, document.Summary.Id)).Summary.Status);

        await ActAsync(approver, approval.Id, "approve", "Aprovado para pagamento.");

        WorkflowInstanceDetailDto finished = await GetInstanceAsync(approver, instance.Summary.Id);
        Assert.Equal(WorkflowInstanceStatus.Completed, finished.Summary.Status);
        Assert.Equal("Aprovada e arquivada", finished.Summary.Outcome);
        Assert.Empty(finished.ActiveNodeKeys);

        Assert.Equal(DocumentStatus.Archived, (await GetDocumentAsync(buyer, document.Summary.Id)).Summary.Status);

        Assert.Contains(finished.Timeline, e => e.EventType == WorkflowEventTypes.InstanceStarted);
        Assert.Contains(finished.Timeline, e => e.EventType == WorkflowEventTypes.ConditionEvaluated);
        Assert.Contains(finished.Timeline, e => e.EventType == WorkflowEventTypes.InstanceCompleted);
    }

    [Fact]
    public async Task A_high_value_invoice_is_routed_to_management_instead_of_finance()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);
        HttpClient manager = await factory.SignInAsync(Manager);

        DocumentDetailDto document = await CreateInvoiceAsync(buyer, "Fatura de valor elevado", total: "48000.00");
        WorkflowInstanceDetailDto instance = await StartAsync(buyer, document.Summary.Id);

        TaskListItemDto validation = await SingleOpenTaskAsync(buyer, instance.Summary.Id);
        await ActAsync(buyer, validation.Id, "complete", null);

        TaskListItemDto approval = await SingleOpenTaskAsync(manager, instance.Summary.Id);

        Assert.Equal("management-approval", approval.NodeKey);
        Assert.Equal(WorkflowTaskPriority.High, approval.Priority);

        WorkflowInstanceDetailDto afterRouting = await GetInstanceAsync(buyer, instance.Summary.Id);

        Assert.Contains(
            afterRouting.Timeline,
            e => e.EventType == WorkflowEventTypes.ConditionEvaluated && e.Detail!.Contains("true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_rejection_ends_the_process_and_rejects_the_document()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);

        DocumentDetailDto document = await CreateInvoiceAsync(buyer, "Fatura a rejeitar", total: "900.00");
        WorkflowInstanceDetailDto instance = await StartAsync(buyer, document.Summary.Id);

        TaskListItemDto validation = await SingleOpenTaskAsync(buyer, instance.Summary.Id);
        await ActAsync(buyer, validation.Id, "reject", "Valores não correspondem à encomenda.");

        WorkflowInstanceDetailDto finished = await GetInstanceAsync(buyer, instance.Summary.Id);

        Assert.Equal(WorkflowInstanceStatus.Rejected, finished.Summary.Status);
        Assert.Equal(DocumentStatus.Rejected, (await GetDocumentAsync(buyer, document.Summary.Id)).Summary.Status);
        Assert.DoesNotContain(finished.Tasks, t => t.Status is WorkflowTaskStatus.Pending or WorkflowTaskStatus.InProgress);
    }

    [Fact]
    public async Task Asking_for_information_keeps_the_task_open_and_the_process_where_it_was()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);

        DocumentDetailDto document = await CreateInvoiceAsync(buyer, "Fatura com dúvida", total: "1200.00");
        WorkflowInstanceDetailDto instance = await StartAsync(buyer, document.Summary.Id);

        TaskListItemDto validation = await SingleOpenTaskAsync(buyer, instance.Summary.Id);
        await ActAsync(buyer, validation.Id, "request-information", "Falta a referência da encomenda.");

        WorkflowInstanceDetailDto afterQuestion = await GetInstanceAsync(buyer, instance.Summary.Id);

        Assert.Equal(WorkflowInstanceStatus.Waiting, afterQuestion.Summary.Status);
        Assert.Equal(["validation"], afterQuestion.ActiveNodeKeys);

        TaskListItemDto stillOpen = await SingleOpenTaskAsync(buyer, instance.Summary.Id);
        Assert.Equal(WorkflowTaskStatus.InProgress, stillOpen.Status);
    }

    [Fact]
    public async Task A_parallel_process_waits_for_every_branch_before_moving_on()
    {
        HttpClient hr = await factory.SignInAsync("joao.pereira@asteria.example");
        HttpClient operations = await factory.SignInAsync(Manager);

        DocumentDetailDto document = await CreateEmployeeDocumentAsync(hr, "Integração de teste paralelo");
        WorkflowInstanceDetailDto instance = await StartAsync(hr, document.Summary.Id, WorkflowCatalogue.Onboarding);

        // Both branches opened at once.
        Assert.Equal(2, instance.ActiveNodeKeys.Count);
        Assert.Equal(2, instance.Tasks.Count(t => t.Status == WorkflowTaskStatus.Pending));

        TaskListItemDto hrTask = await OpenTaskAsync(hr, instance.Summary.Id, "hr-file");
        await ActAsync(hr, hrTask.Id, "complete", null);

        // One branch done is not enough: the join still holds the process.
        WorkflowInstanceDetailDto halfway = await GetInstanceAsync(hr, instance.Summary.Id);
        Assert.Equal(WorkflowInstanceStatus.Waiting, halfway.Summary.Status);

        TaskListItemDto itTask = await OpenTaskAsync(operations, instance.Summary.Id, "it-setup");
        await ActAsync(operations, itTask.Id, "complete", null);

        WorkflowInstanceDetailDto finished = await GetInstanceAsync(hr, instance.Summary.Id);
        Assert.Equal(WorkflowInstanceStatus.Completed, finished.Summary.Status);
        Assert.Equal("Integração concluída", finished.Summary.Outcome);
    }

    [Fact]
    public async Task A_task_offered_to_a_department_is_invisible_to_everyone_else()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);
        HttpClient approver = await factory.SignInAsync(FinanceApprover);

        DocumentDetailDto document = await CreateInvoiceAsync(buyer, "Fatura com tarefa de departamento", total: "700.00");
        WorkflowInstanceDetailDto instance = await StartAsync(buyer, document.Summary.Id);

        TaskListItemDto validation = await SingleOpenTaskAsync(buyer, instance.Summary.Id);

        // The finance approver is not in Procurement: acting on its task must be refused.
        HttpResponseMessage attempt = await approver.PostAsJsonAsync(
            $"/api/tasks/{validation.Id}/complete", new { comment = "não devia conseguir" });

        Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);
    }

    [Fact]
    public async Task Acting_on_a_group_task_records_who_actually_decided()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);

        DocumentDetailDto document = await CreateInvoiceAsync(buyer, "Fatura reclamada pelo departamento", total: "300.00");
        WorkflowInstanceDetailDto instance = await StartAsync(buyer, document.Summary.Id);

        TaskListItemDto validation = await SingleOpenTaskAsync(buyer, instance.Summary.Id);
        Assert.Null(validation.AssignedToUserId);

        TaskListItemDto completed = await ActAsync(buyer, validation.Id, "complete", "Assumida e concluída.");

        Assert.NotNull(completed.AssignedToUserId);
        Assert.Equal("Ana Ferreira", completed.CompletedByName);
    }

    [Fact]
    public async Task A_second_decision_on_the_same_task_is_refused()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);

        DocumentDetailDto document = await CreateInvoiceAsync(buyer, "Fatura decidida duas vezes", total: "800.00");
        WorkflowInstanceDetailDto instance = await StartAsync(buyer, document.Summary.Id);

        TaskListItemDto validation = await SingleOpenTaskAsync(buyer, instance.Summary.Id);
        await ActAsync(buyer, validation.Id, "complete", null);

        HttpResponseMessage second = await buyer.PostAsJsonAsync(
            $"/api/tasks/{validation.Id}/complete", new { comment = "outra vez" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("task_already_closed", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approving_a_task_that_asks_for_work_rather_than_a_decision_is_refused()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);

        DocumentDetailDto document = await CreateInvoiceAsync(buyer, "Fatura com tarefa de execução", total: "650.00");
        WorkflowInstanceDetailDto instance = await StartAsync(buyer, document.Summary.Id);

        TaskListItemDto validation = await SingleOpenTaskAsync(buyer, instance.Summary.Id);

        HttpResponseMessage response = await buyer.PostAsJsonAsync(
            $"/api/tasks/{validation.Id}/approve", new { comment = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("task_not_a_decision", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelling_a_process_closes_its_open_tasks()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);
        HttpClient manager = await factory.SignInAsync(Manager);

        DocumentDetailDto document = await CreateInvoiceAsync(buyer, "Fatura a cancelar", total: "1500.00");
        WorkflowInstanceDetailDto instance = await StartAsync(buyer, document.Summary.Id);

        HttpResponseMessage response = await manager.PostAsJsonAsync(
            $"/api/workflows/instances/{instance.Summary.Id}/cancel",
            new CancelWorkflowRequest("Fornecedor retirou a fatura."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        WorkflowInstanceDetailDto cancelled = await GetInstanceAsync(buyer, instance.Summary.Id);

        Assert.Equal(WorkflowInstanceStatus.Cancelled, cancelled.Summary.Status);
        Assert.All(cancelled.Tasks, task => Assert.False(
            task.Status is WorkflowTaskStatus.Pending or WorkflowTaskStatus.InProgress));
    }

    [Fact]
    public async Task Processes_of_another_organisation_are_not_visible()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);
        HttpClient vertice = await factory.SignInAsync(VerticeAdmin);

        DocumentDetailDto document = await CreateInvoiceAsync(buyer, "Fatura isolada", total: "250.00");
        WorkflowInstanceDetailDto instance = await StartAsync(buyer, document.Summary.Id);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await vertice.GetAsync($"/api/workflows/instances/{instance.Summary.Id}")).StatusCode);

        PagedResult<WorkflowInstanceListItemDto>? theirs =
            await vertice.GetFromJsonAsync<PagedResult<WorkflowInstanceListItemDto>>(
                "/api/workflows/instances?pageSize=100", Json);

        Assert.NotNull(theirs);
        Assert.DoesNotContain(theirs.Items, item => item.Id == instance.Summary.Id);
    }

    [Fact]
    public async Task Starting_a_process_and_deciding_a_task_are_both_audited()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);

        DocumentDetailDto document = await CreateInvoiceAsync(buyer, "Fatura auditada", total: "450.00");
        WorkflowInstanceDetailDto instance = await StartAsync(buyer, document.Summary.Id);

        TaskListItemDto validation = await SingleOpenTaskAsync(buyer, instance.Summary.Id);
        await ActAsync(buyer, validation.Id, "complete", "Conferido.");

        using ISystemScope scope = factory.CreateServiceScope();
        DocOpsDbContext context = scope.Services.GetRequiredService<DocOpsDbContext>();

        Assert.True(await context.AuditEvents
            .IgnoreQueryFilters()
            .AnyAsync(e => e.EntityId == instance.Summary.Id && e.Action == AuditActions.WorkflowInstanceStarted));

        Assert.True(await context.AuditEvents
            .IgnoreQueryFilters()
            .AnyAsync(e => e.EntityId == validation.Id && e.Action == AuditActions.WorkflowTaskCompleted));
    }

    [Fact]
    public async Task A_new_task_notifies_the_people_it_is_waiting_for()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);

        DocumentDetailDto document = await CreateInvoiceAsync(buyer, "Fatura que notifica", total: "990.00");
        await StartAsync(buyer, document.Summary.Id);

        IReadOnlyList<NotificationDto>? notifications =
            await buyer.GetFromJsonAsync<IReadOnlyList<NotificationDto>>("/api/notifications?limit=20", Json);

        Assert.NotNull(notifications);
        Assert.Contains(notifications, n => n.Title == "Validar dados da fatura");
    }

    [Fact]
    public async Task Definitions_report_their_validation_state_and_the_seeded_ones_are_executable()
    {
        HttpClient approver = await factory.SignInAsync(FinanceApprover);

        IReadOnlyList<WorkflowDefinitionSummaryDto>? definitions =
            await approver.GetFromJsonAsync<IReadOnlyList<WorkflowDefinitionSummaryDto>>("/api/workflows", Json);

        Assert.NotNull(definitions);
        Assert.Equal(3, definitions.Count);
        Assert.All(definitions, definition => Assert.True(definition.IsActive));

        foreach (WorkflowDefinitionSummaryDto definition in definitions)
        {
            HttpResponseMessage response = await approver.PostAsync($"/api/workflows/{definition.Id}/test", content: null);
            IReadOnlyList<WorkflowValidationIssueDto>? issues =
                await response.Content.ReadFromJsonAsync<IReadOnlyList<WorkflowValidationIssueDto>>(Json);

            Assert.NotNull(issues);
            Assert.DoesNotContain(issues, issue => issue.Severity == "Error");
        }
    }

    /// <summary>
    /// The designer offers every node type, and the validator lets any of them be activated. A type
    /// without an executor would pass both and only fail when an instance reached it, so the
    /// guarantee lives here, on the container the engine is actually built from.
    /// </summary>
    [Fact]
    public void Every_node_type_has_exactly_one_registered_executor()
    {
        using IServiceScope scope = factory.Services.CreateScope();

        List<WorkflowNodeType> registered =
            [.. scope.ServiceProvider.GetServices<INodeExecutor>().Select(executor => executor.NodeType)];

        Assert.Equal(
            Enum.GetValues<WorkflowNodeType>().Order(),
            registered.Order());
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    private static class WorkflowCatalogue
    {
        public const string InvoiceApproval = "invoice-approval";
        public const string Onboarding = "employee-onboarding";
    }

    private static async Task<Guid> GetDefinitionIdAsync(HttpClient client, string code)
    {
        IReadOnlyList<WorkflowDefinitionSummaryDto>? definitions =
            await client.GetFromJsonAsync<IReadOnlyList<WorkflowDefinitionSummaryDto>>("/api/workflows", Json);

        Assert.NotNull(definitions);
        return definitions.Single(definition => definition.Code == code).Id;
    }

    private static async Task<WorkflowInstanceDetailDto> StartAsync(
        HttpClient client, Guid documentId, string definitionCode = WorkflowCatalogue.InvoiceApproval)
    {
        Guid definitionId = await GetDefinitionIdAsync(client, definitionCode);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/workflows/instances", new StartWorkflowRequest { DefinitionId = definitionId, DocumentId = documentId });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<WorkflowInstanceDetailDto>(Json))!;
    }

    private static async Task<WorkflowInstanceDetailDto> GetInstanceAsync(HttpClient client, Guid instanceId) =>
        (await client.GetFromJsonAsync<WorkflowInstanceDetailDto>($"/api/workflows/instances/{instanceId}", Json))!;

    private static async Task<DocumentDetailDto> GetDocumentAsync(HttpClient client, Guid documentId) =>
        (await client.GetFromJsonAsync<DocumentDetailDto>($"/api/documents/{documentId}", Json))!;

    private static async Task<TaskListItemDto> SingleOpenTaskAsync(HttpClient client, Guid instanceId)
    {
        PagedResult<TaskListItemDto>? tasks = await client.GetFromJsonAsync<PagedResult<TaskListItemDto>>(
            $"/api/tasks?scope=Mine&instanceId={instanceId}&statuses=0&statuses=1", Json);

        Assert.NotNull(tasks);
        return Assert.Single(tasks.Items);
    }

    private static async Task<TaskListItemDto> OpenTaskAsync(HttpClient client, Guid instanceId, string nodeKey)
    {
        PagedResult<TaskListItemDto>? tasks = await client.GetFromJsonAsync<PagedResult<TaskListItemDto>>(
            $"/api/tasks?scope=Mine&instanceId={instanceId}&statuses=0&statuses=1", Json);

        Assert.NotNull(tasks);
        return tasks.Items.Single(task => task.NodeKey == nodeKey);
    }

    private static async Task<TaskListItemDto> ActAsync(
        HttpClient client, Guid taskId, string action, string? comment)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync($"/api/tasks/{taskId}/{action}", new { comment });

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Task action '{action}' failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<TaskListItemDto>(Json))!;
    }

    private static async Task<DocumentDetailDto> CreateInvoiceAsync(HttpClient client, string title, string total)
    {
        IReadOnlyList<DocumentTypeDto>? types =
            await client.GetFromJsonAsync<IReadOnlyList<DocumentTypeDto>>("/api/document-catalogue/types", Json);

        Assert.NotNull(types);
        Guid typeId = types.Single(type => type.Code == "SupplierInvoice").Id;

        using MultipartFormDataContent form = [];
        ByteArrayContent file = new(Encoding.UTF8.GetBytes($"FATURA\n{title}\nTotal: {total}"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(file, "File", "fatura.txt");
        form.Add(new StringContent(title), "Title");
        form.Add(new StringContent(typeId.ToString()), "DocumentTypeId");
        form.Add(new StringContent("supplier=Fornecedor de Teste"), "Metadata");
        form.Add(new StringContent("invoiceNumber=TEST-0001"), "Metadata");
        form.Add(new StringContent("invoiceDate=2026-09-01"), "Metadata");
        form.Add(new StringContent($"totalAmount={total}"), "Metadata");

        HttpResponseMessage response = await client.PostAsync("/api/documents", form);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<DocumentDetailDto>(Json))!;
    }

    private static async Task<DocumentDetailDto> CreateEmployeeDocumentAsync(HttpClient client, string title)
    {
        IReadOnlyList<DocumentTypeDto>? types =
            await client.GetFromJsonAsync<IReadOnlyList<DocumentTypeDto>>("/api/document-catalogue/types", Json);

        Assert.NotNull(types);
        Guid typeId = types.Single(type => type.Code == "EmployeeDocument").Id;

        using MultipartFormDataContent form = [];
        ByteArrayContent file = new(Encoding.UTF8.GetBytes($"DOCUMENTO DE COLABORADOR\n{title}"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(file, "File", "colaborador.txt");
        form.Add(new StringContent(title), "Title");
        form.Add(new StringContent(typeId.ToString()), "DocumentTypeId");
        form.Add(new StringContent("employeeName=Marta Oliveira"), "Metadata");
        form.Add(new StringContent("documentKind=Onboarding"), "Metadata");
        form.Add(new StringContent("effectiveDate=2026-09-01"), "Metadata");

        HttpResponseMessage response = await client.PostAsync("/api/documents", form);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<DocumentDetailDto>(Json))!;
    }
}
