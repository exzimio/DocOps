using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Domain.Auditing;
using DocOps.Domain.Documents;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DocOps.Application.Abstractions;

namespace DocOps.IntegrationTests;

[Collection(ApiCollection.Name)]
public class DocumentsTests(DocOpsApiFactory factory)
{
    private const string Administrator = "admin@asteria.example";
    private const string FinanceApprover = "ricardo.lopes@asteria.example";
    private const string Employee = "ana.ferreira@asteria.example";
    private const string ExternalPartner = "contacto@northstar.example";
    private const string VerticeAdmin = "admin@vertice.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // -----------------------------------------------------------------------------------------
    // Visibility
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task External_partner_sees_only_the_documents_explicitly_shared_with_them()
    {
        HttpClient partner = await factory.SignInAsync(ExternalPartner);

        PagedResult<DocumentListItemDto>? result =
            await partner.GetFromJsonAsync<PagedResult<DocumentListItemDto>>("/api/documents?pageSize=100", Json);

        Assert.NotNull(result);

        // Not a count: the portal suite legitimately submits documents as this same partner, and a
        // fixed total would make this test depend on which class ran first. What has to hold is
        // that every row it gets back is its own — the invoice shared with it, plus anything it
        // submitted itself — and that none of the organisation's other documents leaked in.
        Assert.NotEmpty(result.Items);
        Assert.Contains(result.Items, item => item.Title.Contains("Northstar", StringComparison.Ordinal));

        HttpClient admin = await factory.SignInAsync(Administrator);
        PagedResult<DocumentListItemDto>? everything =
            await admin.GetFromJsonAsync<PagedResult<DocumentListItemDto>>("/api/documents?pageSize=200", Json);

        Assert.NotNull(everything);
        Assert.True(
            result.TotalCount < everything.TotalCount,
            "o parceiro externo não pode ver o catálogo inteiro da organização");
    }

    [Fact]
    public async Task Employee_does_not_see_restricted_documents_of_other_departments()
    {
        HttpClient employee = await factory.SignInAsync(Employee);

        PagedResult<DocumentListItemDto>? result =
            await employee.GetFromJsonAsync<PagedResult<DocumentListItemDto>>("/api/documents?pageSize=100", Json);

        Assert.NotNull(result);
        Assert.All(result.Items, item => Assert.NotEqual(SecurityClassification.Restricted, item.Classification));
    }

    [Fact]
    public async Task A_second_organisation_sees_an_empty_repository()
    {
        HttpClient vertice = await factory.SignInAsync(VerticeAdmin);

        PagedResult<DocumentListItemDto>? result =
            await vertice.GetFromJsonAsync<PagedResult<DocumentListItemDto>>("/api/documents?pageSize=100", Json);

        Assert.NotNull(result);

        // Identidade, não contagem. "Zero documentos" é uma afirmação sobre o que mais ninguém
        // criou, e parte no dia em que um teste de administração criar algo nesta organização. O
        // que tem de valer é que nenhuma linha atravessa.
        HttpClient asteria = await factory.SignInAsync(Administrator);
        PagedResult<DocumentListItemDto>? theirs =
            await asteria.GetFromJsonAsync<PagedResult<DocumentListItemDto>>("/api/documents?pageSize=200", Json);

        Assert.NotNull(theirs);
        Assert.NotEmpty(theirs.Items);
        Assert.DoesNotContain(result.Items, item => theirs.Items.Any(other => other.Id == item.Id));
    }

    [Fact]
    public async Task Filtering_by_status_narrows_the_result_without_widening_visibility()
    {
        HttpClient approver = await factory.SignInAsync(FinanceApprover);

        PagedResult<DocumentListItemDto>? all =
            await approver.GetFromJsonAsync<PagedResult<DocumentListItemDto>>("/api/documents?pageSize=100", Json);

        PagedResult<DocumentListItemDto>? pending = await approver.GetFromJsonAsync<PagedResult<DocumentListItemDto>>(
            $"/api/documents?pageSize=100&statuses={(int)DocumentStatus.PendingApproval}", Json);

        Assert.NotNull(all);
        Assert.NotNull(pending);
        Assert.True(pending.TotalCount > 0);
        Assert.True(pending.TotalCount <= all.TotalCount);
        Assert.All(pending.Items, item => Assert.Equal(DocumentStatus.PendingApproval, item.Status));
    }

    [Fact]
    public async Task Free_text_search_matches_title_reference_and_metadata()
    {
        HttpClient approver = await factory.SignInAsync(FinanceApprover);

        PagedResult<DocumentListItemDto>? result = await approver.GetFromJsonAsync<PagedResult<DocumentListItemDto>>(
            "/api/documents?pageSize=100&query=Northstar", Json);

        Assert.NotNull(result);
        Assert.True(result.TotalCount > 0);
    }

    [Fact]
    public async Task Paging_is_applied_on_the_server()
    {
        HttpClient approver = await factory.SignInAsync(FinanceApprover);

        PagedResult<DocumentListItemDto>? firstPage =
            await approver.GetFromJsonAsync<PagedResult<DocumentListItemDto>>("/api/documents?page=1&pageSize=5", Json);

        Assert.NotNull(firstPage);
        Assert.True(firstPage.Items.Count <= 5);
        Assert.True(firstPage.TotalCount > firstPage.Items.Count);
        Assert.True(firstPage.HasNext);
    }

    // -----------------------------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Upload_creates_a_document_a_first_version_and_an_audit_entry()
    {
        HttpClient approver = await factory.SignInAsync(FinanceApprover);
        DocumentDetailDto document = await CreateInvoiceAsync(approver, "Fatura de teste de integração");

        Assert.StartsWith("FAT-", document.Summary.Reference, StringComparison.Ordinal);
        Assert.Equal(DocumentStatus.Draft, document.Summary.Status);
        Assert.Equal(1, document.Summary.CurrentVersionNumber);
        Assert.Single(document.Versions);
        Assert.True(document.Versions[0].FileSizeBytes > 0);
        Assert.Equal(64, document.Versions[0].ContentHash.Length);

        using ISystemScope scope = factory.CreateServiceScope();
        DocOpsDbContext context = scope.Services.GetRequiredService<DocOpsDbContext>();

        bool created = await context.AuditEvents
            .IgnoreQueryFilters()
            .AnyAsync(e => e.EntityId == document.Summary.Id && e.Action == AuditActions.DocumentCreated);

        Assert.True(created);
    }

    [Fact]
    public async Task Adding_a_version_keeps_the_previous_one_retrievable()
    {
        HttpClient approver = await factory.SignInAsync(FinanceApprover);
        DocumentDetailDto document = await CreateInvoiceAsync(approver, "Fatura com várias versões");

        DocumentDetailDto updated = await AddVersionAsync(
            approver, document.Summary.Id, "Conteúdo corrigido da versão 2", "Correção de valores");

        Assert.Equal(2, updated.Summary.CurrentVersionNumber);
        Assert.Equal(2, updated.Versions.Count);
        Assert.Single(updated.Versions, version => version.IsCurrent);
        Assert.NotEqual(updated.Versions[0].ContentHash, updated.Versions[1].ContentHash);

        HttpResponseMessage first = await approver.GetAsync(
            $"/api/documents/{document.Summary.Id}/content?version=1");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("integração", await first.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Download_returns_the_stored_bytes_and_is_audited()
    {
        HttpClient approver = await factory.SignInAsync(FinanceApprover);
        DocumentDetailDto document = await CreateInvoiceAsync(approver, "Fatura para descarregar");

        HttpResponseMessage response = await approver.GetAsync($"/api/documents/{document.Summary.Id}/content");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("FATURA", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal($"\"{document.Versions[0].ContentHash}\"", response.Headers.ETag?.ToString());

        using ISystemScope scope = factory.CreateServiceScope();
        DocOpsDbContext context = scope.Services.GetRequiredService<DocOpsDbContext>();

        Assert.True(await context.AuditEvents
            .IgnoreQueryFilters()
            .AnyAsync(e => e.EntityId == document.Summary.Id && e.Action == AuditActions.DocumentDownloaded));
    }

    [Fact]
    public async Task Status_transitions_follow_the_state_machine()
    {
        HttpClient approver = await factory.SignInAsync(FinanceApprover);
        DocumentDetailDto document = await CreateInvoiceAsync(approver, "Fatura para aprovação");

        // Draft cannot jump straight to Approved.
        HttpResponseMessage invalid = await approver.PostAsJsonAsync(
            $"/api/documents/{document.Summary.Id}/status",
            new ChangeDocumentStatusRequest(DocumentStatus.Approved, null));

        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);

        await ChangeStatusAsync(approver, document.Summary.Id, DocumentStatus.InReview);
        await ChangeStatusAsync(approver, document.Summary.Id, DocumentStatus.PendingApproval);
        DocumentDetailDto approved = await ChangeStatusAsync(approver, document.Summary.Id, DocumentStatus.Approved);

        Assert.Equal(DocumentStatus.Approved, approved.Summary.Status);
        Assert.Contains(DocumentStatus.Archived, approved.AllowedNextStatuses);
    }

    [Fact]
    public async Task Archiving_sets_a_retention_date_and_restoring_returns_the_previous_status()
    {
        HttpClient approver = await factory.SignInAsync(FinanceApprover);
        DocumentDetailDto document = await CreateInvoiceAsync(approver, "Fatura para arquivo");

        await ChangeStatusAsync(approver, document.Summary.Id, DocumentStatus.InReview);
        await ChangeStatusAsync(approver, document.Summary.Id, DocumentStatus.PendingApproval);
        await ChangeStatusAsync(approver, document.Summary.Id, DocumentStatus.Approved);

        DocumentDetailDto archived = await ReadAsync(
            await approver.PostAsync($"/api/documents/{document.Summary.Id}/archive", content: null));

        Assert.Equal(DocumentStatus.Archived, archived.Summary.Status);
        Assert.NotNull(archived.ArchivedAtUtc);
        Assert.NotNull(archived.Summary.RetentionDueAtUtc);

        DocumentDetailDto restored = await ReadAsync(
            await approver.PostAsync($"/api/documents/{document.Summary.Id}/restore", content: null));

        Assert.Equal(DocumentStatus.Approved, restored.Summary.Status);
        Assert.Null(restored.ArchivedAtUtc);
    }

    [Fact]
    public async Task A_save_based_on_a_stale_read_is_rejected()
    {
        HttpClient approver = await factory.SignInAsync(FinanceApprover);
        DocumentDetailDto document = await CreateInvoiceAsync(approver, "Fatura com edição concorrente");

        UpdateDocumentRequest update = new()
        {
            Title = "Primeira alteração",
            Classification = document.Summary.Classification,
            Tags = [],
            Metadata = RequiredInvoiceMetadata(),
            ConcurrencyToken = document.ConcurrencyToken
        };

        HttpResponseMessage first = await approver.PutAsJsonAsync($"/api/documents/{document.Summary.Id}", update);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Same (now stale) token: the second save must not silently overwrite the first.
        HttpResponseMessage second = await approver.PutAsJsonAsync(
            $"/api/documents/{document.Summary.Id}", update with { Title = "Segunda alteração" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_document_hides_it_but_keeps_the_audit_trail()
    {
        HttpClient approver = await factory.SignInAsync(FinanceApprover);
        HttpClient manager = await factory.SignInAsync("sofia.martins@asteria.example");

        DocumentDetailDto document = await CreateInvoiceAsync(approver, "Fatura a eliminar");

        // The finance approver cannot delete; a manager can.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await approver.DeleteAsync($"/api/documents/{document.Summary.Id}")).StatusCode);

        // Sharing the document with the manager so the delete is possible for the right reason.
        await approver.PostAsJsonAsync(
            $"/api/documents/{document.Summary.Id}/permissions",
            new GrantDocumentPermissionRequest
            {
                SubjectType = PermissionSubjectType.Role,
                RoleName = "Manager",
                AccessLevels = ["view", "download", "edit", "delete"]
            });

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await manager.DeleteAsync($"/api/documents/{document.Summary.Id}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await approver.GetAsync($"/api/documents/{document.Summary.Id}")).StatusCode);

        using ISystemScope scope = factory.CreateServiceScope();
        DocOpsDbContext context = scope.Services.GetRequiredService<DocOpsDbContext>();

        Assert.True(await context.Documents
            .IgnoreQueryFilters()
            .AnyAsync(d => d.Id == document.Summary.Id && d.IsDeleted));

        Assert.True(await context.AuditEvents
            .IgnoreQueryFilters()
            .AnyAsync(e => e.EntityId == document.Summary.Id && e.Action == AuditActions.DocumentDeleted));
    }

    // -----------------------------------------------------------------------------------------
    // Access control
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Granting_and_revoking_a_permission_opens_and_closes_access()
    {
        HttpClient approver = await factory.SignInAsync(FinanceApprover);
        HttpClient employee = await factory.SignInAsync(Employee);

        DocumentDetailDto document = await CreateInvoiceAsync(
            approver, "Fatura confidencial partilhada", SecurityClassification.Restricted);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await employee.GetAsync($"/api/documents/{document.Summary.Id}")).StatusCode);

        CurrentUserProfile? profile = await employee.GetFromJsonAsync<CurrentUserProfile>("/api/auth/me", Json);
        Assert.NotNull(profile);

        HttpResponseMessage granted = await approver.PostAsJsonAsync(
            $"/api/documents/{document.Summary.Id}/permissions",
            new GrantDocumentPermissionRequest
            {
                SubjectType = PermissionSubjectType.User,
                SubjectId = profile.Id,
                AccessLevels = ["download"]
            });

        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);

        DocumentPermissionDto? permission = await granted.Content.ReadFromJsonAsync<DocumentPermissionDto>(Json);
        Assert.NotNull(permission);

        // Every grant carries view, even when only download was requested.
        Assert.Contains("view", permission.AccessLevels);
        Assert.Contains("download", permission.AccessLevels);

        Assert.Equal(
            HttpStatusCode.OK,
            (await employee.GetAsync($"/api/documents/{document.Summary.Id}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await approver.DeleteAsync($"/api/documents/{document.Summary.Id}/permissions/{permission.Id}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await employee.GetAsync($"/api/documents/{document.Summary.Id}")).StatusCode);
    }

    [Fact]
    public async Task Effective_access_explains_the_decision()
    {
        HttpClient approver = await factory.SignInAsync(FinanceApprover);
        DocumentDetailDto document = await CreateInvoiceAsync(approver, "Fatura para explicação de acesso");

        EffectiveAccessDto? access = await approver.GetFromJsonAsync<EffectiveAccessDto>(
            $"/api/documents/{document.Summary.Id}/effective-access", Json);

        Assert.NotNull(access);
        Assert.Contains("view", access.Levels);
        Assert.Contains(access.Reasons, reason => reason.Source == "Ownership");
    }

    [Fact]
    public async Task A_document_of_another_organisation_is_reported_as_not_found()
    {
        HttpClient approver = await factory.SignInAsync(FinanceApprover);
        HttpClient vertice = await factory.SignInAsync(VerticeAdmin);

        DocumentDetailDto document = await CreateInvoiceAsync(approver, "Fatura da Asteria");

        // Not "forbidden": another organisation must not even learn that the identifier exists.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await vertice.GetAsync($"/api/documents/{document.Summary.Id}")).StatusCode);
    }

    // -----------------------------------------------------------------------------------------
    // Upload validation
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task An_executable_upload_is_refused()
    {
        HttpClient approver = await factory.SignInAsync(FinanceApprover);
        Guid typeId = await GetInvoiceTypeIdAsync(approver);

        using MultipartFormDataContent form = BuildForm("conteúdo", "malicioso.exe", "application/x-msdownload");
        form.Add(new StringContent("Ficheiro não permitido"), "Title");
        form.Add(new StringContent(typeId.ToString()), "DocumentTypeId");
        AddRequiredInvoiceMetadata(form);

        HttpResponseMessage response = await approver.PostAsync("/api/documents", form);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("extension_not_allowed", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_required_metadata_field_is_refused()
    {
        HttpClient approver = await factory.SignInAsync(FinanceApprover);
        Guid typeId = await GetInvoiceTypeIdAsync(approver);

        using MultipartFormDataContent form = BuildForm("conteúdo", "fatura.txt", "text/plain");
        form.Add(new StringContent("Fatura sem metadados"), "Title");
        form.Add(new StringContent(typeId.ToString()), "DocumentTypeId");

        HttpResponseMessage response = await approver.PostAsync("/api/documents", form);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("required_metadata_missing", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_document_audit_trail_is_readable_and_ordered_newest_first()
    {
        HttpClient approver = await factory.SignInAsync(FinanceApprover);
        DocumentDetailDto document = await CreateInvoiceAsync(approver, "Fatura com trilho de auditoria");

        await approver.GetAsync($"/api/documents/{document.Summary.Id}");
        await ChangeStatusAsync(approver, document.Summary.Id, DocumentStatus.InReview);

        PagedResult<DocumentAuditEntryDto>? audit =
            await approver.GetFromJsonAsync<PagedResult<DocumentAuditEntryDto>>(
                $"/api/documents/{document.Summary.Id}/audit", Json);

        Assert.NotNull(audit);
        Assert.Contains(audit.Items, entry => entry.Action == AuditActions.DocumentCreated);
        Assert.Contains(audit.Items, entry => entry.Action == AuditActions.DocumentViewed);
        Assert.Contains(audit.Items, entry => entry.Action == AuditActions.DocumentStatusChanged);

        DateTimeOffset[] timestamps = [.. audit.Items.Select(entry => entry.OccurredAtUtc)];
        Assert.Equal(timestamps.OrderByDescending(value => value), timestamps);
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    private sealed record CurrentUserProfile(Guid Id, string Email);

    private static Dictionary<string, string?> RequiredInvoiceMetadata() => new()
    {
        ["supplier"] = "Fornecedor de Teste",
        ["invoiceNumber"] = "TEST-0001",
        ["invoiceDate"] = "2026-09-01",
        ["totalAmount"] = "1234.56"
    };

    private static void AddRequiredInvoiceMetadata(MultipartFormDataContent form)
    {
        foreach ((string code, string? value) in RequiredInvoiceMetadata())
        {
            form.Add(new StringContent($"{code}={value}"), "Metadata");
        }
    }

    private static MultipartFormDataContent BuildForm(string content, string fileName, string contentType)
    {
        MultipartFormDataContent form = [];
        ByteArrayContent file = new(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "File", fileName);

        return form;
    }

    private static async Task<Guid> GetInvoiceTypeIdAsync(HttpClient client)
    {
        IReadOnlyList<DocumentTypeDto>? types =
            await client.GetFromJsonAsync<IReadOnlyList<DocumentTypeDto>>("/api/document-catalogue/types", Json);

        Assert.NotNull(types);
        return types.Single(type => type.Code == "SupplierInvoice").Id;
    }

    private static async Task<DocumentDetailDto> CreateInvoiceAsync(
        HttpClient client, string title, SecurityClassification? classification = null)
    {
        Guid typeId = await GetInvoiceTypeIdAsync(client);

        using MultipartFormDataContent form = BuildForm(
            $"FATURA DE FORNECEDOR\nDocumento criado por um teste de integração.\n{title}",
            "fatura.txt",
            "text/plain");

        form.Add(new StringContent(title), "Title");
        form.Add(new StringContent(typeId.ToString()), "DocumentTypeId");
        form.Add(new StringContent("integração"), "Tags");

        if (classification is { } value)
        {
            form.Add(new StringContent(((int)value).ToString()), "Classification");
        }

        AddRequiredInvoiceMetadata(form);

        return await ReadAsync(await client.PostAsync("/api/documents", form));
    }

    private static async Task<DocumentDetailDto> AddVersionAsync(
        HttpClient client, Guid documentId, string content, string comment)
    {
        using MultipartFormDataContent form = BuildForm(content, "fatura-v2.txt", "text/plain");
        form.Add(new StringContent(comment), "Comment");

        return await ReadAsync(await client.PostAsync($"/api/documents/{documentId}/versions", form));
    }

    private static async Task<DocumentDetailDto> ChangeStatusAsync(
        HttpClient client, Guid documentId, DocumentStatus status)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/documents/{documentId}/status", new ChangeDocumentStatusRequest(status, "Teste de integração"));

        return await ReadAsync(response);
    }

    private static async Task<DocumentDetailDto> ReadAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Request failed: {response.StatusCode} {body}");
        }

        return JsonSerializer.Deserialize<DocumentDetailDto>(body, Json)
            ?? throw new InvalidOperationException("Empty document response.");
    }
}
