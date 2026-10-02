using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DocOps.Application.Documents;
using DocOps.Application.Intelligence;
using DocOps.Application.Workflows;
using DocOps.Domain.Documents;
using DocOps.Domain.Intelligence;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Intelligence;
using Microsoft.Extensions.DependencyInjection;
using DocOps.Application.Abstractions;

namespace DocOps.IntegrationTests;

/// <summary>
/// Document intelligence through the API, against a real database: a file uploaded before anyone read
/// it, suggestions that stay suggestions until a person decides, every decision in the audit trail,
/// and the same rules for a workflow step as for a person pressing a button.
/// </summary>
[Collection(ApiCollection.Name)]
public class AiProcessingTests(DocOpsApiFactory factory)
{
    private const string TenantAdmin = "admin@asteria.example";
    private const string Procurement = "ana.ferreira@asteria.example";
    private const string Legal = "carla.nunes@asteria.example";
    private const string ExternalPartner = "contacto@northstar.example";
    private const string VerticeAdmin = "admin@vertice.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task An_unclassified_upload_is_read_and_suggested_but_nothing_is_written_until_a_person_decides()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);
        DocumentDetailDto document = await UploadIntakeAsync(buyer, "fatura-atlas-tecnologia.txt");

        AiRunDto run = await ProcessAsync(buyer, document.Summary.Id);

        Assert.Equal(AiRunStatus.AwaitingReview, run.Status);
        Assert.Equal(AiSource.Demo, run.Source);
        Assert.Equal("SupplierInvoice", run.SuggestedType?.Code);
        Assert.Equal(ConfidenceBand.High, run.ClassificationBand);

        AiFieldSuggestionDto total = run.Fields.Single(f => f.FieldCode == "totalAmount");
        Assert.Equal("1906.50", total.SuggestedValue);
        Assert.NotNull(total.Evidence);
        Assert.Contains("Total a pagar", total.Evidence.Excerpt);

        // A suggestion is not a value: the document is exactly as it was uploaded.
        DocumentDetailDto unchanged = await GetDocumentAsync(buyer, document.Summary.Id);
        Assert.Equal(DocumentTypeCodes.Unclassified, unchanged.Summary.TypeCode);
        Assert.Empty(unchanged.Metadata);
    }

    [Fact]
    public async Task Applying_is_refused_until_the_classification_is_decided()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);
        DocumentDetailDto document = await UploadIntakeAsync(buyer, "fatura-atlas-tecnologia.txt");
        AiRunDto run = await ProcessAsync(buyer, document.Summary.Id);

        await AcceptAllFieldsOnlyAsync(buyer, run);

        HttpResponseMessage response = await buyer.PostAsync($"/api/ai/runs/{run.Id}/apply", null);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "ai_classification_undecided");
    }

    [Fact]
    public async Task Applying_reclassifies_the_document_writes_the_reviewed_values_and_audits_every_decision()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);
        DocumentDetailDto document = await UploadIntakeAsync(buyer, "fatura-atlas-tecnologia.txt");
        AiRunDto run = await ProcessAsync(buyer, document.Summary.Id);

        run = await PostAsync<AiRunDto>(buyer, $"/api/ai/runs/{run.Id}/accept-all", null);

        AiFieldSuggestionDto due = run.Fields.Single(f => f.FieldCode == "dueDate");
        run = await PostAsync<AiRunDto>(buyer, $"/api/ai/runs/{run.Id}/fields/{due.Id}/decision",
            new DecideFieldRequest(AiSuggestionDecision.Edited, "2026-10-10"));

        AiFieldSuggestionDto order = run.Fields.Single(f => f.FieldCode == "purchaseOrder");
        run = await PostAsync<AiRunDto>(buyer, $"/api/ai/runs/{run.Id}/fields/{order.Id}/decision",
            new DecideFieldRequest(AiSuggestionDecision.Rejected, null));

        Assert.True(run.CanApply);
        AiRunDto applied = await PostAsync<AiRunDto>(buyer, $"/api/ai/runs/{run.Id}/apply", null);
        Assert.Equal(AiRunStatus.Applied, applied.Status);

        DocumentDetailDto after = await GetDocumentAsync(buyer, document.Summary.Id);
        Assert.Equal("SupplierInvoice", after.Summary.TypeCode);

        DocumentMetadataValueDto total = after.Metadata.Single(m => m.Code == "totalAmount");
        DocumentMetadataValueDto dueDate = after.Metadata.Single(m => m.Code == "dueDate");
        DocumentMetadataValueDto purchaseOrder = after.Metadata.Single(m => m.Code == "purchaseOrder");

        Assert.Equal(1906.50m, decimal.Parse(total.Value!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.NotNull(total.AiConfidence);

        // A corrected value was typed by a person, so it carries no AI confidence.
        Assert.StartsWith("2026-10-10", dueDate.Value);
        Assert.Null(dueDate.AiConfidence);

        // A rejected suggestion writes nothing.
        Assert.Null(purchaseOrder.Value);

        // Sensitivity is never lowered by reclassifying: the intake type is confidential.
        Assert.Equal(SecurityClassification.Confidential, after.Summary.Classification);

        List<string> actions = await AuditActionsAsync(buyer, document.Summary.Id);
        Assert.Contains("ai.run.created", actions);
        Assert.Contains("ai.classification.accepted", actions);
        Assert.Contains("ai.suggestion.accepted", actions);
        Assert.Contains("ai.suggestion.edited", actions);
        Assert.Contains("ai.suggestion.rejected", actions);
        Assert.Contains("document.reclassified", actions);
        Assert.Contains("ai.run.applied", actions);
    }

    [Fact]
    public async Task A_corrected_value_is_validated_by_the_field_rules_when_it_is_entered()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);
        DocumentDetailDto document = await UploadIntakeAsync(buyer, "fatura-atlas-tecnologia.txt");
        AiRunDto run = await ProcessAsync(buyer, document.Summary.Id);
        AiFieldSuggestionDto due = run.Fields.Single(f => f.FieldCode == "dueDate");

        HttpResponseMessage response = await buyer.PostAsJsonAsync(
            $"/api/ai/runs/{run.Id}/fields/{due.Id}/decision",
            new DecideFieldRequest(AiSuggestionDecision.Edited, "não é uma data"));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "invalid_metadata_value");
    }

    [Fact]
    public async Task A_required_field_the_document_does_not_contain_must_be_filled_in_before_applying()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);

        // An invoice with no invoice number anywhere in it.
        DocumentDetailDto document = await UploadIntakeTextAsync(buyer, "fatura-sem-numero.txt", """
            Atlas Tecnologia, Lda.
            FATURA
            ======
            Data de emissão: 03/09/2026
            Total a pagar: 250,00 €
            """);

        AiRunDto run = await ProcessAsync(buyer, document.Summary.Id);
        AiFieldSuggestionDto number = run.Fields.Single(f => f.FieldCode == "invoiceNumber");
        Assert.Null(number.SuggestedValue);

        run = await PostAsync<AiRunDto>(buyer, $"/api/ai/runs/{run.Id}/accept-all", null);
        Assert.Contains("Número da fatura", run.MissingRequiredFields);
        Assert.False(run.CanApply);

        HttpResponseMessage refused = await buyer.PostAsync($"/api/ai/runs/{run.Id}/apply", null);
        await AssertProblemAsync(refused, HttpStatusCode.Conflict, "ai_required_fields_missing");

        run = await PostAsync<AiRunDto>(buyer, $"/api/ai/runs/{run.Id}/fields/{number.Id}/decision",
            new DecideFieldRequest(AiSuggestionDecision.Edited, "FT 2026/0999"));

        Assert.True(run.CanApply);
        Assert.Equal(AiRunStatus.Applied, (await PostAsync<AiRunDto>(buyer, $"/api/ai/runs/{run.Id}/apply", null)).Status);
    }

    [Fact]
    public async Task An_analysis_of_an_older_version_cannot_be_applied_to_newer_content()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);
        DocumentDetailDto document = await UploadIntakeAsync(buyer, "fatura-atlas-tecnologia.txt");
        AiRunDto run = await ProcessAsync(buyer, document.Summary.Id);

        using MultipartFormDataContent form = [];
        ByteArrayContent file = new("FATURA revista"u8.ToArray());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(file, "File", "revista.txt");
        (await buyer.PostAsync($"/api/documents/{document.Summary.Id}/versions", form)).EnsureSuccessStatusCode();

        await PostAsync<AiRunDto>(buyer, $"/api/ai/runs/{run.Id}/classification", new DecideClassificationRequest(true));
        await PostAsync<AiRunDto>(buyer, $"/api/ai/runs/{run.Id}/accept-all", null);

        AiRunDto stale = await GetAsync<AiRunDto>(buyer, $"/api/ai/runs/{run.Id}");
        Assert.True(stale.IsStale);
        Assert.False(stale.CanApply);

        HttpResponseMessage response = await buyer.PostAsync($"/api/ai/runs/{run.Id}/apply", null);
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "ai_run_stale");
    }

    [Fact]
    public async Task A_person_who_cannot_edit_the_document_cannot_have_it_processed_or_see_its_analysis()
    {
        HttpClient lawyer = await factory.SignInAsync(Legal);
        HttpClient buyer = await factory.SignInAsync(Procurement);
        HttpClient partner = await factory.SignInAsync(ExternalPartner);
        HttpClient otherTenant = await factory.SignInAsync(VerticeAdmin);

        DocumentDetailDto contract = await UploadIntakeAsync(lawyer, "contrato-nordhavn-logistica.txt");
        AiRunDto run = await ProcessAsync(lawyer, contract.Summary.Id);

        // Confidential and owned by Legal: Procurement cannot open it, so cannot analyse or read it.
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.PostAsync($"/api/documents/{contract.Summary.Id}/process-ai", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.GetAsync($"/api/ai/runs/{run.Id}")).StatusCode);

        HttpResponseMessage asked = await buyer.PostAsJsonAsync(
            "/api/ai/document-question", new AskDocumentRequest(contract.Summary.Id, "Quem é a contraparte?"));
        Assert.Equal(HttpStatusCode.Forbidden, asked.StatusCode);

        // External partners do not hold the AI permission at all.
        Assert.Equal(HttpStatusCode.Forbidden, (await partner.GetAsync("/api/ai/provider")).StatusCode);

        // Another organisation cannot even learn that the run exists.
        Assert.Equal(HttpStatusCode.NotFound, (await otherTenant.GetAsync($"/api/ai/runs/{run.Id}")).StatusCode);
    }

    [Fact]
    public async Task The_review_queue_and_its_statistics_only_count_documents_the_caller_can_see()
    {
        HttpClient lawyer = await factory.SignInAsync(Legal);
        HttpClient buyer = await factory.SignInAsync(Procurement);

        DocumentDetailDto contract = await UploadIntakeAsync(lawyer, "contrato-nordhavn-logistica.txt");
        AiRunDto run = await ProcessAsync(lawyer, contract.Summary.Id);

        Application.Common.PagedResult<AiRunListItemDto> lawyersQueue =
            await GetAsync<Application.Common.PagedResult<AiRunListItemDto>>(lawyer, "/api/ai/runs?statuses=1&pageSize=100");
        Application.Common.PagedResult<AiRunListItemDto> buyersQueue =
            await GetAsync<Application.Common.PagedResult<AiRunListItemDto>>(buyer, "/api/ai/runs?statuses=1&pageSize=100");

        Assert.Contains(lawyersQueue.Items, r => r.Id == run.Id);
        Assert.DoesNotContain(buyersQueue.Items, r => r.Id == run.Id);

        AiStatisticsDto lawyersStats = await GetAsync<AiStatisticsDto>(lawyer, "/api/ai/statistics");
        AiStatisticsDto buyersStats = await GetAsync<AiStatisticsDto>(buyer, "/api/ai/statistics");

        Assert.Equal(lawyersQueue.TotalCount, lawyersStats.AwaitingReview);
        Assert.Equal(buyersQueue.TotalCount, buyersStats.AwaitingReview);
    }

    [Fact]
    public async Task A_question_is_answered_from_the_text_with_a_citation_that_is_really_there()
    {
        HttpClient buyer = await factory.SignInAsync(Procurement);
        DocumentDetailDto document = await UploadIntakeAsync(buyer, "fatura-atlas-tecnologia.txt");

        DocumentAnswerDto answer = await PostAsync<DocumentAnswerDto>(buyer, "/api/ai/document-question",
            new AskDocumentRequest(document.Summary.Id, "Qual é o total da fatura?"));

        Assert.True(answer.Answered);
        Assert.Equal(AiSource.Demo, answer.Source);
        Assert.Contains("906,50", answer.Text);
        AiCitationDto citation = Assert.Single(answer.Citations);
        Assert.Equal("document", citation.Kind);
        Assert.Equal("fatura-atlas-tecnologia.txt", citation.FileName);
        Assert.Contains("Total a pagar", citation.Excerpt);
        Assert.Equal(0, answer.DiscardedCitations);

        DocumentAnswerDto unknown = await PostAsync<DocumentAnswerDto>(buyer, "/api/ai/document-question",
            new AskDocumentRequest(document.Summary.Id, "Qual é a cor do logótipo?"));

        Assert.False(unknown.Answered);
        Assert.Empty(unknown.Citations);
    }

    [Fact]
    public async Task Who_approved_a_document_is_answered_from_the_approval_records_not_from_the_text()
    {
        HttpClient approver = await factory.SignInAsync("ricardo.lopes@asteria.example");

        Application.Common.PagedResult<DocumentListItemDto>? page = await approver.GetFromJsonAsync<Application.Common.PagedResult<DocumentListItemDto>>(
            "/api/documents?query=" + Uri.EscapeDataString("Verdemar Serviços 2026/0219"), Json);
        DocumentListItemDto approved = page!.Items.Single(d => d.Title == "Fatura Verdemar Serviços 2026/0219");

        DocumentAnswerDto answer = await PostAsync<DocumentAnswerDto>(approver, "/api/ai/document-question",
            new AskDocumentRequest(approved.Id, "Quem aprovou este documento?"));

        Assert.True(answer.Answered);
        Assert.Contains("Ricardo Lopes", answer.Text);
        Assert.All(answer.Citations, c => Assert.Equal("audit", c.Kind));
    }

    [Fact]
    public async Task A_workflow_step_queues_an_analysis_the_worker_runs_and_the_process_continues_without_applying_it()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);
        DocumentDetailDto document = await UploadIntakeAsync(admin, "fatura-atlas-tecnologia.txt");
        Guid definitionId = await CreateAiWorkflowAsync(admin);

        WorkflowInstanceDetailDto started = await StartAsync(admin, definitionId, document.Summary.Id);

        // The step does not call the provider itself: it queues the analysis and waits.
        Assert.Equal(WorkflowInstanceStatus.Waiting, started.Summary.Status);
        Assert.Equal(["classificar"], started.ActiveNodeKeys);

        await RunWorkerAsync();

        WorkflowInstanceDetailDto finished = await GetAsync<WorkflowInstanceDetailDto>(admin, $"/api/workflows/instances/{started.Summary.Id}");

        Assert.Equal(WorkflowInstanceStatus.Completed, finished.Summary.Status);

        // The normal exit, not the "failed" one: a step that continues without choosing a branch
        // must not also take the branch edges beside its unconditional one.
        Assert.Equal("Analisado", finished.Summary.Outcome);
        Assert.Equal("SupplierInvoice", finished.Variables["ai.suggestedType"]);
        Assert.Equal("1906.50", finished.Variables["ai.totalAmount"]);
        Assert.Equal("0", finished.Variables["ai.requiredMissing"]);
        Assert.Contains(finished.Timeline, e => e.EventType == WorkflowEventTypes.AiRequested);
        Assert.Contains(finished.Timeline, e => e.EventType == WorkflowEventTypes.AiCompleted);

        // Both steps read the same analysis, and it waits for a person like any other.
        AiRunDto run = (await GetAsync<AiRunDto>(admin, $"/api/documents/{document.Summary.Id}/ai-runs/latest"))!;
        Assert.Equal(AiRunOrigin.Workflow, run.Origin);
        Assert.Equal(AiRunStatus.AwaitingReview, run.Status);
        Assert.Equal(DocumentTypeCodes.Unclassified, (await GetDocumentAsync(admin, document.Summary.Id)).Summary.TypeCode);
    }

    [Fact]
    public async Task A_document_without_text_sends_the_workflow_down_the_failed_branch()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        // A scanned PDF: stored, but with no text to read and no OCR to invent some.
        DocumentDetailDto scanned = await UploadIntakeBytesAsync(
            admin, "digitalizacao.pdf", "application/pdf", "%PDF-1.4\n%âãÏÓ\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray());

        Guid definitionId = await CreateAiWorkflowAsync(admin);
        WorkflowInstanceDetailDto started = await StartAsync(admin, definitionId, scanned.Summary.Id);

        await RunWorkerAsync();

        WorkflowInstanceDetailDto finished = await GetAsync<WorkflowInstanceDetailDto>(admin, $"/api/workflows/instances/{started.Summary.Id}");

        Assert.Equal(WorkflowInstanceStatus.Completed, finished.Summary.Status);
        Assert.Equal("Sem texto para analisar", finished.Summary.Outcome);
        Assert.Equal("failed", finished.Variables["ai.status"]);
        Assert.Contains(finished.Timeline, e => e.EventType == WorkflowEventTypes.AiFailed);
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    private async Task RunWorkerAsync()
    {
        // The scope has no signed-in user and no tenant, exactly like the worker process.
        using ISystemScope scope = factory.CreateServiceScope();
        await scope.Services.GetRequiredService<AiQueueProcessor>().ProcessQueuedAsync(CancellationToken.None);
    }

    /// <summary>Start → AI classification → AI extraction → end, with a "failed" exit to its own end.</summary>
    private static async Task<Guid> CreateAiWorkflowAsync(HttpClient client)
    {
        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/workflows", new CreateWorkflowRequest { Name = $"Triagem por IA {Guid.NewGuid():N}"[..30] });
        created.EnsureSuccessStatusCode();
        WorkflowDefinitionDetailDto draft = (await created.Content.ReadFromJsonAsync<WorkflowDefinitionDetailDto>(Json))!;

        SaveWorkflowRequest graph = new()
        {
            Name = draft.Summary.Name,
            Trigger = draft.Summary.Trigger,
            ConcurrencyToken = draft.ConcurrencyToken,
            Nodes =
            [
                new SaveWorkflowNodeRequest { Key = "inicio", Name = "Início", Type = WorkflowNodeType.Start },
                new SaveWorkflowNodeRequest { Key = "classificar", Name = "Classificar", Type = WorkflowNodeType.AiClassification },
                new SaveWorkflowNodeRequest { Key = "extrair", Name = "Extrair dados", Type = WorkflowNodeType.AiExtraction },
                new SaveWorkflowNodeRequest { Key = "fim", Name = "Analisado", Type = WorkflowNodeType.End, ConfigJson = """{"outcome":"Analisado"}""" },
                new SaveWorkflowNodeRequest { Key = "sem-texto", Name = "Sem texto", Type = WorkflowNodeType.End, ConfigJson = """{"outcome":"Sem texto para analisar"}""" }
            ],
            Edges =
            [
                new SaveWorkflowEdgeRequest { FromNodeKey = "inicio", ToNodeKey = "classificar" },
                new SaveWorkflowEdgeRequest { FromNodeKey = "classificar", ToNodeKey = "extrair" },
                new SaveWorkflowEdgeRequest { FromNodeKey = "classificar", ToNodeKey = "sem-texto", Branch = "failed" },
                new SaveWorkflowEdgeRequest { FromNodeKey = "extrair", ToNodeKey = "fim" }
            ]
        };

        HttpResponseMessage saved = await client.PutAsJsonAsync($"/api/workflows/{draft.Summary.Id}", graph);
        saved.EnsureSuccessStatusCode();

        HttpResponseMessage activated = await client.PostAsync($"/api/workflows/{draft.Summary.Id}/activate", null);
        if (!activated.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Activation failed: {await activated.Content.ReadAsStringAsync()}");
        }

        return draft.Summary.Id;
    }

    private static async Task<WorkflowInstanceDetailDto> StartAsync(HttpClient client, Guid definitionId, Guid documentId)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/workflows/instances", new StartWorkflowRequest { DefinitionId = definitionId, DocumentId = documentId });

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Start failed: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<WorkflowInstanceDetailDto>(Json))!;
    }

    private static Task<DocumentDetailDto> UploadIntakeAsync(HttpClient client, string sample) =>
        UploadIntakeTextAsync(client, sample, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", sample)));

    private static Task<DocumentDetailDto> UploadIntakeTextAsync(HttpClient client, string fileName, string text) =>
        UploadIntakeBytesAsync(client, fileName, "text/plain", Encoding.UTF8.GetBytes(text));

    /// <summary>Uploads a file as "Por classificar": no type chosen, no field filled in.</summary>
    private static async Task<DocumentDetailDto> UploadIntakeBytesAsync(
        HttpClient client, string fileName, string contentType, byte[] bytes)
    {
        IReadOnlyList<DocumentTypeDto>? types =
            await client.GetFromJsonAsync<IReadOnlyList<DocumentTypeDto>>("/api/document-catalogue/types", Json);
        Guid unclassified = types!.Single(t => t.Code == DocumentTypeCodes.Unclassified).Id;

        using MultipartFormDataContent form = [];
        ByteArrayContent file = new(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(file, "File", fileName);
        form.Add(new StringContent($"Documento recebido — {fileName} — {Guid.NewGuid():N}"), "Title");
        form.Add(new StringContent(unclassified.ToString()), "DocumentTypeId");

        HttpResponseMessage response = await client.PostAsync("/api/documents", form);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Upload failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<DocumentDetailDto>(Json))!;
    }

    private static Task<AiRunDto> ProcessAsync(HttpClient client, Guid documentId) =>
        PostAsync<AiRunDto>(client, $"/api/documents/{documentId}/process-ai", null);

    /// <summary>Accepts each found value one by one, leaving the classification undecided.</summary>
    private static async Task AcceptAllFieldsOnlyAsync(HttpClient client, AiRunDto run)
    {
        foreach (AiFieldSuggestionDto field in run.Fields.Where(f => f.SuggestedValue is not null))
        {
            await PostAsync<AiRunDto>(client, $"/api/ai/runs/{run.Id}/fields/{field.Id}/decision",
                new DecideFieldRequest(AiSuggestionDecision.Accepted, null));
        }
    }

    private static async Task<DocumentDetailDto> GetDocumentAsync(HttpClient client, Guid documentId) =>
        (await client.GetFromJsonAsync<DocumentDetailDto>($"/api/documents/{documentId}", Json))!;

    private static async Task<List<string>> AuditActionsAsync(HttpClient client, Guid documentId)
    {
        Application.Common.PagedResult<DocumentAuditEntryDto>? audit =
            await client.GetFromJsonAsync<Application.Common.PagedResult<DocumentAuditEntryDto>>(
                $"/api/documents/{documentId}/audit?pageSize=100", Json);

        return [.. audit!.Items.Select(e => e.Action)];
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string path)
    {
        HttpResponseMessage response = await client.GetAsync(path);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GET {path} failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object? body)
    {
        HttpResponseMessage response = body is null
            ? await client.PostAsync(path, null)
            : await client.PostAsync(path, JsonContent.Create(body, body.GetType(), options: Json));

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"POST {path} failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private sealed record ProblemDocument(string? Code, string? Detail);

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"expected {status}, got {response.StatusCode}: {body}");

        ProblemDocument? problem = JsonSerializer.Deserialize<ProblemDocument>(body, Json);
        Assert.Equal(code, problem?.Code);
    }
}
