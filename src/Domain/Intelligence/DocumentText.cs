using System.Globalization;
using System.Text;

namespace DocOps.Domain.Intelligence;

/// <summary>One line of a document's text, with where it sits.</summary>
public sealed record TextLine(int Number, int Page, string? Section, string Text);

/// <summary>
/// A pointer into a document's text: the line, the page and the section it belongs to, and the
/// words quoted from it. Only ever built from a <see cref="DocumentText"/>, never from free text.
/// </summary>
public sealed record TextReference(int Line, int Page, string? Section, string Excerpt);

/// <summary>
/// A document's extracted text, split into numbered lines with their page and section. It is the
/// single source a citation may point into: anything a provider claims to quote is checked against
/// it before it is shown to anyone.
/// </summary>
public sealed class DocumentText
{
    /// <summary>Pages are separated by a form feed, which is how the PDF extractor emits them.</summary>
    public const char PageBreak = '\f';

    private readonly List<TextLine> _lines = [];

    public DocumentText(string? text)
    {
        Raw = text ?? string.Empty;

        string[] rawLines = Raw.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int page = 1;
        string? section = null;

        for (int index = 0; index < rawLines.Length; index++)
        {
            string line = rawLines[index];

            // A form feed may sit at the start of a line or on its own; either way it opens a page.
            while (line.Contains(PageBreak, StringComparison.Ordinal))
            {
                int at = line.IndexOf(PageBreak, StringComparison.Ordinal);
                if (at > 0)
                {
                    Add(line[..at], page, section);
                }

                page++;
                line = line[(at + 1)..];
            }

            string trimmed = line.Trim();

            if (IsUnderline(trimmed))
            {
                continue;
            }

            string? next = index + 1 < rawLines.Length ? rawLines[index + 1].Trim() : null;

            if (IsHeading(trimmed, next))
            {
                section = ToTitle(trimmed);
            }

            Add(line, page, section);
        }
    }

    public string Raw { get; }

    public IReadOnlyList<TextLine> Lines => _lines;

    public bool IsEmpty => _lines.All(l => string.IsNullOrWhiteSpace(l.Text));

    public TextLine? Line(int number) =>
        number >= 1 && number <= _lines.Count ? _lines[number - 1] : null;

    /// <summary>Builds a reference to a line that is known to exist.</summary>
    public TextReference Reference(TextLine line, string? excerpt = null) =>
        new(line.Number, line.Page, line.Section, Shorten(excerpt ?? line.Text.Trim()));

    /// <summary>
    /// True only when the quoted words really appear on the line the reference points at. This is
    /// what stops an invented citation — from a model or from a bug — from reaching the interface.
    /// </summary>
    public bool Verifies(TextReference reference)
    {
        TextLine? line = Line(reference.Line);

        return line is not null
            && line.Page == reference.Page
            && !string.IsNullOrWhiteSpace(reference.Excerpt)
            && Normalise(line.Text).Contains(Normalise(reference.Excerpt), StringComparison.Ordinal);
    }

    /// <summary>
    /// Finds where a quotation actually sits. Used for providers that return the words they relied
    /// on but not a trustworthy position. Null when the words are not in the document.
    /// </summary>
    public TextReference? Locate(string? excerpt)
    {
        if (string.IsNullOrWhiteSpace(excerpt))
        {
            return null;
        }

        string wanted = Normalise(excerpt);

        TextLine? line = _lines.FirstOrDefault(l => Normalise(l.Text).Contains(wanted, StringComparison.Ordinal));

        return line is null ? null : Reference(line, excerpt.Trim());
    }

    /// <summary>Lower case, accents removed, runs of whitespace collapsed: how text is compared.</summary>
    public static string Normalise(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(decomposed.Length);
        bool lastWasSpace = false;

        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            builder.Append(char.ToLowerInvariant(c));
            lastWasSpace = false;
        }

        return builder.ToString().TrimEnd();
    }

    private void Add(string text, int page, string? section) =>
        _lines.Add(new TextLine(_lines.Count + 1, page, section, text.TrimEnd()));

    private static bool IsUnderline(string line) =>
        line.Length >= 3 && line.All(c => c is '-' or '=' or '_');

    /// <summary>A heading is an underlined line, or a short line written entirely in capitals.</summary>
    private static bool IsHeading(string line, string? next)
    {
        if (line.Length is < 3 or > 60 || !line.Any(char.IsLetter))
        {
            return false;
        }

        if (next is not null && IsUnderline(next))
        {
            return true;
        }

        // Capitals only — "ª" and "º" are letters but neither upper nor lower case — and mostly
        // letters, so "IBAN PT50 0000 0000" is a line of data, not a heading.
        int letters = line.Count(char.IsLetter);
        int visible = line.Count(c => !char.IsWhiteSpace(c));

        return !line.Contains(':', StringComparison.Ordinal)
            && !line.Any(char.IsLower)
            && line.Count(char.IsUpper) >= 3
            && letters * 2 >= visible;
    }

    private static string ToTitle(string heading)
    {
        string lower = heading.ToLower(CultureInfo.GetCultureInfo("pt-PT"));
        return char.ToUpper(lower[0], CultureInfo.GetCultureInfo("pt-PT")) + lower[1..];
    }

    private static string Shorten(string excerpt) =>
        excerpt.Length <= 200 ? excerpt : excerpt[..197] + "…";
}
