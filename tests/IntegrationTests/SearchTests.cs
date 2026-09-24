using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocOps.Application.Search;
using DocOps.Domain.Search;

namespace DocOps.IntegrationTests;

/// <summary>
/// The search against a real SQL Server.
///
/// The container these tests start is the stock image, which has no full-text component, so what
/// runs here is the portable path. That is deliberate rather than a limitation: it proves the
/// application migrates and answers on a server without the component, and the diagnostics in every
/// response say which path answered — a search that had silently degraded would pass a test that
/// only counted rows.
///
/// What does not change between the two paths is the part that matters most: which documents a
/// caller is allowed to see, and therefore which ones can be counted, ranked or paged over.
/// </summary>
[Collection(ApiCollection.Name)]
public class SearchTests(DocOpsApiFactory factory)
{
    private const string TenantAdmin = "admin@asteria.example";
    private const string Employee = "ana.ferreira@asteria.example";
    private const string ExternalPartner = "contacto@northstar.example";
    private const string VerticeAdmin = "admin@vertice.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<SearchResultDto> SearchAsync(HttpClient client, string query)
    {
        SearchResultDto? result = await client.GetFromJsonAsync<SearchResultDto>($"/api/search?{query}", Json);

        Assert.NotNull(result);

        return result;
    }

    // -----------------------------------------------------------------------------------------
    // Authorization
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_second_organisation_finds_nothing_of_the_first()
    {
        HttpClient vertice = await factory.SignInAsync(VerticeAdmin);

        SearchResultDto result = await SearchAsync(vertice, "query=fatura&pageSize=100");

        // Identidade, não contagem: a segunda organização pode legitimamente vir a ter faturas
        // próprias, criadas por outra suite. O que nunca pode acontecer é encontrar as da primeira.
        HttpClient asteria = await factory.SignInAsync(TenantAdmin);
        SearchResultDto theirs = await SearchAsync(asteria, "query=fatura&pageSize=100");

        Assert.NotEmpty(theirs.Page.Items);
        Assert.DoesNotContain(
            result.Page.Items,
            item => theirs.Page.Items.Any(other => other.Document.Id == item.Document.Id));
    }

    /// <summary>
    /// The total is part of the answer, not a hint about what exists. Two callers searching the same
    /// words must get totals that match what each of them may see.
    /// </summary>
    [Fact]
    public async Task The_total_never_counts_a_document_the_caller_may_not_see()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);
        HttpClient employee = await factory.SignInAsync(Employee);

        SearchResultDto forAdmin = await SearchAsync(admin, "query=contrato&pageSize=100");
        SearchResultDto forEmployee = await SearchAsync(employee, "query=contrato&pageSize=100");

        Assert.True(forAdmin.Page.TotalCount >= forEmployee.Page.TotalCount);
        Assert.Equal(forEmployee.Page.TotalCount, forEmployee.Page.Items.Count);

        // And the ones the employee did get are a subset of the ones the administrator got.
        Assert.Subset(
            forAdmin.Page.Items.Select(hit => hit.Document.Id).ToHashSet(),
            forEmployee.Page.Items.Select(hit => hit.Document.Id).ToHashSet());
    }

    /// <summary>
    /// An external partner reaches their own documents through the portal, not through the internal
    /// search. Both ways in are refused — the question endpoint as firmly as the query one, since a
    /// reader that answered would be a way around the permission.
    /// </summary>
    [Fact]
    public async Task Refuses_a_caller_without_the_search_permission()
    {
        HttpClient partner = await factory.SignInAsync(ExternalPartner);

        HttpResponseMessage search = await partner.GetAsync("/api/search?query=contrato");
        HttpResponseMessage question = await partner.PostAsJsonAsync(
            "/api/search/natural-language", new { question = "faturas", paging = new { page = 1, pageSize = 5 } });

        Assert.Equal(HttpStatusCode.Forbidden, search.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, question.StatusCode);
    }

    // -----------------------------------------------------------------------------------------
    // Matching
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Finds_a_document_by_words_in_its_title()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        SearchResultDto result = await SearchAsync(admin, "query=Northstar&pageSize=100");

        Assert.NotEmpty(result.Page.Items);
        Assert.All(result.Page.Items, hit => Assert.NotEmpty(hit.MatchedIn));
    }

    [Fact]
    public async Task Says_which_engine_answered()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        SearchResultDto result = await SearchAsync(admin, "query=contrato&pageSize=5");

        // Whichever path ran, the response must not claim to have used an index it did not use.
        Assert.False(result.Diagnostics.FullTextIndexUsed && !result.Diagnostics.FullTextAvailable);
        Assert.Equal(["contrato"], result.Diagnostics.Terms);
    }

    [Fact]
    public async Task Returns_an_empty_page_rather_than_an_error_when_nothing_matches()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        SearchResultDto result = await SearchAsync(admin, "query=zzzznaoexiste&pageSize=10");

        Assert.Equal(0, result.Page.TotalCount);
        Assert.Empty(result.Page.Items);
    }

    /// <summary>
    /// The operators of the full-text grammar are not a way in, and neither is a stray quote. The
    /// request must be answered, not refused with a server error.
    /// </summary>
    [Theory]
    [InlineData("fatura\" OR 1=1")]
    [InlineData("'; DROP TABLE Documents; --")]
    [InlineData("NEAR((a,b),1)")]
    [InlineData("%_%")]
    public async Task Survives_text_that_tries_to_be_syntax(string hostile)
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        HttpResponseMessage response = await admin.GetAsync(
            $"/api/search?query={Uri.EscapeDataString(hostile)}&pageSize=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // -----------------------------------------------------------------------------------------
    // Filters
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Filters_by_the_approval_a_document_is_waiting_on()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        SearchResultDto awaiting = await SearchAsync(
            admin, $"approvalStates={DocumentApprovalState.Awaiting}&sortBy=CreatedAt&pageSize=100");
        SearchResultDto never = await SearchAsync(
            admin, $"approvalStates={DocumentApprovalState.NotRequested}&sortBy=CreatedAt&pageSize=100");

        Assert.NotEmpty(awaiting.Page.Items);

        // A document cannot both be waiting on an approver and never have been sent for approval.
        Assert.Empty(
            awaiting.Page.Items.Select(hit => hit.Document.Id)
                .Intersect(never.Page.Items.Select(hit => hit.Document.Id)));
    }

    [Fact]
    public async Task Filters_by_where_a_document_stands_in_its_process()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        SearchResultDto running = await SearchAsync(
            admin, $"workflowStates={DocumentWorkflowState.InProgress}&sortBy=CreatedAt&pageSize=100");
        SearchResultDto none = await SearchAsync(
            admin, $"workflowStates={DocumentWorkflowState.None}&sortBy=CreatedAt&pageSize=100");

        Assert.NotEmpty(running.Page.Items);
        Assert.NotEmpty(none.Page.Items);
        Assert.Empty(
            running.Page.Items.Select(hit => hit.Document.Id)
                .Intersect(none.Page.Items.Select(hit => hit.Document.Id)));
    }

    [Fact]
    public async Task Asking_for_several_states_returns_the_union_and_not_the_intersection()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        SearchResultDto running = await SearchAsync(
            admin, $"workflowStates={DocumentWorkflowState.InProgress}&sortBy=CreatedAt&pageSize=100");
        SearchResultDto none = await SearchAsync(
            admin, $"workflowStates={DocumentWorkflowState.None}&sortBy=CreatedAt&pageSize=100");
        SearchResultDto both = await SearchAsync(
            admin,
            $"workflowStates={DocumentWorkflowState.InProgress}&workflowStates={DocumentWorkflowState.None}"
            + "&sortBy=CreatedAt&pageSize=100");

        Assert.Equal(running.Page.TotalCount + none.Page.TotalCount, both.Page.TotalCount);
    }

    [Fact]
    public async Task Pages_without_repeating_or_losing_a_document()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        SearchResultDto first = await SearchAsync(admin, "sortBy=CreatedAt&includeArchived=false&page=1&pageSize=5");
        SearchResultDto second = await SearchAsync(admin, "sortBy=CreatedAt&includeArchived=false&page=2&pageSize=5");

        Assert.Equal(5, first.Page.Items.Count);
        Assert.NotEmpty(second.Page.Items);
        Assert.Empty(
            first.Page.Items.Select(hit => hit.Document.Id)
                .Intersect(second.Page.Items.Select(hit => hit.Document.Id)));
    }

    // -----------------------------------------------------------------------------------------
    // Natural language
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Reads_a_question_into_filters_and_runs_those_filters()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        NaturalLanguageSearchResultDto? answer = await AskAsync(admin, "faturas que aguardam aprovação");

        Assert.NotNull(answer);
        Assert.True(answer.Interpretation.Understood);
        Assert.Contains(
            answer.Interpretation.Filters,
            filter => filter.Field == "approvalState" && filter.Value == nameof(DocumentApprovalState.Awaiting));
        Assert.Contains(answer.Interpretation.Filters, filter => filter.Field == "type");

        // Nothing was left over to search as text: the whole question became filters.
        Assert.Null(answer.Interpretation.Text);
    }

    [Fact]
    public async Task Says_when_it_understood_nothing_rather_than_inventing_a_reading()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        NaturalLanguageSearchResultDto? answer = await AskAsync(admin, "qualquer coisa que ninguém ensinou");

        Assert.NotNull(answer);
        Assert.False(answer.Interpretation.Understood);
        Assert.All(answer.Interpretation.Filters, filter => Assert.Equal("text", filter.Field));
    }

    /// <summary>
    /// The reading is deterministic and must be declared as the demonstration reader's work. A demo
    /// answer presented as a model's would be the one thing this project must never do (ADR-004).
    /// </summary>
    [Fact]
    public async Task Answers_the_same_question_the_same_way_and_declares_the_demo_reader()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        NaturalLanguageSearchResultDto? first = await AskAsync(admin, "contratos aprovados");
        NaturalLanguageSearchResultDto? second = await AskAsync(admin, "contratos aprovados");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(AiOrigin.Demo, first.Interpretation.Source);
        Assert.Equal(
            first.Interpretation.Filters.Select(filter => $"{filter.Field}={filter.Value}"),
            second.Interpretation.Filters.Select(filter => $"{filter.Field}={filter.Value}"));
    }

    /// <summary>
    /// A question runs through the same service, and therefore the same authorization, as the form.
    /// Asking in words must not reach a single document that the filters would not.
    /// </summary>
    [Fact]
    public async Task A_question_reaches_no_further_than_the_form_would()
    {
        HttpClient employee = await factory.SignInAsync(Employee);
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        NaturalLanguageSearchResultDto? forEmployee = await AskAsync(employee, "contratos aprovados");
        NaturalLanguageSearchResultDto? forAdmin = await AskAsync(admin, "contratos aprovados");

        Assert.NotNull(forEmployee);
        Assert.NotNull(forAdmin);
        Assert.Subset(
            forAdmin.Result.Page.Items.Select(hit => hit.Document.Id).ToHashSet(),
            forEmployee.Result.Page.Items.Select(hit => hit.Document.Id).ToHashSet());
    }

    [Fact]
    public async Task Rejects_an_empty_question()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            "/api/search/natural-language", new { question = "  ", paging = new { page = 1, pageSize = 5 } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<NaturalLanguageSearchResultDto?> AskAsync(HttpClient client, string question)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/search/natural-language",
            new { question, paging = new { page = 1, pageSize = 100 } });

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<NaturalLanguageSearchResultDto>(Json);
    }
}
