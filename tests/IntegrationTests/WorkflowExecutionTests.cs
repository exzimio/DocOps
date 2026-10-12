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
    private const string Procurement = "ana.ferreira@asteria.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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

    private static async Task<TaskListItemDto> OpenTaskAsync(HttpClient client, Guid instanceId, string nodeKey)
    {
        PagedResult<TaskListItemDto> tasks = (await client.GetFromJsonAsync<PagedResult<TaskListItemDto>>(
            $"/api/tasks?scope=Mine&instanceId={instanceId}&statuses=0&statuses=1", Json))!;

        return tasks.Items.Single(task => task.NodeKey == nodeKey);
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
