using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Domain.Search;

namespace DocOps.Application.Search;

// ---------------------------------------------------------------------------------------------
// Read models
// ---------------------------------------------------------------------------------------------

/// <summary>
/// A stretch of a document's own text around a term that was searched for, and where the term sits
/// inside it, so the interface can mark it without having to search the excerpt again — and without
/// being able to mark something the document does not say.
/// </summary>
public sealed record SearchSnippetDto(string Text, int Start, int Length);

/// <summary>One document that answered the search, with why it answered it.</summary>
public sealed record SearchHitDto
{
    public required DocumentListItemDto Document { get; init; }

    /// <summary>
    /// How well the document answered, from 0 to 1. Zero when the search carried no text — then
    /// there is nothing to be more or less relevant to, and the order is the one that was asked for.
    /// </summary>
    public required double Score { get; init; }

    /// <summary>Which parts of the document the terms were found in.</summary>
    public required IReadOnlyList<SearchMatchField> MatchedIn { get; init; }

    /// <summary>The passage the terms were found in, when they were found in the text itself.</summary>
    public SearchSnippetDto? Snippet { get; init; }
}

/// <summary>
/// What the search engine actually did. It is shown in the interface rather than kept in a log: a
/// ranked search and a substring scan return different things, and hiding which one ran would make
/// the results look better than they are.
/// </summary>
public sealed record SearchDiagnosticsDto
{
    /// <summary>The full-text index answered. When false, the search fell back to substring matching.</summary>
    public required bool FullTextIndexUsed { get; init; }

    /// <summary>Whether the server has full-text search at all.</summary>
    public required bool FullTextAvailable { get; init; }

    /// <summary>The terms the typed text was understood as, after quotes and punctuation were read.</summary>
    public required IReadOnlyList<string> Terms { get; init; }

    public required long ElapsedMilliseconds { get; init; }
}

public sealed record SearchResultDto
{
    public required PagedResult<SearchHitDto> Page { get; init; }
    public required SearchDiagnosticsDto Diagnostics { get; init; }
}

// ---------------------------------------------------------------------------------------------
// Natural language
// ---------------------------------------------------------------------------------------------

/// <summary>
/// One filter the question was read as, so the person can see what it was turned into before
/// trusting the answer.
/// </summary>
/// <param name="Field">
/// Which filter, as a stable key — <c>type</c>, <c>status</c>, <c>approvalState</c> and so on. The
/// interface holds the labels for these in both its languages; the server does not send prose, so
/// nothing here has to be translated twice.
/// </param>
/// <param name="Value">
/// The value, as it is: the enumeration name for an enumeration, the catalogue name for a document
/// type or department, an ISO date for a date.
/// </param>
public sealed record InterpretedFilterDto(string Field, string Value);

/// <summary>
/// What a question was understood to mean. The question is never executed as written: it is turned
/// into the same filters the form on screen produces, and those filters are what runs.
/// </summary>
public sealed record SearchInterpretationDto
{
    public required string Question { get; init; }

    /// <summary>The filters the question was read as.</summary>
    public required IReadOnlyList<InterpretedFilterDto> Filters { get; init; }

    /// <summary>The free text left over after the filters were taken out; may be empty.</summary>
    public string? Text { get; init; }

    /// <summary>
    /// False when nothing in the question was recognised. The search still runs — as a plain text
    /// search — but the interface must say that the question was not understood as filters.
    /// </summary>
    public required bool Understood { get; init; }

    /// <summary>
    /// Which engine read the question. The demo reader is rule-based and must be shown as such
    /// everywhere it appears; it is never presented as a language model.
    /// </summary>
    public required AiOrigin Source { get; init; }
}

/// <summary>Where an answer came from. Mirrors the AI module: a demo reader is never shown as a model.</summary>
public enum AiOrigin
{
    Demo = 0,
    Model = 1
}

/// <summary>
/// A question, read. The filters are the same object the search form produces, so the question
/// cannot ask for anything the form could not ask for — and cannot reach past the authorization the
/// search applies to both.
/// </summary>
public sealed record InterpretedSearch(DocumentSearchRequest Request, SearchInterpretationDto Summary);

public sealed record NaturalLanguageSearchResultDto
{
    public required SearchInterpretationDto Interpretation { get; init; }
    public required SearchResultDto Result { get; init; }
}

// ---------------------------------------------------------------------------------------------
// Write models
// ---------------------------------------------------------------------------------------------

public sealed record NaturalLanguageSearchRequest
{
    public required string Question { get; init; }
    public PageRequest Paging { get; init; } = new();
}

// ---------------------------------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------------------------------

public interface ISearchService
{
    /// <summary>
    /// Runs a search. Authorization is part of the query, not a filter applied to its results: a
    /// document the caller may not see is never counted, never ranked and never paged over.
    /// </summary>
    Task<SearchResultDto> SearchAsync(DocumentSearchRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads a question in plain language and returns the filters it means. It never queries anything
/// itself: the caller runs the filters through <see cref="ISearchService"/>, which is where
/// authorization lives. An interpreter that could reach the data could also be talked into reaching
/// data the asker may not see.
/// </summary>
public interface IAiSearchService
{
    Task<InterpretedSearch> InterpretAsync(
        string question, PageRequest paging, CancellationToken cancellationToken = default);
}
