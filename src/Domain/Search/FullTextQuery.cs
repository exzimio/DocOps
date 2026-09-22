using System.Globalization;
using System.Text;

namespace DocOps.Domain.Search;

/// <summary>One term the user asked for, as it will be matched.</summary>
/// <param name="Text">The words themselves, already cleaned of anything the index cannot hold.</param>
/// <param name="IsPhrase">A quoted group, matched in order, rather than a single word.</param>
public sealed record SearchTerm(string Text, bool IsPhrase);

/// <summary>
/// Turns what a person typed into a SQL Server full-text search condition.
///
/// The condition goes into <c>CONTAINSTABLE</c>, which takes it as a string parameter — so the raw
/// text is never concatenated into the statement. That alone is not enough: the condition has its
/// own grammar, and a stray quote or a bare <c>AND</c> would make the whole search fail rather than
/// return nothing. So the input is taken apart into terms here and the condition is rebuilt from
/// them; nothing the user typed survives as syntax, only as quoted words.
/// </summary>
public static class FullTextQuery
{
    /// <summary>Single letters and digits match almost everything, and the index ignores most of them anyway.</summary>
    private const int MinimumTermLength = 2;

    /// <summary>A ceiling on how many terms one search may carry, so a pasted page cannot become a query.</summary>
    private const int MaxTerms = 12;

    /// <summary>
    /// Splits the text into the terms it asks for: a quoted group stays whole, everything else is one
    /// word per term. Anything that is not a letter or a digit separates words, which is also how the
    /// full-text index itself decides where words end.
    /// </summary>
    public static IReadOnlyList<SearchTerm> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        List<SearchTerm> terms = [];
        StringBuilder current = new();
        bool inPhrase = false;

        void Flush(bool phrase)
        {
            string text = Collapse(current.ToString());
            current.Clear();

            if (text.Length < MinimumTermLength || terms.Count >= MaxTerms)
            {
                return;
            }

            terms.Add(new SearchTerm(text, phrase && text.Contains(' ', StringComparison.Ordinal)));
        }

        foreach (char character in raw)
        {
            if (character is '"' or '«' or '»')
            {
                Flush(inPhrase);
                inPhrase = !inPhrase;
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                current.Append(character);
                continue;
            }

            // Inside quotes a separator keeps the words in one term; outside it ends the term.
            if (inPhrase)
            {
                current.Append(' ');
            }
            else
            {
                Flush(false);
            }
        }

        Flush(inPhrase);

        return terms;
    }

    /// <summary>
    /// Builds the <c>CONTAINS</c> condition for the given terms, or <c>null</c> when nothing in the
    /// text is searchable — in which case the caller must not run a full-text query at all, because
    /// an empty condition is a syntax error rather than a match-everything.
    /// </summary>
    public static string? Build(string? raw) => Build(Parse(raw));

    /// <inheritdoc cref="Build(string?)"/>
    public static string? Build(IReadOnlyList<SearchTerm> terms)
    {
        if (terms.Count == 0)
        {
            return null;
        }

        // Every term must be present. A prefix match on the last word of each lets "fatur" find
        // "faturas", which is what someone typing into a search box expects.
        return string.Join(
            " AND ",
            terms.Select(term => $"\"{term.Text}{(term.IsPhrase ? string.Empty : "*")}\""));
    }

    /// <summary>
    /// Collapses runs of separators and strips accents. The index folds accents itself for the
    /// language it was built with; folding here as well keeps the fallback path — which is a plain
    /// comparison against a case- and accent-insensitive collation — matching the same documents.
    /// </summary>
    private static string Collapse(string value)
    {
        string normalised = value.Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(normalised.Length);
        bool pendingSpace = false;

        foreach (char character in normalised)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                pendingSpace = false;
                builder.Append(character);
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
