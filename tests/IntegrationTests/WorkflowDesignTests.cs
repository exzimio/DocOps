using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DocOps.Application.Documents;
using DocOps.Application.Workflows;
using DocOps.Domain.Workflows;

namespace DocOps.IntegrationTests;

/// <summary>
/// Exercises designing a process through the API, against a real database: what a draft starts as,
/// what saving does before and after a process has run, and what the server refuses outright.
/// </summary>
[Collection(ApiCollection.Name)]
public class WorkflowDesignTests(DocOpsApiFactory factory)
{
    private const string TenantAdmin = "admin@asteria.example";
    private const string Procurement = "ana.ferreira@asteria.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_new_process_starts_as_an_inactive_draft_that_already_runs_end_to_end()
    {
        HttpClient designer = await factory.SignInAsync(TenantAdmin);

        WorkflowDefinitionDetailDto draft = await CreateAsync(designer, "Processo acabado de criar");

        Assert.False(draft.Summary.IsActive);
        Assert.Equal(1, draft.Summary.Version);
        Assert.True(draft.Summary.IsLatestVersion);
        Assert.True(draft.Summary.IsEditableInPlace);

        // A start and an end, already connected: the canvas never opens on something unusable.
        Assert.Equal([WorkflowNodeType.Start, WorkflowNodeType.End], draft.Nodes.Select(node => node.Type));
        WorkflowEdgeDto edge = Assert.Single(draft.Edges);
        Assert.Equal(draft.Nodes[0].Key, edge.FromNodeKey);
        Assert.Equal(draft.Nodes[1].Key, edge.ToNodeKey);
        Assert.Empty(draft.ValidationIssues);
    }

    [Fact]
    public async Task Saving_a_process_that_never_ran_edits_it_in_place()
    {
        HttpClient designer = await factory.SignInAsync(TenantAdmin);

        WorkflowDefinitionDetailDto draft = await CreateAsync(designer, "Processo editado no sítio");
        WorkflowDefinitionDetailDto saved = await SaveAsync(designer, draft, ApprovalGraph());

        Assert.Equal(draft.Summary.Id, saved.Summary.Id);
        Assert.Equal(1, saved.Summary.Version);
        Assert.Equal(4, saved.Nodes.Count);
        Assert.Equal(3, saved.Edges.Count);
        Assert.Empty(saved.ValidationIssues);

        // The stored configuration survives the round trip, including the branch on each edge.
        Assert.Contains(saved.Edges, e => e.Branch == "approved" && e.ToNodeKey == "aprovado");
        Assert.Contains(saved.Edges, e => e.Branch == "rejected" && e.ToNodeKey == "rejeitado");
    }

    [Fact]
    public async Task Saving_a_process_that_has_run_supersedes_it_so_running_instances_keep_their_graph()
    {
        HttpClient designer = await factory.SignInAsync(TenantAdmin);
        HttpClient buyer = await factory.SignInAsync(Procurement);

        WorkflowDefinitionDetailDto draft = await CreateAsync(designer, "Processo com histórico");
        WorkflowDefinitionDetailDto version1 = await SaveAsync(designer, draft, ApprovalGraph());
        await ActivateAsync(designer, version1.Summary.Id);

        DocumentDetailDto document = await CreateDocumentAsync(buyer, "Documento do processo com histórico");
        Guid instanceId = await StartAsync(designer, version1.Summary.Id, document.Summary.Id);

        WorkflowDefinitionDetailDto reloaded = await GetAsync(designer, version1.Summary.Id);
        Assert.False(reloaded.Summary.IsEditableInPlace);

        List<SaveWorkflowNodeRequest> nodes = [.. ApprovalGraph().Nodes];
        nodes[1] = nodes[1] with { Name = "Aprovação revista" };

        WorkflowDefinitionDetailDto version2 = await SaveAsync(
            designer, reloaded, new Graph(nodes, ApprovalGraph().Edges));

        Assert.NotEqual(version1.Summary.Id, version2.Summary.Id);
        Assert.Equal(2, version2.Summary.Version);
        Assert.Equal(version1.Summary.Code, version2.Summary.Code);
        Assert.True(version2.Summary.IsLatestVersion);

        // The old row stays exactly as it was, because the running instance still resolves against it.
        WorkflowDefinitionDetailDto original = await GetAsync(designer, version1.Summary.Id);
        Assert.False(original.Summary.IsLatestVersion);
        Assert.Equal("Aprovação do gestor", original.Nodes.Single(node => node.Key == "aprovacao").Name);

        WorkflowInstanceDetailDto instance = await GetInstanceAsync(designer, instanceId);
        Assert.Equal(version1.Summary.Id, instance.Summary.DefinitionId);
        Assert.Equal("Aprovação do gestor", instance.Nodes.Single(node => node.Key == "aprovacao").Name);
    }

    [Fact]
    public async Task A_save_built_on_a_stale_read_is_refused_instead_of_overwriting()
    {
        HttpClient designer = await factory.SignInAsync(TenantAdmin);

        WorkflowDefinitionDetailDto draft = await CreateAsync(designer, "Processo em conflito");
        await SaveAsync(designer, draft, ApprovalGraph());

        // Same token as the first save: whoever holds it is looking at a version that no longer exists.
        HttpResponseMessage response = await designer.PutAsJsonAsync(
            $"/api/workflows/{draft.Summary.Id}", BuildSaveRequest(draft, ApprovalGraph()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_edge_pointing_at_a_node_that_does_not_exist_is_refused()
    {
        HttpClient designer = await factory.SignInAsync(TenantAdmin);

        WorkflowDefinitionDetailDto draft = await CreateAsync(designer, "Processo com ligação solta");

        Graph broken = ApprovalGraph() with
        {
            Edges = [.. ApprovalGraph().Edges, new SaveWorkflowEdgeRequest { FromNodeKey = "aprovacao", ToNodeKey = "inexistente" }]
        };

        HttpResponseMessage response = await designer.PutAsJsonAsync(
            $"/api/workflows/{draft.Summary.Id}", BuildSaveRequest(draft, broken));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        ProblemDocument? problem = await response.Content.ReadFromJsonAsync<ProblemDocument>(Json);
        Assert.Equal("edge_references_unknown_node", problem?.Code);
    }

    [Fact]
    public async Task A_process_with_an_unconfigured_step_saves_but_cannot_be_activated()
    {
        HttpClient designer = await factory.SignInAsync(TenantAdmin);

        WorkflowDefinitionDetailDto draft = await CreateAsync(designer, "Processo por configurar");

        Graph unconfigured = ApprovalGraph() with
        {
            Nodes = [.. ApprovalGraph().Nodes.Select(node => node.Key == "aprovacao" ? node with { ConfigJson = null } : node)]
        };

        // Saving a half-drawn process is a normal state; activating one is not.
        WorkflowDefinitionDetailDto saved = await SaveAsync(designer, draft, unconfigured);
        Assert.Contains(saved.ValidationIssues, issue => issue.Code == "assignment_missing");

        HttpResponseMessage response = await designer.PostAsync($"/api/workflows/{saved.Summary.Id}/activate", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        ProblemDocument? problem = await response.Content.ReadFromJsonAsync<ProblemDocument>(Json);
        Assert.Equal("workflow_validation_failed", problem?.Code);
        Assert.False((await GetAsync(designer, saved.Summary.Id)).Summary.IsActive);
    }

    [Fact]
    public async Task A_duplicate_is_always_inactive_and_carries_the_graph_but_not_the_history()
    {
        HttpClient designer = await factory.SignInAsync(TenantAdmin);

        WorkflowDefinitionDetailDto draft = await CreateAsync(designer, "Processo a duplicar");
        WorkflowDefinitionDetailDto source = await SaveAsync(designer, draft, ApprovalGraph());
        await ActivateAsync(designer, source.Summary.Id);

        HttpResponseMessage response = await designer.PostAsJsonAsync(
            $"/api/workflows/{source.Summary.Id}/duplicate", new DuplicateWorkflowRequest("Cópia do processo"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        WorkflowDefinitionDetailDto copy = (await response.Content.ReadFromJsonAsync<WorkflowDefinitionDetailDto>(Json))!;

        Assert.NotEqual(source.Summary.Code, copy.Summary.Code);
        Assert.False(copy.Summary.IsActive);
        Assert.Equal(1, copy.Summary.Version);
        Assert.Equal(source.Nodes.Count, copy.Nodes.Count);
        Assert.Equal(0, copy.Summary.RunningInstances);
    }

    [Fact]
    public async Task A_process_with_running_instances_cannot_be_deleted()
    {
        HttpClient designer = await factory.SignInAsync(TenantAdmin);
        HttpClient buyer = await factory.SignInAsync(Procurement);

        WorkflowDefinitionDetailDto draft = await CreateAsync(designer, "Processo em curso");
        WorkflowDefinitionDetailDto definition = await SaveAsync(designer, draft, ApprovalGraph());
        await ActivateAsync(designer, definition.Summary.Id);

        DocumentDetailDto document = await CreateDocumentAsync(buyer, "Documento do processo em curso");
        await StartAsync(designer, definition.Summary.Id, document.Summary.Id);

        HttpResponseMessage refused = await designer.DeleteAsync($"/api/workflows/{definition.Summary.Id}");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        ProblemDocument? problem = await refused.Content.ReadFromJsonAsync<ProblemDocument>(Json);
        Assert.Equal("workflow_has_running_instances", problem?.Code);
    }

    [Fact]
    public async Task Designing_is_refused_to_a_user_without_the_permission()
    {
        HttpClient employee = await factory.SignInAsync(Procurement);

        HttpResponseMessage response = await employee.PostAsJsonAsync(
            "/api/workflows", new CreateWorkflowRequest { Name = "Processo não autorizado" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    private sealed record ProblemDocument(string? Code, string? Detail);

    private sealed record Graph(
        IReadOnlyList<SaveWorkflowNodeRequest> Nodes, IReadOnlyList<SaveWorkflowEdgeRequest> Edges);

    /// <summary>A start, one approval and the two ends its branches lead to: the smallest real process.</summary>
    private static Graph ApprovalGraph() => new(
        [
            new SaveWorkflowNodeRequest { Key = "inicio", Name = "Início", Type = WorkflowNodeType.Start, PositionX = 0, PositionY = 0 },
            new SaveWorkflowNodeRequest
            {
                Key = "aprovacao",
                Name = "Aprovação do gestor",
                Type = WorkflowNodeType.Approval,
                ConfigJson = """{"taskTitle":"Aprovar","assignment":{"strategy":"Role","role":"Manager"},"dueInHours":24}""",
                PositionX = 320,
                PositionY = 0
            },
            new SaveWorkflowNodeRequest
            {
                Key = "aprovado",
                Name = "Aprovado",
                Type = WorkflowNodeType.End,
                ConfigJson = """{"outcome":"Aprovado"}""",
                PositionX = 640,
                PositionY = 0
            },
            new SaveWorkflowNodeRequest
            {
                Key = "rejeitado",
                Name = "Rejeitado",
                Type = WorkflowNodeType.End,
                ConfigJson = """{"outcome":"Rejeitado","endsAsRejected":true}""",
                PositionX = 640,
                PositionY = 160
            }
        ],
        [
            new SaveWorkflowEdgeRequest { FromNodeKey = "inicio", ToNodeKey = "aprovacao" },
            new SaveWorkflowEdgeRequest { FromNodeKey = "aprovacao", ToNodeKey = "aprovado", Branch = "approved" },
            new SaveWorkflowEdgeRequest { FromNodeKey = "aprovacao", ToNodeKey = "rejeitado", Branch = "rejected" }
        ]);

    private static SaveWorkflowRequest BuildSaveRequest(WorkflowDefinitionDetailDto definition, Graph graph) => new()
    {
        Name = definition.Summary.Name,
        Description = definition.Summary.Description,
        DocumentTypeId = definition.Summary.DocumentTypeId,
        Trigger = definition.Summary.Trigger,
        Nodes = graph.Nodes,
        Edges = graph.Edges,
        ConcurrencyToken = definition.ConcurrencyToken
    };

    private static async Task<WorkflowDefinitionDetailDto> CreateAsync(HttpClient client, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/workflows", new CreateWorkflowRequest { Name = $"{name} {Guid.NewGuid():N}"[..40] });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<WorkflowDefinitionDetailDto>(Json))!;
    }

    private static async Task<WorkflowDefinitionDetailDto> SaveAsync(
        HttpClient client, WorkflowDefinitionDetailDto definition, Graph graph)
    {
        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/workflows/{definition.Summary.Id}", BuildSaveRequest(definition, graph));

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Save failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<WorkflowDefinitionDetailDto>(Json))!;
    }

    private static async Task ActivateAsync(HttpClient client, Guid definitionId)
    {
        HttpResponseMessage response = await client.PostAsync($"/api/workflows/{definitionId}/activate", null);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Activate failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
    }

    private static async Task<WorkflowDefinitionDetailDto> GetAsync(HttpClient client, Guid definitionId) =>
        (await client.GetFromJsonAsync<WorkflowDefinitionDetailDto>($"/api/workflows/{definitionId}", Json))!;

    private static async Task<Guid> StartAsync(HttpClient client, Guid definitionId, Guid documentId)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/workflows/instances", new StartWorkflowRequest { DefinitionId = definitionId, DocumentId = documentId });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<WorkflowInstanceDetailDto>(Json))!.Summary.Id;
    }

    private static async Task<WorkflowInstanceDetailDto> GetInstanceAsync(HttpClient client, Guid instanceId) =>
        (await client.GetFromJsonAsync<WorkflowInstanceDetailDto>($"/api/workflows/instances/{instanceId}", Json))!;

    private static async Task<DocumentDetailDto> CreateDocumentAsync(HttpClient client, string title)
    {
        IReadOnlyList<DocumentTypeDto>? types =
            await client.GetFromJsonAsync<IReadOnlyList<DocumentTypeDto>>("/api/document-catalogue/types", Json);

        Assert.NotNull(types);
        Guid typeId = types.Single(type => type.Code == "SupplierInvoice").Id;

        using MultipartFormDataContent form = [];
        ByteArrayContent file = new(Encoding.UTF8.GetBytes($"FATURA\n{title}"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(file, "File", "fatura.txt");
        form.Add(new StringContent(title), "Title");
        form.Add(new StringContent(typeId.ToString()), "DocumentTypeId");
        form.Add(new StringContent("supplier=Fornecedor de Teste"), "Metadata");
        form.Add(new StringContent("invoiceNumber=DESIGN-0001"), "Metadata");
        form.Add(new StringContent("invoiceDate=2026-09-01"), "Metadata");
        form.Add(new StringContent("totalAmount=1250.00"), "Metadata");

        HttpResponseMessage response = await client.PostAsync("/api/documents", form);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<DocumentDetailDto>(Json))!;
    }
}
