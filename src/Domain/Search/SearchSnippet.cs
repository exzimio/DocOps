using System.Globalization;
using System.Text;

namespace DocOps.Domain.Search;

/// <summary>A piece of a document's text around a match, and where inside that piece the match sits.</summary>
/// <param name="Text">The excerpt, with an ellipsis where text was cut away.</param>
/// <param name="Start">Index of the match inside <paramref name="Text"/>.</param>
/// <param name="Length">Length of the match inside <paramref name="Text"/>.</param>
public sealed record SearchSnippet(string Text, int Start, int Length);

/// <summary>
/// Pulls the sentence a search term was found in, so a result can show why it matched instead of
/// asking the reader to open the document and look. The excerpt is cut from the stored text and is
/// never rewritten: what the list shows is what the document says.
/// </summary>
public static class SnippetExtractor
{
    private const int Window = 160;

    /// <summary>
    /// Finds the first of <paramref name="terms"/> in <paramref name="text"/> and returns the
    /// surrounding words, or <c>null</c> when none of them appears — which happens legitimately when
    /// a document matched on its title or on a metadata value rather than on its body.
    /// </summary>
    public static SearchSnippet? Extract(string? text, IReadOnlyList<SearchTerm> terms)
    {
        if (string.IsNullOrWhiteSpace(text) || terms.Count == 0)
        {
            return null;
        }

        // Compared without accents and without case, because that is how the index matched them.
        string haystack = Fold(text);

        foreach (SearchTerm term in terms)
        {
            string needle = Fold(term.Text);
            int at = haystack.IndexOf(needle, StringComparison.Ordinal);

            if (at < 0)
            {
                continue;
            }

            return Cut(text, at, needle.Length);
        }

        return null;
    }

    private static SearchSnippet Cut(string text, int matchStart, int matchLength)
    {
        int from = Math.Max(0, matchStart - (Window / 2));
        int to = Math.Min(text.Length, matchStart + matchLength + (Window / 2));

        // Step back to a word boundary so the excerpt does not open mid-word — but only so far. An
        // extracted text can hold an unbroken run of hundreds of characters (a hash, an encoded
        // attachment), and walking to the start of one would return most of the document.
        const int reach = 30;
        int floor = Math.Max(0, from - reach);
        int ceiling = Math.Min(text.Length, to + reach);

        while (from > floor && !char.IsWhiteSpace(text[from - 1]))
        {
            from--;
        }

        while (to < ceiling && !char.IsWhiteSpace(text[to]))
        {
            to++;
        }

        // Then pull both edges in to a real character. Business documents are full of rules and
        // underlines, and an excerpt that opens on "==================" tells the reader nothing.
        while (from < matchStart && !char.IsLetterOrDigit(text[from]))
        {
            from++;
        }

        while (to > matchStart + matchLength && !char.IsLetterOrDigit(text[to - 1]))
        {
            to--;
        }

        string prefix = from > 0 ? "… " : string.Empty;
        string suffix = to < text.Length ? " …" : string.Empty;
        string body = Whitespace(text[from..to]);

        // Collapsing whitespace moved the match, so it is located again inside the excerpt rather
        // than by arithmetic on the original offsets. It is certainly there: the window contains it.
        int offset = Fold(body).IndexOf(Fold(text.Substring(matchStart, matchLength)), StringComparison.Ordinal);

        return offset < 0
            ? new SearchSnippet(prefix + body + suffix, 0, 0)
            : new SearchSnippet(prefix + body + suffix, prefix.Length + offset, matchLength);
    }

    private static string Whitespace(string value)
    {
        StringBuilder builder = new(value.Length);
        bool space = false;

        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Lowercases and removes accents while keeping every character in place, so an index found in
    /// the folded text is also valid in the original.
    /// </summary>
    private static string Fold(string value)
    {
        StringBuilder builder = new(value.Length);

        foreach (char character in value)
        {
            string decomposed = character.ToString().Normalize(NormalizationForm.FormD);
            char first = decomposed[0];

            builder.Append(
                CharUnicodeInfo.GetUnicodeCategory(first) == UnicodeCategory.NonSpacingMark
                    ? char.ToLowerInvariant(character)
                    : char.ToLowerInvariant(first));
        }

        return builder.ToString();
    }
}
