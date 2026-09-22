using System.Diagnostics;
using System.Linq.Expressions;
using DocOps.Application.Abstractions;
using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Application.Search;
using DocOps.Domain.Documents;
using DocOps.Domain.Search;
using DocOps.Infrastructure.Documents;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Search;

/// <summary>
/// The search behind the search screen.
///
/// Everything happens in the database: the authorization predicate, the filters, the text match, the
/// score and the page. Nothing is fetched only to be discarded, and a document the caller may not
/// see never enters the count — the total on screen is the total the caller is allowed to know
/// about, not the total that exists.
/// </summary>
public sealed class SearchService(
    DocOpsDbContext dbContext,
    DocumentAccessResolver accessResolver,
    FullTextAvailability fullText,
    IDateTimeProvider clock) : ISearchService
{
    /// <summary>
    /// How much of a document's text is read back to build the excerpt. Whole extracted texts would
    /// be megabytes per page to render a line and a half of context.
    /// </summary>
    private const int SnippetSourceLength = 20_000;

    /// <summary>
    /// What a match in each part of a document is worth. Title first, because whoever names a
    /// document names what it is about; body text last, because a word appearing somewhere in twelve
    /// pages says the least. The weights are fixed and stated here rather than tuned invisibly.
    /// </summary>
    private const int TitleWeight = 5;

    private const int ReferenceWeight = 4;
    private const int MetadataWeight = 3;
    private const int SummaryWeight = 2;
    private const int TagWeight = 2;
    private const int ContentWeight = 1;

    private const double MaxWeight =
        TitleWeight + ReferenceWeight + MetadataWeight + SummaryWeight + TagWeight + ContentWeight;

    public async Task<SearchResultDto> SearchAsync(
        DocumentSearchRequest request, CancellationToken cancellationToken = default)
    {
        long startedAt = Stopwatch.GetTimestamp();

        AccessSubject subject = await accessResolver.GetSubjectAsync(cancellationToken);

        IQueryable<Document> query = dbContext.Documents
            .Where(DocumentAccessResolver.BuildVisibilityPredicate(subject, clock.UtcNow));

        query = DocumentFilters.ApplyFacets(dbContext, query, request, subject.UserId);

        IReadOnlyList<SearchTerm> terms = FullTextQuery.Parse(request.Query);
        string? condition = FullTextQuery.Build(terms);
        bool available = await fullText.IsEnabledAsync(cancellationToken);
        bool useIndex = available && condition is not null;

        IQueryable<ScoredDocument> scored = query.Select(Match(dbContext, request.Query, useIndex ? condition : null));

        if (terms.Count > 0)
        {
            scored = scored.Where(s =>
                s.InTitle || s.InReference || s.InMetadata || s.InSummary || s.InTag || s.InContent);
        }

        long total = await scored.LongCountAsync(cancellationToken);

        if (total == 0)
        {
            return new SearchResultDto
            {
                Page = PagedResult<SearchHitDto>.Empty(
                    request.Paging.NormalizedPage, request.Paging.NormalizedPageSize),
                Diagnostics = Diagnostics(useIndex, available, terms, startedAt)
            };
        }

        List<ScoredDocument> rows = await Order(scored, request, terms.Count > 0)
            .Skip(request.Paging.Skip)
            .Take(request.Paging.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        IReadOnlyList<SearchHitDto> hits = await ShapeAsync(rows, subject, terms, cancellationToken);

        return new SearchResultDto
        {
            Page = new PagedResult<SearchHitDto>(
                hits, request.Paging.NormalizedPage, request.Paging.NormalizedPageSize, total),
            Diagnostics = Diagnostics(useIndex, available, terms, startedAt)
        };
    }

    // -----------------------------------------------------------------------------------------
    // Matching and scoring
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the one expression that both finds and ranks, in whichever of the two shapes the server
    /// can answer.
    ///
    /// With the index, <paramref name="condition"/> carries every term and each column is asked
    /// whether it holds them all; word stems and accents are handled by the index itself. Without it,
    /// the same columns are scanned for the typed text as a single substring — a narrower question,
    /// which is why the answer says which of the two ran. The one thing that never changes between
    /// them is which documents the query is allowed to look at.
    /// </summary>
    private static Expression<Func<Document, ScoredDocument>> Match(
        DocOpsDbContext db, string? raw, string? condition)
    {
        string text = raw?.Trim() ?? string.Empty;
        bool empty = text.Length == 0;

        if (condition is null)
        {
            return document => new ScoredDocument
            {
                Id = document.Id,
                Title = document.Title,
                Reference = document.Reference,
                Status = document.Status,
                CreatedAtUtc = document.CreatedAtUtc,
                ModifiedAtUtc = document.ModifiedAtUtc,
                InTitle = !empty && document.Title.Contains(text),
                InReference = !empty && document.Reference.Contains(text),
                InMetadata = !empty && document.MetadataValues.Any(v =>
                    v.TextValue != null && v.TextValue.Contains(text)),
                InSummary = !empty && document.Summary != null && document.Summary.Contains(text),
                InTag = !empty && document.Tags.Any(t => t.Tag!.Name.Contains(text)),
                InContent = !empty && db.DocumentVersions.Any(v =>
                    v.DocumentId == document.Id
                    && v.IsCurrent
                    && v.ExtractedText != null
                    && v.ExtractedText.Contains(text))
            };
        }

        return document => new ScoredDocument
        {
            Id = document.Id,
            Title = document.Title,
            Reference = document.Reference,
            Status = document.Status,
            CreatedAtUtc = document.CreatedAtUtc,
            ModifiedAtUtc = document.ModifiedAtUtc,
            InTitle = EF.Functions.Contains(document.Title, condition),
            InReference = EF.Functions.Contains(document.Reference, condition),
            InMetadata = document.MetadataValues.Any(v =>
                v.TextValue != null && EF.Functions.Contains(v.TextValue, condition)),
            InSummary = document.Summary != null && EF.Functions.Contains(document.Summary, condition),

            // Tags are matched by substring even here. A tag is one short label, so a substring is
            // the right question to ask of it — and it is the only question available: the tenant
            // filter turns every join to Tags into a derived table, and CONTAINS only reads columns
            // of real ones.
            InTag = !empty && document.Tags.Any(t => t.Tag!.Name.Contains(text)),
            InContent = db.DocumentVersions.Any(v =>
                v.DocumentId == document.Id
                && v.IsCurrent
                && v.ExtractedText != null
                && EF.Functions.Contains(v.ExtractedText, condition))
        };
    }

    /// <summary>
    /// Orders the matches. Relevance is only an order when there is text to be relevant to; asking
    /// for it without text falls back to the newest first, because every document would otherwise
    /// score the same and the page boundaries would be arbitrary. Every order ends on the identifier
    /// so that two equal rows never swap places between one page and the next.
    /// </summary>
    private static IQueryable<ScoredDocument> Order(
        IQueryable<ScoredDocument> scored, DocumentSearchRequest request, bool hasText)
    {
        if (request.SortBy == DocumentSortField.Relevance && hasText)
        {
            return scored
                .OrderByDescending(s =>
                    (s.InTitle ? TitleWeight : 0)
                    + (s.InReference ? ReferenceWeight : 0)
                    + (s.InMetadata ? MetadataWeight : 0)
                    + (s.InSummary ? SummaryWeight : 0)
                    + (s.InTag ? TagWeight : 0)
                    + (s.InContent ? ContentWeight : 0))
                .ThenByDescending(s => s.CreatedAtUtc)
                .ThenBy(s => s.Id);
        }

        return (request.SortBy, request.SortDescending) switch
        {
            (DocumentSortField.Title, true) => scored.OrderByDescending(s => s.Title).ThenBy(s => s.Id),
            (DocumentSortField.Title, false) => scored.OrderBy(s => s.Title).ThenBy(s => s.Id),
            (DocumentSortField.Reference, true) => scored.OrderByDescending(s => s.Reference).ThenBy(s => s.Id),
            (DocumentSortField.Reference, false) => scored.OrderBy(s => s.Reference).ThenBy(s => s.Id),
            (DocumentSortField.Status, true) => scored.OrderByDescending(s => s.Status)
                .ThenByDescending(s => s.CreatedAtUtc).ThenBy(s => s.Id),
            (DocumentSortField.Status, false) => scored.OrderBy(s => s.Status)
                .ThenByDescending(s => s.CreatedAtUtc).ThenBy(s => s.Id),
            (DocumentSortField.ModifiedAt, true) => scored
                .OrderByDescending(s => s.ModifiedAtUtc ?? s.CreatedAtUtc).ThenBy(s => s.Id),
            (DocumentSortField.ModifiedAt, false) => scored
                .OrderBy(s => s.ModifiedAtUtc ?? s.CreatedAtUtc).ThenBy(s => s.Id),
            (_, false) => scored.OrderBy(s => s.CreatedAtUtc).ThenBy(s => s.Id),
            _ => scored.OrderByDescending(s => s.CreatedAtUtc).ThenBy(s => s.Id)
        };
    }

    // -----------------------------------------------------------------------------------------
    // Shaping
    // -----------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<SearchHitDto>> ShapeAsync(
        List<ScoredDocument> rows,
        AccessSubject subject,
        IReadOnlyList<SearchTerm> terms,
        CancellationToken cancellationToken)
    {
        Guid[] ids = [.. rows.Select(row => row.Id)];

        // Re-applying the visibility predicate costs one predicate and removes a whole class of
        // mistake: this query can only ever load documents the caller was already allowed to see.
        Dictionary<Guid, DocumentListItemDto> documents = await dbContext.Documents
            .Where(DocumentAccessResolver.BuildVisibilityPredicate(subject, clock.UtcNow))
            .Where(d => ids.Contains(d.Id))
            .Select(DocumentProjections.ToListItem(dbContext))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        Dictionary<Guid, string> texts = [];
        Guid[] withContent = [.. rows.Where(row => row.InContent).Select(row => row.Id)];

        if (withContent.Length > 0)
        {
            texts = await dbContext.DocumentVersions
                .Where(v => withContent.Contains(v.DocumentId) && v.IsCurrent && v.ExtractedText != null)
                .Select(v => new
                {
                    v.DocumentId,
                    Text = v.ExtractedText!.Length > SnippetSourceLength
                        ? v.ExtractedText.Substring(0, SnippetSourceLength)
                        : v.ExtractedText
                })
                .ToDictionaryAsync(row => row.DocumentId, row => row.Text, cancellationToken);
        }

        List<SearchHitDto> hits = new(rows.Count);

        foreach (ScoredDocument row in rows)
        {
            if (!documents.TryGetValue(row.Id, out DocumentListItemDto? document))
            {
                continue;
            }

            SearchSnippet? snippet = row.InContent && texts.TryGetValue(row.Id, out string? text)
                ? SnippetExtractor.Extract(text, terms)
                : null;

            hits.Add(new SearchHitDto
            {
                Document = document,
                Score = terms.Count == 0 ? 0 : Math.Round(row.Weight / MaxWeight, 4),
                MatchedIn = row.Fields,
                Snippet = snippet is null
                    ? null
                    : new SearchSnippetDto(snippet.Text, snippet.Start, snippet.Length)
            });
        }

        return hits;
    }

    private static SearchDiagnosticsDto Diagnostics(
        bool useIndex, bool available, IReadOnlyList<SearchTerm> terms, long startedAt) =>
        new()
        {
            FullTextIndexUsed = useIndex && terms.Count > 0,
            FullTextAvailable = available,
            Terms = [.. terms.Select(term => term.Text)],
            ElapsedMilliseconds = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds
        };

    /// <summary>
    /// A document that matched, with where it matched and the columns needed to order it. Everything
    /// the order depends on is read in the same query, so the database decides which rows the page
    /// holds — the alternative is fetching the catalogue to sort it here.
    /// </summary>
    private sealed class ScoredDocument
    {
        public required Guid Id { get; init; }
        public required string Title { get; init; }
        public required string Reference { get; init; }
        public required DocumentStatus Status { get; init; }
        public required DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset? ModifiedAtUtc { get; init; }

        public bool InTitle { get; init; }
        public bool InReference { get; init; }
        public bool InMetadata { get; init; }
        public bool InSummary { get; init; }
        public bool InTag { get; init; }
        public bool InContent { get; init; }

        public int Weight =>
            (InTitle ? TitleWeight : 0)
            + (InReference ? ReferenceWeight : 0)
            + (InMetadata ? MetadataWeight : 0)
            + (InSummary ? SummaryWeight : 0)
            + (InTag ? TagWeight : 0)
            + (InContent ? ContentWeight : 0);

        public IReadOnlyList<SearchMatchField> Fields
        {
            get
            {
                List<SearchMatchField> fields = [];

                if (InTitle)
                {
                    fields.Add(SearchMatchField.Title);
                }

                if (InReference)
                {
                    fields.Add(SearchMatchField.Reference);
                }

                if (InMetadata)
                {
                    fields.Add(SearchMatchField.Metadata);
                }

                if (InSummary)
                {
                    fields.Add(SearchMatchField.Summary);
                }

                if (InTag)
                {
                    fields.Add(SearchMatchField.Tag);
                }

                if (InContent)
                {
                    fields.Add(SearchMatchField.Content);
                }

                return fields;
            }
        }
    }
}
