using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Application.Search;
using DocOps.Domain.Documents;
using DocOps.Domain.Identity;
using DocOps.Domain.Search;
using DocOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOps.Api.Controllers;

[ApiController]
[Route("api/search")]
[Authorize]
[Produces("application/json")]
public sealed class SearchController(
    ISearchService searchService,
    IAiSearchService interpreter) : ControllerBase
{
    /// <summary>
    /// Searches the repository. The result is a page: the filtering, the ranking and the count all
    /// happen in the database, and all of them happen inside the set of documents the caller may
    /// see — so neither the results nor the total can reveal a document they may not.
    /// </summary>
    [HttpGet]
    [HasPermission(Permissions.SearchUse)]
    [ProducesResponseType<SearchResultDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SearchResultDto>> Search(
        [FromQuery] SearchQuery search, CancellationToken cancellationToken) =>
        Ok(await searchService.SearchAsync(search.ToRequest(), cancellationToken));

    /// <summary>
    /// Answers a question written in plain language.
    ///
    /// The question is read into the same filters the search form produces and those filters are
    /// what runs, through the same service and therefore the same authorization. The reading is
    /// returned with the results so the person can see what their question was taken to mean — and
    /// so the interface can say which engine read it, which for the demonstration reader is a table
    /// of rules and never a language model.
    /// </summary>
    [HttpPost("natural-language")]
    [HasPermission(Permissions.SearchUse)]
    [ProducesResponseType<NaturalLanguageSearchResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NaturalLanguageSearchResultDto>> Ask(
        NaturalLanguageSearchRequest request, CancellationToken cancellationToken)
    {
        InterpretedSearch interpreted = await interpreter.InterpretAsync(
            request.Question, request.Paging, cancellationToken);

        SearchResultDto result = await searchService.SearchAsync(interpreted.Request, cancellationToken);

        return Ok(new NaturalLanguageSearchResultDto
        {
            Interpretation = interpreted.Summary,
            Result = result
        });
    }
}

/// <summary>
/// The search as it arrives on the query string. It carries the same filters as the document list —
/// the search screen is a richer way of asking the same question — and differs only in what it
/// orders by when nothing is said: relevance, because someone on the search screen is looking for
/// something rather than browsing.
/// </summary>
public sealed record SearchQuery
{
    public string? Query { get; init; }
    public Guid[]? TypeIds { get; init; }
    public DocumentStatus[]? Statuses { get; init; }
    public Guid[]? DepartmentIds { get; init; }
    public Guid[]? TagIds { get; init; }
    public SecurityClassification[]? Classifications { get; init; }
    public DocumentWorkflowState[]? WorkflowStates { get; init; }
    public DocumentApprovalState[]? ApprovalStates { get; init; }
    public Guid? OwnerUserId { get; init; }
    public DateTimeOffset? CreatedFromUtc { get; init; }
    public DateTimeOffset? CreatedToUtc { get; init; }
    public bool IncludeArchived { get; init; } = true;
    public bool OnlyMine { get; init; }
    public DocumentSortField SortBy { get; init; } = DocumentSortField.Relevance;
    public bool SortDescending { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = PageRequest.DefaultPageSize;

    public DocumentSearchRequest ToRequest() => new()
    {
        Query = Query,
        TypeIds = TypeIds ?? [],
        Statuses = Statuses ?? [],
        DepartmentIds = DepartmentIds ?? [],
        TagIds = TagIds ?? [],
        Classifications = Classifications ?? [],
        WorkflowStates = WorkflowStates ?? [],
        ApprovalStates = ApprovalStates ?? [],
        OwnerUserId = OwnerUserId,
        CreatedFromUtc = CreatedFromUtc,
        CreatedToUtc = CreatedToUtc,
        IncludeArchived = IncludeArchived,
        OnlyMine = OnlyMine,
        SortBy = SortBy,
        SortDescending = SortDescending,
        Paging = new PageRequest { Page = Page, PageSize = PageSize }
    };
}
