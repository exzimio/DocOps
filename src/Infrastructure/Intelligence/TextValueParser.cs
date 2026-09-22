using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DocOps.Infrastructure.Intelligence;

/// <summary>
/// Reads typed values out of free text the way they are written in Portuguese and English business
/// documents: "1 906,50 €", "1,906.50", "03/09/2026", "1 de outubro de 2026", "March 3, 2026".
/// Every value is returned normalised (invariant decimal, ISO date) so it binds to metadata exactly
/// like a value typed into the form.
/// </summary>
internal static partial class TextValueParser
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly Dictionary<string, int> Months = new(StringComparer.Ordinal)
    {
        ["janeiro"] = 1, ["fevereiro"] = 2, ["marco"] = 3, ["abril"] = 4, ["maio"] = 5, ["junho"] = 6,
        ["julho"] = 7, ["agosto"] = 8, ["setembro"] = 9, ["outubro"] = 10, ["novembro"] = 11, ["dezembro"] = 12,
        ["january"] = 1, ["february"] = 2, ["march"] = 3, ["april"] = 4, ["may"] = 5, ["june"] = 6,
        ["july"] = 7, ["august"] = 8, ["september"] = 9, ["october"] = 10, ["november"] = 11, ["december"] = 12
    };

    /// <summary>
    /// Lower-cases and strips accents one character at a time, so positions in the folded string are
    /// positions in the original. That lets a label be found in folded text and the value be read from
    /// the original, with its accents intact.
    /// </summary>
    public static string Fold(string value)
    {
        StringBuilder builder = new(value.Length);

        foreach (char c in value)
        {
            string decomposed = c.ToString().Normalize(NormalizationForm.FormD);
            builder.Append(char.ToLowerInvariant(decomposed[0]));
        }

        return builder.ToString();
    }

    /// <summary>
    /// First amount in the text, as an invariant decimal string. Percentages are skipped, and an
    /// amount written without decimals only counts when a currency is named alongside it: otherwise
    /// "VAT GB 123 4567 89" — a registration number — would be read as a VAT amount of 123.
    /// </summary>
    public static string? ParseAmount(string text)
    {
        bool currencyNamed = ParseCurrency(text) is not null;
        string? whole = null;

        foreach (Match match in AmountPattern().Matches(text))
        {
            string rest = text[(match.Index + match.Length)..].TrimStart();

            if (rest.StartsWith('%') || !TryParseAmount(match.Value, out decimal amount))
            {
                continue;
            }

            if (HasDecimals(match.Value))
            {
                return amount.ToString("0.00", Invariant);
            }

            whole ??= amount.ToString("0.00", Invariant);
        }

        return currencyNamed ? whole : null;
    }

    private static bool HasDecimals(string raw)
    {
        int last = raw.LastIndexOfAny(['.', ',']);
        return last >= 0 && raw.Length - last - 1 is 1 or 2;
    }

    /// <summary>First whole number in the text, e.g. "90 dias" gives "90".</summary>
    public static string? ParseInteger(string text)
    {
        Match match = IntegerPattern().Match(text);
        return match.Success ? match.Value : null;
    }

    /// <summary>First date in the text, as yyyy-MM-dd. Day-first, as dates are written in Portugal.</summary>
    public static string? ParseDate(string text)
    {
        string folded = Fold(text);

        (int Index, DateOnly Date)? best = null;

        foreach (Match match in NumericDatePattern().Matches(folded))
        {
            if (TryDate(match.Groups["y"].Value, match.Groups["m"].Value, match.Groups["d"].Value, out DateOnly date))
            {
                best = Earliest(best, match.Index, date);
            }
        }

        foreach (Match match in IsoDatePattern().Matches(folded))
        {
            if (TryDate(match.Groups["y"].Value, match.Groups["m"].Value, match.Groups["d"].Value, out DateOnly date))
            {
                best = Earliest(best, match.Index, date);
            }
        }

        foreach (Match match in WrittenDatePattern().Matches(folded))
        {
            if (Months.TryGetValue(match.Groups["month"].Value, out int month)
                && TryDate(match.Groups["y"].Value, month.ToString(Invariant), match.Groups["d"].Value, out DateOnly date))
            {
                best = Earliest(best, match.Index, date);
            }
        }

        foreach (Match match in DayMonthYearPattern().Matches(folded))
        {
            if (Months.TryGetValue(match.Groups["month"].Value, out int month)
                && TryDate(match.Groups["y"].Value, month.ToString(Invariant), match.Groups["d"].Value, out DateOnly date))
            {
                best = Earliest(best, match.Index, date);
            }
        }

        foreach (Match match in EnglishDatePattern().Matches(folded))
        {
            if (Months.TryGetValue(match.Groups["month"].Value, out int month)
                && TryDate(match.Groups["y"].Value, month.ToString(Invariant), match.Groups["d"].Value, out DateOnly date))
            {
                best = Earliest(best, match.Index, date);
            }
        }

        return best?.Date.ToString("yyyy-MM-dd", Invariant);
    }

    /// <summary>ISO currency code named or symbolised in the text, or null.</summary>
    public static string? ParseCurrency(string text)
    {
        if (text.Contains('€', StringComparison.Ordinal) || EuroWord().IsMatch(text))
        {
            return "EUR";
        }

        if (text.Contains('£', StringComparison.Ordinal) || PoundWord().IsMatch(text))
        {
            return "GBP";
        }

        if (text.Contains("US$", StringComparison.Ordinal) || text.Contains('$', StringComparison.Ordinal) || DollarWord().IsMatch(text))
        {
            return "USD";
        }

        return null;
    }

    /// <summary>
    /// Cleans a free-text value read after a label: stops at a column gap or at the start of an
    /// identification clause ("…, NIF 509 …"), and drops trailing punctuation.
    /// </summary>
    public static string? CleanText(string text)
    {
        string value = text.Trim();

        Match gap = ColumnGap().Match(value);
        if (gap.Success)
        {
            value = value[..gap.Index];
        }

        Match clause = IdentificationClause().Match(value);
        if (clause.Success)
        {
            value = value[..clause.Index];
        }

        value = value.Trim().TrimEnd(',', ';', ':', '—', '-', '–').Trim();

        if (value.EndsWith('.') && !EndsWithAbbreviation(value))
        {
            value = value[..^1].TrimEnd();
        }

        if (value.Length > 200)
        {
            value = value[..200].TrimEnd();
        }

        return value.Length == 0 ? null : value;
    }

    /// <summary>True for "Lda.", "S.A.", "Inc.": a full stop that belongs to the name.</summary>
    private static bool EndsWithAbbreviation(string value)
    {
        int start = value.LastIndexOf(' ') + 1;
        string token = value[start..].TrimEnd('.');

        return token.Length is > 0 and <= 4 || token.Contains('.', StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads "1 906,50", "1.906,50", "1,906.50", "1906.5" and "1906,50". The last separator followed
    /// by one or two digits is the decimal separator; every other separator groups thousands.
    /// </summary>
    internal static bool TryParseAmount(string raw, out decimal amount)
    {
        string compact = new([.. raw.Where(c => char.IsDigit(c) || c is '.' or ',' or '-')]);

        int lastSeparator = compact.LastIndexOfAny(['.', ',']);
        string normalised;

        if (lastSeparator >= 0 && compact.Length - lastSeparator - 1 is 1 or 2)
        {
            string whole = new([.. compact[..lastSeparator].Where(c => char.IsDigit(c) || c == '-')]);
            normalised = $"{whole}.{compact[(lastSeparator + 1)..]}";
        }
        else
        {
            normalised = new([.. compact.Where(c => char.IsDigit(c) || c == '-')]);
        }

        return decimal.TryParse(normalised, NumberStyles.Number, Invariant, out amount);
    }

    private static (int Index, DateOnly Date) Earliest((int Index, DateOnly Date)? current, int index, DateOnly date) =>
        current is null || index < current.Value.Index ? (index, date) : current.Value;

    private static bool TryDate(string year, string month, string day, out DateOnly date)
    {
        date = default;

        return int.TryParse(year, NumberStyles.None, Invariant, out int y)
            && int.TryParse(month, NumberStyles.None, Invariant, out int m)
            && int.TryParse(day, NumberStyles.None, Invariant, out int d)
            && y is >= 1900 and <= 2200
            && m is >= 1 and <= 12
            && d >= 1 && d <= DateTime.DaysInMonth(y, m)
            && (date = new DateOnly(y, m, d)) != default;
    }

    // Every pattern carries a timeout: they run on uploaded text, which is untrusted input.

    [GeneratedRegex(@"(?<![\d.,])(?:-?\d{1,3}(?:[ \u00A0\u202F.,]\d{3})+(?:[.,]\d{1,2})?|-?\d+(?:[.,]\d{1,2})?)(?![\d])", RegexOptions.CultureInvariant, 250)]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant, 250)]
    private static partial Regex IntegerPattern();

    [GeneratedRegex(@"(?<!\d)(?<d>\d{1,2})[/.\-](?<m>\d{1,2})[/.\-](?<y>\d{4})(?!\d)", RegexOptions.CultureInvariant, 250)]
    private static partial Regex NumericDatePattern();

    [GeneratedRegex(@"(?<!\d)(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})(?!\d)", RegexOptions.CultureInvariant, 250)]
    private static partial Regex IsoDatePattern();

    [GeneratedRegex(@"(?<!\d)(?<d>\d{1,2})(?:\.?º|\.?ª)?\s+de\s+(?<month>[a-z]+)\s+de\s+(?<y>\d{4})", RegexOptions.CultureInvariant, 250)]
    private static partial Regex WrittenDatePattern();

    [GeneratedRegex(@"(?<month>[a-z]+)\s+(?<d>\d{1,2}),?\s+(?<y>\d{4})", RegexOptions.CultureInvariant, 250)]
    private static partial Regex EnglishDatePattern();

    [GeneratedRegex(@"(?<!\d)(?<d>\d{1,2})\s+(?<month>[a-z]+),?\s+(?<y>\d{4})", RegexOptions.CultureInvariant, 250)]
    private static partial Regex DayMonthYearPattern();

    [GeneratedRegex(@"\b(EUR|euros?)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 250)]
    private static partial Regex EuroWord();

    [GeneratedRegex(@"\b(GBP|libras?|pounds?)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 250)]
    private static partial Regex PoundWord();

    [GeneratedRegex(@"\b(USD|d[oó]lares?|dollars?)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 250)]
    private static partial Regex DollarWord();

    [GeneratedRegex(@"\s{2,}|\t", RegexOptions.CultureInvariant, 250)]
    private static partial Regex ColumnGap();

    [GeneratedRegex(@",?\s+(?:NIF|NIPC|VAT|contribuinte|com sede|pessoa coletiva|registad[ao])\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 250)]
    private static partial Regex IdentificationClause();
}
