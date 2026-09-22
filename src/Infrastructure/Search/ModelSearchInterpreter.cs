using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Application.Search;
using DocOps.Domain.Documents;
using DocOps.Domain.Search;
using DocOps.Infrastructure.Intelligence;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocOps.Infrastructure.Search;

/// <summary>
/// Reads a question with the configured language model, and is trusted no further than the demo
/// reader is.
///
/// The model is given the question and the names in the tenant's own catalogue — no document text,
/// no identifiers, no data. What comes back is a set of names and enumeration values, and every one
/// of them is looked up again here: a type the catalogue does not have is dropped, a status that
/// does not parse is dropped, a date that does not parse is dropped. The model therefore cannot
/// widen a search beyond what the form on screen could ask for, and identifiers it never saw cannot
/// be smuggled into a filter.
///
/// When the model is unreachable, slow, or answers with nothing usable, the rule reader answers
/// instead and the reply says the answer came from the rules. A search that quietly returns nothing
/// because a remote service was down would look exactly like a search that found nothing.
/// </summary>
public sealed class ModelSearchInterpreter(
    HttpClient httpClient,
    DocOpsDbContext dbContext,
    DemoSearchInterpreter fallback,
    IOptions<AiOptions> options,
    ILogger<ModelSearchInterpreter> logger) : IAiSearchService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AiOptions _options = options.Value;

    public async Task<InterpretedSearch> InterpretAsync(
        string question, PageRequest paging, CancellationToken cancellationToken = default)
    {
        Catalogue catalogue = await LoadCatalogueAsync(cancellationToken);
        Reply? reply;

        try
        {
            reply = await AskAsync(question, catalogue, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The message may carry the question; only the fact is logged.
            logger.LogWarning(exception, "O modelo não respondeu à interpretação da pergunta; a usar as regras.");
            reply = null;
        }

        if (reply is null)
        {
            return await fallback.InterpretAsync(question, paging, cancellationToken);
        }

        List<InterpretedFilterDto> filters = [];

        List<Guid> typeIds = Resolve(reply.Types, catalogue.Types, filters, "type");
        List<Guid> departmentIds = Resolve(reply.Departments, catalogue.Departments, filters, "department");
        List<Guid> tagIds = Resolve(reply.Tags, catalogue.Tags, filters, "tag");

        List<DocumentStatus> statuses = ParseAll<DocumentStatus>(reply.Statuses, filters, "status");
        List<DocumentApprovalState> approvals =
            ParseAll<DocumentApprovalState>(reply.ApprovalStates, filters, "approvalState");
        List<DocumentWorkflowState> workflows =
            ParseAll<DocumentWorkflowState>(reply.WorkflowStates, filters, "workflowState");
        List<SecurityClassification> classifications =
            ParseAll<SecurityClassification>(reply.Classifications, filters, "classification");

        DateTimeOffset? from = ParseDate(reply.CreatedFrom, filters, "createdFrom");
        DateTimeOffset? to = ParseDate(reply.CreatedTo, filters, "createdTo");

        if (reply.OnlyMine == true)
        {
            filters.Add(new InterpretedFilterDto("onlyMine", "true"));
        }

        string? text = string.IsNullOrWhiteSpace(reply.Text) ? null : reply.Text.Trim();

        if (text is not null)
        {
            filters.Add(new InterpretedFilterDto("text", text));
        }

        // Everything the model said was discarded, so it understood nothing this application can act
        // on. The rules get the question instead of the person getting an empty answer.
        if (filters.Count == 0)
        {
            return await fallback.InterpretAsync(question, paging, cancellationToken);
        }

        DocumentSearchRequest request = new()
        {
            Query = text,
            TypeIds = typeIds,
            DepartmentIds = departmentIds,
            TagIds = tagIds,
            Statuses = statuses,
            Classifications = classifications,
            ApprovalStates = approvals,
            WorkflowStates = workflows,
            OnlyMine = reply.OnlyMine == true,
            CreatedFromUtc = from,
            CreatedToUtc = to,
            SortBy = text is null ? DocumentSortField.CreatedAt : DocumentSortField.Relevance,
            SortDescending = true,
            Paging = paging
        };

        return new InterpretedSearch(
            request,
            new SearchInterpretationDto
            {
                Question = question.Trim(),
                Filters = filters,
                Text = text,
                Understood = filters.Any(filter => filter.Field != "text"),
                Source = AiOrigin.Model
            });
    }

    // -----------------------------------------------------------------------------------------

    private async Task<Catalogue> LoadCatalogueAsync(CancellationToken cancellationToken)
    {
        List<NamedEntry> types = await dbContext.DocumentTypes
            .Where(t => t.IsActive)
            .Select(t => new NamedEntry(t.Id, t.Name))
            .ToListAsync(cancellationToken);

        List<NamedEntry> departments = await dbContext.Departments
            .Select(d => new NamedEntry(d.Id, d.Name))
            .ToListAsync(cancellationToken);

        List<NamedEntry> tags = await dbContext.Tags
            .Select(t => new NamedEntry(t.Id, t.Name))
            .ToListAsync(cancellationToken);

        return new Catalogue(types, departments, tags);
    }

    private async Task<Reply?> AskAsync(string question, Catalogue catalogue, CancellationToken cancellationToken)
    {
        string path = _options.IsAzure
            ? $"{_options.Endpoint!.TrimEnd('/')}/chat/completions?api-version={Uri.EscapeDataString(_options.ApiVersion)}"
            : $"{_options.Endpoint!.TrimEnd('/')}/chat/completions";

        using HttpRequestMessage message = new(HttpMethod.Post, path);

        if (_options.IsAzure)
        {
            message.Headers.Add("api-key", _options.ApiKey);
        }
        else
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        message.Content = JsonContent.Create(
            new ChatRequest(
                _options.Model!,
                [new ChatMessage("system", SystemPrompt(catalogue)), new ChatMessage("user", question)],
                new ResponseFormat("json_object"),
                Temperature: 0),
            options: Json);

        using HttpResponseMessage response = await httpClient.SendAsync(message, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("AI model request failed with HTTP {Status}", (int)response.StatusCode);
            return null;
        }

        ChatResponse? chat = await response.Content.ReadFromJsonAsync<ChatResponse>(Json, cancellationToken);
        string? payload = chat?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Reply>(payload, Json);
        }
        catch (JsonException)
        {
            logger.LogWarning("AI model returned content that is not the requested JSON");
            return null;
        }
    }

    private static string SystemPrompt(Catalogue catalogue) =>
        "You turn a question about a document repository into filters. Answer only with JSON of the form " +
        "{\"types\":[string],\"departments\":[string],\"tags\":[string],\"statuses\":[string]," +
        "\"approvalStates\":[string],\"workflowStates\":[string],\"classifications\":[string]," +
        "\"onlyMine\":boolean,\"createdFrom\":\"yyyy-MM-dd\",\"createdTo\":\"yyyy-MM-dd\",\"text\":string}. " +
        "Use only names that appear in the lists below, copied exactly; omit anything you are unsure of. " +
        "Put words that are not a filter into \"text\". The question may be in Portuguese or English.\n" +
        $"Document types: {string.Join("; ", catalogue.Types.Select(entry => entry.Name))}\n" +
        $"Departments: {string.Join("; ", catalogue.Departments.Select(entry => entry.Name))}\n" +
        $"Tags: {string.Join("; ", catalogue.Tags.Select(entry => entry.Name))}\n" +
        $"statuses: {string.Join(", ", Enum.GetNames<DocumentStatus>())}\n" +
        $"approvalStates: {string.Join(", ", Enum.GetNames<DocumentApprovalState>())}\n" +
        $"workflowStates: {string.Join(", ", Enum.GetNames<DocumentWorkflowState>())}\n" +
        $"classifications: {string.Join(", ", Enum.GetNames<SecurityClassification>())}";

    /// <summary>
    /// Turns the names the model returned into identifiers, by looking each one up in the catalogue
    /// that was sent to it. A name that is not there yields nothing — the model does not get to name
    /// something into existence.
    /// </summary>
    private static List<Guid> Resolve(
        IReadOnlyList<string>? names,
        IReadOnlyList<NamedEntry> catalogue,
        List<InterpretedFilterDto> filters,
        string field)
    {
        List<Guid> resolved = [];

        foreach (string name in names ?? [])
        {
            string folded = SearchText.Fold(name);

            NamedEntry? entry = catalogue.FirstOrDefault(candidate =>
                string.Equals(SearchText.Fold(candidate.Name), folded, StringComparison.Ordinal));

            if (entry is null || resolved.Contains(entry.Id))
            {
                continue;
            }

            resolved.Add(entry.Id);
            filters.Add(new InterpretedFilterDto(field, entry.Name));
        }

        return resolved;
    }

    private static List<T> ParseAll<T>(
        IReadOnlyList<string>? values, List<InterpretedFilterDto> filters, string field)
        where T : struct, Enum
    {
        List<T> parsed = [];

        foreach (string value in values ?? [])
        {
            if (!Enum.TryParse(value, ignoreCase: true, out T item)
                || !Enum.IsDefined(item)
                || parsed.Contains(item))
            {
                continue;
            }

            parsed.Add(item);
            filters.Add(new InterpretedFilterDto(field, item.ToString()));
        }

        return parsed;
    }

    private static DateTimeOffset? ParseDate(string? value, List<InterpretedFilterDto> filters, string field)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day))
        {
            return null;
        }

        filters.Add(new InterpretedFilterDto(field, value!));

        return new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    // Wire formats -------------------------------------------------------------------------------

    private sealed record NamedEntry(Guid Id, string Name);

    private sealed record Catalogue(
        IReadOnlyList<NamedEntry> Types,
        IReadOnlyList<NamedEntry> Departments,
        IReadOnlyList<NamedEntry> Tags);

    private sealed record ChatRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("response_format")] ResponseFormat ResponseFormat,
        decimal Temperature);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ResponseFormat(string Type);

    private sealed record ChatResponse(IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice(ChatMessage? Message);

    private sealed record Reply(
        IReadOnlyList<string>? Types,
        IReadOnlyList<string>? Departments,
        IReadOnlyList<string>? Tags,
        IReadOnlyList<string>? Statuses,
        IReadOnlyList<string>? ApprovalStates,
        IReadOnlyList<string>? WorkflowStates,
        IReadOnlyList<string>? Classifications,
        bool? OnlyMine,
        string? CreatedFrom,
        string? CreatedTo,
        string? Text);
}
