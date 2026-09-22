using System.Globalization;
using DocOps.Application.Abstractions;
using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Application.Search;
using DocOps.Domain.Documents;
using DocOps.Domain.Search;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Search;

/// <summary>
/// Reads a question written in plain language and returns the filters it means, by rule.
///
/// It is the demonstration reader: a table of phrases and a catalogue lookup, nothing more. It is
/// deterministic — the same question always gives the same filters — and everything it understood is
/// handed back so the person can see it before trusting the results. It is never presented as a
/// language model, and it does not need one: the questions this application is asked are about its
/// own catalogue, and the catalogue is right here.
///
/// What it does not understand is not guessed at. Leftover words become the free text of an ordinary
/// search, which is the honest thing to do with words nobody has taught it.
/// </summary>
public sealed class DemoSearchInterpreter(
    DocOpsDbContext dbContext,
    IDateTimeProvider clock) : IAiSearchService
{
    public async Task<InterpretedSearch> InterpretAsync(
        string question, PageRequest paging, CancellationToken cancellationToken = default)
    {
        Reading reading = new(SearchText.Fold(question));

        List<InterpretedFilterDto> filters = [];

        // Order matters. The longest, most specific phrases are consumed first so that "aguarda
        // aprovação" is read as an approval state and not as the status "aprovado" plus a stray word.
        List<DocumentApprovalState> approvals = reading.TakeAll(SearchVocabulary.ApprovalStates, filters, "approvalState");
        List<DocumentWorkflowState> workflows = reading.TakeAll(SearchVocabulary.WorkflowStates, filters, "workflowState");
        List<DocumentStatus> statuses = reading.TakeAll(SearchVocabulary.Statuses, filters, "status");
        List<SecurityClassification> classifications =
            reading.TakeAll(SearchVocabulary.Classifications, filters, "classification");

        bool onlyMine = reading.TakeAny(SearchVocabulary.Mine);

        if (onlyMine)
        {
            filters.Add(new InterpretedFilterDto("onlyMine", "true"));
        }

        (DateTimeOffset? from, DateTimeOffset? to) = reading.TakePeriod(clock.UtcNow, filters);

        List<Guid> typeIds = await TakeCatalogueAsync(
            reading,
            filters,
            "type",
            dbContext.DocumentTypes.Where(t => t.IsActive).Select(t => new CatalogueEntry(t.Id, t.Name, t.Code)),
            cancellationToken);

        List<Guid> departmentIds = await TakeCatalogueAsync(
            reading,
            filters,
            "department",
            dbContext.Departments.Select(d => new CatalogueEntry(d.Id, d.Name, d.Code)),
            cancellationToken);

        List<Guid> tagIds = await TakeCatalogueAsync(
            reading,
            filters,
            "tag",
            dbContext.Tags.Select(t => new CatalogueEntry(t.Id, t.Name, null)),
            cancellationToken);

        string? text = reading.Remainder();

        if (!string.IsNullOrEmpty(text))
        {
            filters.Add(new InterpretedFilterDto("text", text));
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
            OnlyMine = onlyMine,
            CreatedFromUtc = from,
            CreatedToUtc = to,
            SortBy = string.IsNullOrEmpty(text) ? DocumentSortField.CreatedAt : DocumentSortField.Relevance,
            SortDescending = true,
            Paging = paging
        };

        // "Understood" means filters were recognised. A question that produced nothing but free text
        // was not understood as a question — it was read as a search box, and the interface says so.
        bool understood = filters.Any(filter => filter.Field != "text");

        return new InterpretedSearch(
            request,
            new SearchInterpretationDto
            {
                Question = question.Trim(),
                Filters = filters,
                Text = text,
                Understood = understood,
                Source = AiOrigin.Demo
            });
    }

    /// <summary>
    /// Matches the words still unread against a catalogue the tenant owns. The catalogue is queried
    /// through the same context as everything else, so a question can only ever name types,
    /// departments and tags belonging to the caller's tenant.
    /// </summary>
    private static async Task<List<Guid>> TakeCatalogueAsync(
        Reading reading,
        List<InterpretedFilterDto> filters,
        string field,
        IQueryable<CatalogueEntry> catalogue,
        CancellationToken cancellationToken)
    {
        List<Guid> matched = [];

        if (reading.IsExhausted)
        {
            return matched;
        }

        List<CatalogueEntry> entries = await catalogue.ToListAsync(cancellationToken);

        foreach (CatalogueEntry entry in entries)
        {
            // A catalogue name is matched by its own significant words: someone asking for "faturas"
            // means the type called "Fatura de fornecedor", and will not type its full name.
            string[] keywords =
            [
                .. SearchText.Fold(entry.Name)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(word => word.Length >= 4 && !SearchVocabulary.Noise.Contains(word))
            ];

            if (keywords.Length == 0 || !reading.TakeWords(keywords, entry.Code))
            {
                continue;
            }

            matched.Add(entry.Id);
            filters.Add(new InterpretedFilterDto(field, entry.Name));
        }

        return matched;
    }

    private sealed record CatalogueEntry(Guid Id, string Name, string? Code);

    /// <summary>
    /// The question as it is being read: the folded words, and which of them have already been
    /// accounted for. A word is consumed at most once, so a phrase cannot be counted twice and the
    /// words nobody claimed are exactly what is left at the end.
    /// </summary>
    private sealed class Reading
    {
        private readonly string[] _words;
        private readonly bool[] _used;

        public Reading(string folded)
        {
            _words = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            _used = new bool[_words.Length];
        }

        public bool IsExhausted => _used.All(used => used);

        /// <summary>Consumes every phrase from the table that appears, in the order given.</summary>
        public List<T> TakeAll<T>(
            IReadOnlyList<SearchPhrase<T>> table, List<InterpretedFilterDto> filters, string field)
            where T : struct, Enum
        {
            List<T> found = [];

            foreach (SearchPhrase<T> entry in table)
            {
                if (!TakePhrase(entry.Phrase) || found.Contains(entry.Value))
                {
                    continue;
                }

                found.Add(entry.Value);
                filters.Add(new InterpretedFilterDto(field, entry.Value.ToString()));
            }

            return found;
        }

        public bool TakeAny(IReadOnlyList<string> phrases)
        {
            bool any = false;

            foreach (string phrase in phrases)
            {
                any |= TakePhrase(phrase);
            }

            return any;
        }

        /// <summary>
        /// Consumes a phrase where it appears as whole words that have not been read yet. Matching
        /// whole words is what stops "aprovado" from being found inside "aprovadores".
        /// </summary>
        public bool TakePhrase(string phrase)
        {
            string[] parts = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0 || parts.Length > _words.Length)
            {
                return false;
            }

            for (int start = 0; start + parts.Length <= _words.Length; start++)
            {
                bool hit = true;

                for (int offset = 0; offset < parts.Length; offset++)
                {
                    if (_used[start + offset]
                        || !string.Equals(_words[start + offset], parts[offset], StringComparison.Ordinal))
                    {
                        hit = false;
                        break;
                    }
                }

                if (!hit)
                {
                    continue;
                }

                for (int offset = 0; offset < parts.Length; offset++)
                {
                    _used[start + offset] = true;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Consumes any unread word that is the same word as one of <paramref name="keywords"/>, or
        /// that is exactly the code. One hit is enough: catalogue names are several words long and a
        /// question names them by one.
        /// </summary>
        public bool TakeWords(string[] keywords, string? code)
        {
            string? folded = code is null ? null : SearchText.Fold(code);
            bool hit = false;

            for (int index = 0; index < _words.Length; index++)
            {
                if (_used[index])
                {
                    continue;
                }

                bool matches =
                    (folded is not null && folded.Length >= 3
                        && string.Equals(_words[index], folded, StringComparison.Ordinal))
                    || keywords.Any(keyword => SearchText.SameWord(_words[index], keyword));

                if (!matches)
                {
                    continue;
                }

                _used[index] = true;
                hit = true;
            }

            return hit;
        }

        /// <summary>
        /// Reads a period out of the question. Only the forms written here are understood; a date
        /// nobody taught it stays in the leftover text rather than becoming a wrong range.
        /// </summary>
        public (DateTimeOffset? From, DateTimeOffset? To) TakePeriod(
            DateTimeOffset now, List<InterpretedFilterDto> filters)
        {
            DateTimeOffset today = new(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset? from = null;
            DateTimeOffset? to = null;

            if (TakePhrase("hoje") || TakePhrase("today"))
            {
                (from, to) = (today, today.AddDays(1));
            }
            else if (TakePhrase("ontem") || TakePhrase("yesterday"))
            {
                (from, to) = (today.AddDays(-1), today);
            }
            else if (TakePhrase("esta semana") || TakePhrase("this week"))
            {
                from = today.AddDays(-(int)today.DayOfWeek + (today.DayOfWeek == DayOfWeek.Sunday ? -6 : 1));
                to = from.Value.AddDays(7);
            }
            else if (TakePhrase("semana passada") || TakePhrase("last week"))
            {
                DateTimeOffset monday =
                    today.AddDays(-(int)today.DayOfWeek + (today.DayOfWeek == DayOfWeek.Sunday ? -6 : 1));
                (from, to) = (monday.AddDays(-7), monday);
            }
            else if (TakePhrase("este mes") || TakePhrase("this month"))
            {
                from = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
                to = from.Value.AddMonths(1);
            }
            else if (TakePhrase("mes passado") || TakePhrase("last month"))
            {
                to = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
                from = to.Value.AddMonths(-1);
            }
            else if (TakePhrase("este ano") || TakePhrase("this year"))
            {
                from = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
                to = from.Value.AddYears(1);
            }
            else if (TakePhrase("ano passado") || TakePhrase("last year"))
            {
                to = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
                from = to.Value.AddYears(-1);
            }
            else if (TakeSpan(today, out DateTimeOffset spanFrom))
            {
                from = spanFrom;
            }

            if (from is { } start)
            {
                filters.Add(new InterpretedFilterDto(
                    "createdFrom", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            }

            if (to is { } end)
            {
                filters.Add(new InterpretedFilterDto(
                    "createdTo", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            }

            return (from, to);
        }

        /// <summary>Reads "últimos 30 dias" and its relatives, where the number is part of the question.</summary>
        private bool TakeSpan(DateTimeOffset today, out DateTimeOffset from)
        {
            from = default;

            string[] openers = ["ultimos", "ultimas", "last"];
            string[] days = ["dias", "days"];
            string[] weeks = ["semanas", "weeks"];
            string[] months = ["meses", "months"];

            for (int index = 0; index + 2 < _words.Length; index++)
            {
                if (_used[index] || _used[index + 1] || _used[index + 2]
                    || !openers.Contains(_words[index])
                    || !int.TryParse(_words[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int count)
                    || count is < 1 or > 3650)
                {
                    continue;
                }

                string unit = _words[index + 2];

                from = unit switch
                {
                    _ when days.Contains(unit) => today.AddDays(-count),
                    _ when weeks.Contains(unit) => today.AddDays(-7 * count),
                    _ when months.Contains(unit) => today.AddMonths(-count),
                    _ => default
                };

                if (from == default)
                {
                    continue;
                }

                _used[index] = _used[index + 1] = _used[index + 2] = true;

                return true;
            }

            return false;
        }

        /// <summary>What nobody claimed, with the filler words dropped. Empty when nothing is left.</summary>
        public string? Remainder()
        {
            string[] left =
            [
                .. _words
                    .Where((word, index) => !_used[index] && !SearchVocabulary.Noise.Contains(word))
            ];

            return left.Length == 0 ? null : string.Join(' ', left);
        }
    }
}
