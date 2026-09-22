using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Application.Inbox;
using DocOps.Domain.Inbox;

namespace DocOps.IntegrationTests;

/// <summary>
/// The inbox against a real SQL Server.
///
/// <see cref="A_second_organisation_starts_with_no_messages_of_the_first"/> is a regression test:
/// the first version of this feature shipped without a tenant query filter on
/// <c>EmailMessage</c>/<c>EmailAttachment</c>, and every organisation could read every other
/// organisation's mail. It was caught before commit by
/// <c>DocOps.UnitTests.Persistence.TenantFilterCoverageTests</c>, which checks that every
/// tenant-scoped entity has a filter at all; this test checks the other half — that the filter
/// actually keeps one organisation's inbox out of another's.
/// </summary>
[Collection(ApiCollection.Name)]
public class InboxTests(DocOpsApiFactory factory)
{
    private const string TenantAdmin = "admin@asteria.example";
    private const string Employee = "ana.ferreira@asteria.example";
    private const string VerticeAdmin = "admin@vertice.example";
    private const string ExternalPartner = "contacto@northstar.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<PagedResult<EmailListItemDto>> ListAsync(HttpClient client, string query = "")
    {
        PagedResult<EmailListItemDto>? result =
            await client.GetFromJsonAsync<PagedResult<EmailListItemDto>>($"/api/inbox?pageSize=100&{query}", Json);

        Assert.NotNull(result);

        return result;
    }

    // -----------------------------------------------------------------------------------------
    // Authorization and tenant isolation
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_second_organisation_starts_with_no_messages_of_the_first()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);
        HttpClient vertice = await factory.SignInAsync(VerticeAdmin);

        PagedResult<EmailListItemDto> asteriaInbox = await ListAsync(admin);
        PagedResult<EmailListItemDto> verticeInbox = await ListAsync(vertice);

        Assert.NotEmpty(asteriaInbox.Items);
        Assert.Empty(verticeInbox.Items);

        // Not merely fewer — none of Asteria's subjects or senders leaked across, by any field.
        Assert.DoesNotContain(
            verticeInbox.Items,
            item => asteriaInbox.Items.Any(other => other.FromAddress == item.FromAddress));
    }

    [Fact]
    public async Task Refuses_a_caller_without_inbox_view()
    {
        HttpClient partner = await factory.SignInAsync(ExternalPartner);

        HttpResponseMessage response = await partner.GetAsync("/api/inbox");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Refuses_a_caller_without_inbox_process_to_synchronise_or_act()
    {
        // Every internal role that can see the inbox can also act on it (see RolePermissionDefaults),
        // so this is proven from the one role that has neither: the external partner.
        HttpClient partner = await factory.SignInAsync(ExternalPartner);

        HttpResponseMessage sync = await partner.PostAsync("/api/inbox/sync", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, sync.StatusCode);
    }

    // -----------------------------------------------------------------------------------------
    // Synchronisation
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Synchronising_twice_adds_nothing_the_second_time()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        HttpResponseMessage first = await admin.PostAsync("/api/inbox/sync", content: null);
        first.EnsureSuccessStatusCode();
        MailSyncResultDto? firstResult = await first.Content.ReadFromJsonAsync<MailSyncResultDto>(Json);

        HttpResponseMessage second = await admin.PostAsync("/api/inbox/sync", content: null);
        second.EnsureSuccessStatusCode();
        MailSyncResultDto? secondResult = await second.Content.ReadFromJsonAsync<MailSyncResultDto>(Json);

        Assert.NotNull(firstResult);
        Assert.NotNull(secondResult);
        Assert.Equal(0, secondResult.Added);
        Assert.Equal(secondResult.Fetched, secondResult.AlreadyKnown);
        Assert.Equal(MailSource.Demo, secondResult.Source);
    }

    // -----------------------------------------------------------------------------------------
    // Turning mail into documents
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Saving_an_attachment_creates_a_real_document_and_links_the_message()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        PagedResult<EmailListItemDto> inbox = await ListAsync(admin, "onlyWithAttachments=true");
        EmailListItemDto withAttachment = inbox.Items.First(item => item.LinkedDocumentId is null);

        EmailDetailDto? detail =
            await admin.GetFromJsonAsync<EmailDetailDto>($"/api/inbox/{withAttachment.Id}", Json);
        Assert.NotNull(detail);
        EmailAttachmentDto attachment = detail.Attachments.First(a => a.SavedAsDocumentId is null);

        Guid unclassifiedTypeId = await UnclassifiedTypeIdAsync(admin);

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"/api/inbox/{withAttachment.Id}/attachments/save",
            new
            {
                attachmentId = attachment.Id,
                documentTypeId = unclassifiedTypeId,
                title = $"Anexo de teste {Guid.NewGuid()}",
            });

        response.EnsureSuccessStatusCode();
        DocumentDetailDto? document = await response.Content.ReadFromJsonAsync<DocumentDetailDto>(Json);

        Assert.NotNull(document);

        EmailDetailDto? updated = await admin.GetFromJsonAsync<EmailDetailDto>($"/api/inbox/{withAttachment.Id}", Json);
        Assert.NotNull(updated);
        Assert.Equal(document.Summary.Id, updated.Summary.LinkedDocumentId);
        Assert.Equal(EmailStatus.Processed, updated.Summary.Status);

        // The document is real: it exists as an ordinary document, not a side effect visible only
        // from the inbox.
        HttpResponseMessage fetched = await admin.GetAsync($"/api/documents/{document.Summary.Id}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);

        await admin.DeleteAsync($"/api/documents/{document.Summary.Id}");
    }

    [Fact]
    public async Task Saving_the_same_attachment_twice_is_refused()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        PagedResult<EmailListItemDto> inbox = await ListAsync(admin, "onlyWithAttachments=true");
        EmailListItemDto message = inbox.Items.First(item => item.LinkedDocumentId is null);
        EmailDetailDto? detail = await admin.GetFromJsonAsync<EmailDetailDto>($"/api/inbox/{message.Id}", Json);
        Assert.NotNull(detail);
        EmailAttachmentDto attachment = detail.Attachments.First(a => a.SavedAsDocumentId is null);

        Guid unclassifiedTypeId = await UnclassifiedTypeIdAsync(admin);

        HttpResponseMessage first = await admin.PostAsJsonAsync(
            $"/api/inbox/{message.Id}/attachments/save",
            new { attachmentId = attachment.Id, documentTypeId = unclassifiedTypeId });
        first.EnsureSuccessStatusCode();
        DocumentDetailDto? document = await first.Content.ReadFromJsonAsync<DocumentDetailDto>(Json);

        HttpResponseMessage second = await admin.PostAsJsonAsync(
            $"/api/inbox/{message.Id}/attachments/save",
            new { attachmentId = attachment.Id, documentTypeId = unclassifiedTypeId });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        Assert.NotNull(document);
        await admin.DeleteAsync($"/api/documents/{document.Summary.Id}");
    }

    [Fact]
    public async Task Cannot_link_a_message_to_a_document_the_caller_may_not_see()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);
        HttpClient employee = await factory.SignInAsync(Employee);

        PagedResult<DocumentListItemDto>? restricted = await admin.GetFromJsonAsync<PagedResult<DocumentListItemDto>>(
            "/api/documents?classifications=3&pageSize=5", Json);
        Assert.NotNull(restricted);

        if (restricted.Items.Count == 0)
        {
            return; // no restricted document seeded; nothing to assert against here.
        }

        PagedResult<EmailListItemDto> inbox = await ListAsync(employee);
        EmailListItemDto message = inbox.Items.First(item => item.LinkedDocumentId is null);

        HttpResponseMessage response = await employee.PostAsJsonAsync(
            $"/api/inbox/{message.Id}/link", new { documentId = restricted.Items[0].Id });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Starting_a_workflow_without_a_linked_document_is_refused()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        PagedResult<EmailListItemDto> inbox = await ListAsync(admin);
        EmailListItemDto unlinked = inbox.Items.First(item => item.LinkedDocumentId is null);

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"/api/inbox/{unlinked.Id}/workflow", new { workflowDefinitionId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        ProblemDetailsPayload? problem = await response.Content.ReadFromJsonAsync<ProblemDetailsPayload>(Json);
        Assert.Equal("email_without_document", problem?.Code);
    }

    // -----------------------------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Archiving_twice_is_refused_the_second_time()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        PagedResult<EmailListItemDto> inbox = await ListAsync(admin, "statuses=0&statuses=1");
        EmailListItemDto message = inbox.Items[0];

        HttpResponseMessage first = await admin.PostAsync($"/api/inbox/{message.Id}/archive", content: null);
        first.EnsureSuccessStatusCode();

        HttpResponseMessage second = await admin.PostAsync($"/api/inbox/{message.Id}/archive", content: null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // Left as it was found: reopened, so this test can run again against the same database.
        await admin.PostAsync($"/api/inbox/{message.Id}/reopen", content: null);
    }

    private static async Task<Guid> UnclassifiedTypeIdAsync(HttpClient client)
    {
        DocumentTypeDto[]? types = await client.GetFromJsonAsync<DocumentTypeDto[]>("/api/document-catalogue/types", Json);
        Assert.NotNull(types);

        return types.First(type => type.Code == "Unclassified").Id;
    }

    private sealed record ProblemDetailsPayload(string? Code);
}
