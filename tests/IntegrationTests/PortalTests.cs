using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Application.Portal;
using DocOps.Application.Workflows;
using DocOps.Domain.Workflows;

namespace DocOps.IntegrationTests;

/// <summary>
/// The external portal against a real SQL Server.
///
/// The thing most worth proving here is a negative: that a supplier, who is the only kind of user
/// this application lets in from outside, cannot reach anything that is not its own. The portal has
/// no authorisation of its own — it reuses the document access evaluator — so these tests are as
/// much about that reuse still holding as about the portal's own behaviour.
/// </summary>
[Collection(ApiCollection.Name)]
public class PortalTests(DocOpsApiFactory factory)
{
    private const string Supplier = "contacto@northstar.example";
    private const string TenantAdmin = "admin@asteria.example";
    private const string Finance = "ricardo.lopes@asteria.example";
    private const string Procurement = "ana.ferreira@asteria.example";
    private const string VerticeAdmin = "admin@vertice.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // -----------------------------------------------------------------------------------------
    // Who gets in
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task An_internal_user_has_no_business_in_the_portal()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        // The separation runs both ways on purpose: no internal role holds portal.access, however
        // senior. A tenant administrator is the strongest case there is, and it is still refused.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/portal/submissions")).StatusCode);
    }

    [Fact]
    public async Task A_supplier_reaches_the_portal_and_nothing_else()
    {
        HttpClient supplier = await factory.SignInAsync(Supplier);

        Assert.Equal(HttpStatusCode.OK, (await supplier.GetAsync("/api/portal/submissions")).StatusCode);

        // Every internal surface, refused. These are the screens a supplier might guess the address
        // of, and the refusal is the server's, not the interface's.
        Assert.Equal(HttpStatusCode.Forbidden, (await supplier.GetAsync("/api/inbox")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await supplier.GetAsync("/api/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await supplier.GetAsync("/api/integrations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await supplier.GetAsync("/api/workflows")).StatusCode);
    }

    [Fact]
    public async Task A_supplier_sees_only_what_is_its_own()
    {
        HttpClient supplier = await factory.SignInAsync(Supplier);
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        IReadOnlyList<PortalSubmissionDto> mine = await SubmissionsAsync(supplier);
        PagedResult<DocumentListItemDto> everything =
            (await admin.GetFromJsonAsync<PagedResult<DocumentListItemDto>>("/api/documents?pageSize=200", Json))!;

        // The organisation has a repository; the supplier sees a handful of it. Not "fewer" by
        // coincidence — every one it does see is one it owns or was explicitly granted.
        Assert.NotEmpty(mine);
        Assert.True(mine.Count < everything.TotalCount);

        foreach (PortalSubmissionDto submission in mine)
        {
            DocumentDetailDto document =
                (await admin.GetFromJsonAsync<DocumentDetailDto>($"/api/documents/{submission.Id}", Json))!;

            Assert.Equal(submission.Reference, document.Summary.Reference);
        }
    }

    [Fact]
    public async Task One_organisation_cannot_read_another_ones_submission()
    {
        HttpClient supplier = await factory.SignInAsync(Supplier);
        PortalSubmissionDto submission = await SubmitAsync(supplier, "Fatura de isolamento");

        HttpClient vertice = await factory.SignInAsync(VerticeAdmin);

        // Forbidden rather than not-found here because the caller is internal to another
        // organisation: it never gets as far as the document, the permission stops it first.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await vertice.GetAsync($"/api/portal/submissions/{submission.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_supplier_cannot_read_a_document_that_is_not_its_own()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);
        HttpClient supplier = await factory.SignInAsync(Supplier);

        PagedResult<DocumentListItemDto> all =
            (await admin.GetFromJsonAsync<PagedResult<DocumentListItemDto>>("/api/documents?pageSize=200", Json))!;
        IReadOnlyList<PortalSubmissionDto> mine = await SubmissionsAsync(supplier);

        Guid someoneElses = all.Items.First(d => mine.All(m => m.Id != d.Id)).Id;

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await supplier.GetAsync($"/api/portal/submissions/{someoneElses}")).StatusCode);

        // And not through the back door of the download either.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await supplier.GetAsync($"/api/portal/submissions/{someoneElses}/content")).StatusCode);
    }

    // -----------------------------------------------------------------------------------------
    // Submitting
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_submission_becomes_a_real_document_the_organisation_can_work_on()
    {
        HttpClient supplier = await factory.SignInAsync(Supplier);
        PortalSubmissionDto submission = await SubmitAsync(supplier, "Fatura NS-2026-1000");

        Assert.True(submission.SubmittedHere);
        Assert.Equal(PortalProgress.Received, submission.Progress);
        Assert.False(submission.AwaitingResponse);

        // It is a document like any other: created through IDocumentService, with its own reference.
        // Read by finance, because that is the department a portal submission lands in — the intake
        // type is confidential, so nobody outside that department reaches it by classification.
        HttpClient buyer = await factory.SignInAsync(Finance);
        DocumentDetailDto document =
            (await buyer.GetFromJsonAsync<DocumentDetailDto>($"/api/documents/{submission.Id}", Json))!;

        Assert.Equal(submission.Reference, document.Summary.Reference);
        Assert.Equal("Northstar Supplies", document.BusinessEntity);

        // And workable, not merely visible. The receiving department gets contributor access on
        // submission precisely so that a process can be started on it — reading it would not be
        // enough, and a submission nobody can act on is a submission into a display case.
        HttpResponseMessage started = await buyer.PostAsJsonAsync(
            "/api/workflows/instances",
            new StartWorkflowRequest { DefinitionId = await InvoiceDefinitionAsync(buyer), DocumentId = submission.Id },
            Json);

        Assert.True(
            started.IsSuccessStatusCode,
            $"não foi possível iniciar processo: {started.StatusCode} {await started.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task A_supplier_can_read_back_the_file_it_sent()
    {
        HttpClient supplier = await factory.SignInAsync(Supplier);
        PortalSubmissionDto submission = await SubmitAsync(supplier, "Fatura para reler", "CONTEÚDO ORIGINAL");

        HttpResponseMessage download = await supplier.GetAsync($"/api/portal/submissions/{submission.Id}/content");
        download.EnsureSuccessStatusCode();

        Assert.Contains("CONTEÚDO ORIGINAL", await download.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Watching_the_portal_is_one_permission_and_submitting_is_another()
    {
        HttpClient supplier = await factory.SignInAsync(Supplier);

        // The demo partner holds both, so this asserts the split is declared on the actions rather
        // than that a particular account lacks it: view on the reads, submit on the writes.
        Assert.Equal(HttpStatusCode.OK, (await supplier.GetAsync("/api/portal/submissions")).StatusCode);

        using MultipartFormDataContent empty = [];
        empty.Add(new StringContent("Sem ficheiro"), "Title");

        // No file at all is a validation failure, not an authorisation one — which is how we know
        // the request reached the action instead of being refused at the door.
        HttpResponseMessage response = await supplier.PostAsync("/api/portal/submissions", empty);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -----------------------------------------------------------------------------------------
    // The conversation about a submission
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Answering_a_question_nobody_asked_is_refused()
    {
        HttpClient supplier = await factory.SignInAsync(Supplier);
        PortalSubmissionDto submission = await SubmitAsync(supplier, "Fatura sem perguntas");

        HttpResponseMessage response = await supplier.PostAsJsonAsync(
            $"/api/portal/submissions/{submission.Id}/information",
            new ProvideInformationRequest { Body = "Aqui está a informação." },
            Json);

        // Otherwise text would appear on an approver's timeline out of nowhere, and the portal would
        // be a channel for unsolicited messages rather than an answer to a question.
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "no_open_question");
    }

    [Fact]
    public async Task A_question_reaches_the_supplier_and_the_answer_comes_back_without_moving_the_process()
    {
        HttpClient supplier = await factory.SignInAsync(Supplier);
        HttpClient buyer = await factory.SignInAsync(Procurement);

        HttpClient finance = await factory.SignInAsync(Finance);

        PortalSubmissionDto submission = await SubmitAsync(supplier, "Fatura com pergunta");

        // Finance receives the submission and starts the process; the first step of the seeded
        // invoice workflow is validation, which belongs to procurement — so it is procurement that
        // ends up asking the supplier the question.
        Guid instanceId = await StartProcessAsync(finance, submission.Id);
        TaskListItemDto task = await OpenTaskAsync(finance, instanceId);

        await buyer.PostAsJsonAsync(
            $"/api/tasks/{task.Id}/request-information",
            new { comment = "Falta o número de encomenda. Pode indicar?" },
            Json);

        // The supplier sees the question, and only the question: nothing else from the timeline.
        PortalSubmissionDetailDto asked = await DetailAsync(supplier, submission.Id);
        Assert.True(asked.Summary.AwaitingResponse);
        PortalMessageDto question = Assert.Single(asked.Messages);
        Assert.True(question.FromOrganisation);
        Assert.Contains("número de encomenda", question.Body, StringComparison.Ordinal);

        WorkflowInstanceDetailDto before = await InstanceAsync(finance, instanceId);

        PortalSubmissionDetailDto answered = await PostAsync<PortalSubmissionDetailDto>(
            supplier,
            $"/api/portal/submissions/{submission.Id}/information",
            new ProvideInformationRequest { Body = "É a ENC-2026-00045." });

        Assert.Equal(2, answered.Messages.Count);
        Assert.False(answered.Messages[1].FromOrganisation);
        Assert.False(answered.Summary.AwaitingResponse);

        // The answer is information, not a decision. A supplier must never be able to move its own
        // invoice forward by replying to a question about it — the person who asked decides.
        WorkflowInstanceDetailDto after = await InstanceAsync(finance, instanceId);
        Assert.Equal(before.Summary.Status, after.Summary.Status);
        Assert.Equal(before.ActiveNodeKeys, after.ActiveNodeKeys);

        // And it is on the internal timeline, where whoever asked will read it.
        Assert.Contains(
            after.Timeline,
            entry => entry.EventType == WorkflowEventTypes.InformationProvided
                && entry.Detail != null
                && entry.Detail.Contains("ENC-2026-00045", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_supplier_never_sees_the_rest_of_the_timeline()
    {
        HttpClient supplier = await factory.SignInAsync(Supplier);
        HttpClient finance = await factory.SignInAsync(Finance);

        PortalSubmissionDto submission = await SubmitAsync(supplier, "Fatura com histórico interno");
        Guid instanceId = await StartProcessAsync(finance, submission.Id);
        WorkflowInstanceDetailDto instance = await InstanceAsync(finance, instanceId);

        // The process has a timeline of its own already — nodes entered, task created, document
        // status applied — and the supplier is shown none of it.
        Assert.NotEmpty(instance.Timeline);

        PortalSubmissionDetailDto detail = await DetailAsync(supplier, submission.Id);
        Assert.Empty(detail.Messages);
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    private static async Task<IReadOnlyList<PortalSubmissionDto>> SubmissionsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<IReadOnlyList<PortalSubmissionDto>>("/api/portal/submissions", Json))!;

    private static async Task<PortalSubmissionDetailDto> DetailAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<PortalSubmissionDetailDto>($"/api/portal/submissions/{id}", Json))!;

    private static async Task<PortalSubmissionDto> SubmitAsync(
        HttpClient client, string title, string content = "FATURA de teste\nTotal: 100,00 EUR")
    {
        using MultipartFormDataContent form = [];
        form.Add(new StringContent(title), "Title");
        form.Add(new StringContent("Submetida por um teste de integração."), "Note");

        ByteArrayContent file = new(System.Text.Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "File", "teste.txt");

        HttpResponseMessage response = await client.PostAsync("/api/portal/submissions", form);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"submissão falhou: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<PortalSubmissionDto>(Json))!;
    }

    private static async Task<Guid> InvoiceDefinitionAsync(HttpClient client)
    {
        IReadOnlyList<WorkflowDefinitionSummaryDto> definitions =
            (await client.GetFromJsonAsync<IReadOnlyList<WorkflowDefinitionSummaryDto>>("/api/workflows", Json))!;

        return definitions.First(definition => definition.IsActive).Id;
    }

    private static async Task<Guid> StartProcessAsync(HttpClient client, Guid documentId)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/workflows/instances",
            new StartWorkflowRequest { DefinitionId = await InvoiceDefinitionAsync(client), DocumentId = documentId },
            Json);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"não foi possível iniciar processo: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        PagedResult<WorkflowInstanceListItemDto> instances =
            (await client.GetFromJsonAsync<PagedResult<WorkflowInstanceListItemDto>>(
                "/api/workflows/instances?page=1&pageSize=50", Json))!;

        return instances.Items.First(instance => instance.DocumentId == documentId).Id;
    }

    private static async Task<WorkflowInstanceDetailDto> InstanceAsync(HttpClient client, Guid instanceId) =>
        (await client.GetFromJsonAsync<WorkflowInstanceDetailDto>($"/api/workflows/instances/{instanceId}", Json))!;

    private static async Task<TaskListItemDto> OpenTaskAsync(HttpClient client, Guid instanceId)
    {
        WorkflowInstanceDetailDto instance = await InstanceAsync(client, instanceId);

        return instance.Tasks.First(task =>
            task.Status is WorkflowTaskStatus.Pending or WorkflowTaskStatus.InProgress);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body)
    {
        HttpResponseMessage response =
            await client.PostAsync(path, JsonContent.Create(body, body.GetType(), options: Json));

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"POST {path} falhou: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private sealed record ProblemDocument(string? Code, string? Detail);

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"esperado {status}, veio {response.StatusCode}: {body}");

        ProblemDocument? problem = JsonSerializer.Deserialize<ProblemDocument>(body, Json);
        Assert.Equal(code, problem?.Code);
    }
}
