using System.Globalization;
using DocOps.Application.Intelligence;
using DocOps.Domain.Documents;
using DocOps.Domain.Intelligence;

namespace DocOps.Infrastructure.Intelligence;

/// <summary>
/// The demonstration provider (ADR-004). Deterministic and rule-based: it reads the document's real
/// text with the label rules in <see cref="DemoVocabulary"/> and with the labels of the tenant's own
/// catalogue, and every value it returns points at the line it was read from. It is not a language
/// model and never claims to be one — its <see cref="Provider"/> says so, and the interface shows it.
/// </summary>
public sealed class DemoAiProvider : IAiDocumentService
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-PT");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-GB");

    public AiProviderInfo Provider { get; } = new(
        AiSource.Demo,
        "Fornecedor de demonstração",
        "Regras determinísticas sobre o texto do documento. Não é um modelo de linguagem: o mesmo " +
        "documento produz sempre o mesmo resultado, e cada valor indica a linha de onde foi lido.");

    public Task<ClassificationResult> ClassifyAsync(
        DocumentText content, IReadOnlyList<DocumentTypeProfile> candidates, CancellationToken cancellationToken = default)
    {
        if (content.IsEmpty)
        {
            return Task.FromResult(new ClassificationResult([]));
        }

        List<(DocumentTypeProfile Type, int Score, List<TextReference> Evidence)> scored = [];

        foreach (DocumentTypeProfile type in candidates)
        {
            int score = 0;
            List<TextReference> evidence = [];

            if (DemoVocabulary.TypeSignals.TryGetValue(type.Code, out IReadOnlyList<(string Term, int Weight)>? signals))
            {
                foreach ((string term, int weight) in signals)
                {
                    TextLine? line = FindTerm(content, term);
                    if (line is null)
                    {
                        continue;
                    }

                    score += weight;
                    AddEvidence(evidence, content, line);
                }
            }

            // A type the demo has never heard of can still be recognised by its own field labels.
            foreach (FieldProfile field in type.Fields)
            {
                TextLine? line = FindTerm(content, field.Label);
                if (line is not null && StartsWithLabel(line.Text, field.Label))
                {
                    score += 1;
                    AddEvidence(evidence, content, line);
                }
            }

            if (score > 0)
            {
                scored.Add((type, score, evidence));
            }
        }

        if (scored.Count == 0)
        {
            return Task.FromResult(new ClassificationResult([]));
        }

        scored.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : string.CompareOrdinal(a.Type.Code, b.Type.Code));

        int best = scored[0].Score;
        int second = scored.Count > 1 ? scored[1].Score : 0;

        List<ClassificationCandidate> result =
        [
            .. scored.Select((entry, index) => new ClassificationCandidate(
                entry.Type.Code,
                ClassificationConfidence(entry.Score, index == 0 ? second : best),
                entry.Evidence))
        ];

        return Task.FromResult(new ClassificationResult(result));
    }

    public Task<ExtractionResult> ExtractAsync(
        DocumentText content, DocumentTypeProfile schema, CancellationToken cancellationToken = default)
    {
        Dictionary<string, ExtractedField> found = new(StringComparer.OrdinalIgnoreCase);

        foreach (FieldProfile field in schema.Fields)
        {
            found[field.Code] = ExtractField(content, field);
        }

        DeriveMissingAmounts(found);

        return Task.FromResult(new ExtractionResult([.. schema.Fields.Select(f => found[f.Code])]));
    }

    public async Task<DocumentAnswer> AnswerAsync(
        DocumentText content, DocumentTypeProfile? schema, string question, CancellationToken cancellationToken = default)
    {
        bool english = IsEnglish(question);
        string folded = TextValueParser.Fold(question);

        if (content.IsEmpty)
        {
            return NotFound(english);
        }

        // 1. The question names something the schema knows: read that field.
        if (schema is not null)
        {
            ExtractionResult extraction = await ExtractAsync(content, schema, cancellationToken);

            foreach ((string[] terms, string[] fieldCodes) in DemoVocabulary.QuestionIntents)
            {
                if (!terms.Any(term => ContainsWord(folded, term)))
                {
                    continue;
                }

                foreach (string code in fieldCodes)
                {
                    ExtractedField? field = extraction.Fields.FirstOrDefault(f =>
                        string.Equals(f.FieldCode, code, StringComparison.OrdinalIgnoreCase));

                    FieldProfile? profile = schema.Fields.FirstOrDefault(f =>
                        string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase));

                    if (field?.Value is null || field.Evidence is null || profile is null)
                    {
                        continue;
                    }

                    string currency = extraction.Fields
                        .FirstOrDefault(f => string.Equals(f.FieldCode, "currency", StringComparison.OrdinalIgnoreCase))?.Value
                        ?? TextValueParser.ParseCurrency(content.Raw)
                        ?? string.Empty;

                    string value = FormatValue(profile, field.Value, currency, english);

                    return new DocumentAnswer(true, $"{profile.Label}: {value}.", field.Confidence, [field.Evidence]);
                }
            }
        }

        // 2. Otherwise, the line that shares the most words with the question — quoted, not paraphrased.
        string[] words =
        [
            .. folded.Split([' ', '?', '!', ',', '.', ';', ':', '"', '\''], StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 4 && !DemoVocabulary.StopWords.Contains(w))
                .Distinct()
        ];

        if (words.Length > 0)
        {
            TextLine? bestLine = null;
            int bestHits = 0;

            foreach (TextLine line in content.Lines)
            {
                string foldedLine = TextValueParser.Fold(line.Text);
                int hits = words.Count(w => ContainsWord(foldedLine, w));

                if (hits > bestHits)
                {
                    bestHits = hits;
                    bestLine = line;
                }
            }

            int needed = words.Length >= 3 ? 2 : 1;

            if (bestLine is not null && bestHits >= needed)
            {
                TextReference reference = content.Reference(bestLine);
                string text = english
                    ? $"The document says: \"{reference.Excerpt}\""
                    : $"O documento refere: «{reference.Excerpt}»";

                return new DocumentAnswer(true, text, 0.5m, [reference]);
            }
        }

        return NotFound(english);
    }

    // -----------------------------------------------------------------------------------------
    // Extraction
    // -----------------------------------------------------------------------------------------

    private static ExtractedField ExtractField(DocumentText content, FieldProfile field)
    {
        DemoVocabulary.Fields.TryGetValue(field.Code, out FieldRule? rule);
        IReadOnlyList<string> exclusions = rule?.Exclusions ?? [];

        // The catalogue's own label comes first, but it is often a bare word ("Total", "IVA"), so it
        // carries the same exclusions as a generic label: otherwise "Total" would be read from the
        // "Total ilíquido" line that sits above "Total a pagar".
        List<(LabelRule Label, IReadOnlyList<string> Exclusions)> attempts =
        [
            (new LabelRule(field.Label, 0.95m), ExclusionsFor(field.Label, exclusions)),
            .. (rule?.Specific ?? []).Select(label => (label, (IReadOnlyList<string>)[])),
            .. (rule?.Generic ?? []).Select(label => (label, ExclusionsFor(label.Text, exclusions)))
        ];

        foreach ((LabelRule label, IReadOnlyList<string> skip) in attempts)
        {
            ExtractedField? match = ReadAfterLabel(content, field, label, skip);
            if (match is not null)
            {
                return match;
            }
        }

        return Fallback(content, field) ?? new ExtractedField(field.Code, null, 0m, null);
    }

    /// <summary>An exclusion that is part of the label itself cannot exclude it ("Total IVA" is not excluded by "IVA").</summary>
    private static IReadOnlyList<string> ExclusionsFor(string label, IReadOnlyList<string> exclusions)
    {
        string folded = TextValueParser.Fold(label);
        return [.. exclusions.Where(exclusion => !ContainsWord(folded, exclusion))];
    }

    private static ExtractedField? ReadAfterLabel(
        DocumentText content, FieldProfile field, LabelRule label, IReadOnlyList<string> exclusions)
    {
        string foldedLabel = TextValueParser.Fold(label.Text);

        foreach (TextLine line in content.Lines)
        {
            string foldedLine = TextValueParser.Fold(line.Text);

            if (exclusions.Any(exclusion => ContainsWord(foldedLine, exclusion)))
            {
                continue;
            }

            int at = FindLabel(foldedLine, foldedLabel);
            if (at < 0)
            {
                continue;
            }

            string remainder = SkipSeparator(line.Text[(at + foldedLabel.Length)..]);
            string? value = ParseValue(field, remainder);

            if (value is null)
            {
                continue;
            }

            return new ExtractedField(field.Code, value, label.Confidence, content.Reference(line));
        }

        return null;
    }

    /// <summary>Values found without a label: weaker evidence, so lower confidence.</summary>
    private static ExtractedField? Fallback(DocumentText content, FieldProfile field)
    {
        if (string.Equals(field.Code, "currency", StringComparison.OrdinalIgnoreCase))
        {
            foreach (TextLine line in content.Lines)
            {
                string? currency = TextValueParser.ParseCurrency(line.Text);
                if (currency is not null && MatchesOption(field, currency) is { } option)
                {
                    return new ExtractedField(field.Code, option, 0.8m, content.Reference(line));
                }
            }

            return null;
        }

        if (field.FieldType == MetadataFieldType.Select)
        {
            // A contract titled "Contrato de prestação de serviços" says which option applies.
            foreach (TextLine line in content.Lines.Take(15))
            {
                string foldedLine = TextValueParser.Fold(line.Text);
                string? option = field.Options.FirstOrDefault(o => ContainsWord(foldedLine, TextValueParser.Fold(o)));

                if (option is not null)
                {
                    return new ExtractedField(field.Code, option, 0.8m, content.Reference(line));
                }
            }

            return null;
        }

        if (string.Equals(field.Code, "supplier", StringComparison.OrdinalIgnoreCase))
        {
            // Letterhead: the first company name at the top of the page, outside the recipient block.
            foreach (TextLine line in content.Lines.Take(10))
            {
                string foldedLine = TextValueParser.Fold(line.Text);

                if (DemoVocabulary.RecipientMarkers.Any(m => foldedLine.Contains(m, StringComparison.Ordinal)))
                {
                    continue;
                }

                string[] tokens = foldedLine.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Any(t => DemoVocabulary.CompanySuffixes.Contains(t)))
                {
                    string? name = TextValueParser.CleanText(line.Text);
                    if (name is not null)
                    {
                        return new ExtractedField(field.Code, name, 0.75m, content.Reference(line));
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// When two of net, VAT and total were read, the third follows. Marked with modest confidence and
    /// pointing at the line of the total, which is what it was calculated from.
    /// </summary>
    private static void DeriveMissingAmounts(Dictionary<string, ExtractedField> found)
    {
        if (!found.TryGetValue("netAmount", out ExtractedField? net)
            || !found.TryGetValue("vatAmount", out ExtractedField? vat)
            || !found.TryGetValue("totalAmount", out ExtractedField? total))
        {
            return;
        }

        decimal? n = Parse(net.Value);
        decimal? v = Parse(vat.Value);
        decimal? t = Parse(total.Value);

        if (v is null && n is not null && t is not null && t >= n)
        {
            found["vatAmount"] = vat with { Value = Format(t.Value - n.Value), Confidence = 0.6m, Evidence = total.Evidence };
        }
        else if (n is null && v is not null && t is not null && t >= v)
        {
            found["netAmount"] = net with { Value = Format(t.Value - v.Value), Confidence = 0.6m, Evidence = total.Evidence };
        }

        static decimal? Parse(string? value) =>
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d) ? d : null;

        static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string? ParseValue(FieldProfile field, string remainder) => field.FieldType switch
    {
        MetadataFieldType.Date => TextValueParser.ParseDate(remainder),
        MetadataFieldType.Currency => TextValueParser.ParseAmount(remainder),
        MetadataFieldType.Number => TextValueParser.ParseInteger(remainder),
        MetadataFieldType.Boolean => null,
        MetadataFieldType.Select => MatchesOption(field, remainder),
        _ => TextValueParser.CleanText(remainder)
    };

    private static string? MatchesOption(FieldProfile field, string text)
    {
        if (field.Options.Count == 0)
        {
            return TextValueParser.CleanText(text);
        }

        string folded = TextValueParser.Fold(text);

        return field.Options.FirstOrDefault(option => ContainsWord(folded, TextValueParser.Fold(option)))
            ?? (string.Equals(field.Code, "currency", StringComparison.OrdinalIgnoreCase)
                && TextValueParser.ParseCurrency(text) is { } code
                && field.Options.Contains(code, StringComparer.OrdinalIgnoreCase)
                    ? code
                    : null);
    }

    // -----------------------------------------------------------------------------------------
    // Text helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Position of a label in a line, as a whole word. Very short labels ("IVA", "De") count only at
    /// the start of the line, where they are headings of a value rather than words of a sentence.
    /// </summary>
    private static int FindLabel(string foldedLine, string foldedLabel)
    {
        if (foldedLabel.Length <= 4)
        {
            string trimmed = foldedLine.TrimStart();
            int offset = foldedLine.Length - trimmed.Length;

            return trimmed.StartsWith(foldedLabel, StringComparison.Ordinal) && IsBoundary(trimmed, foldedLabel.Length)
                ? offset
                : -1;
        }

        int index = 0;

        while ((index = foldedLine.IndexOf(foldedLabel, index, StringComparison.Ordinal)) >= 0)
        {
            bool startOk = index == 0 || !char.IsLetterOrDigit(foldedLine[index - 1]);

            if (startOk && IsBoundary(foldedLine, index + foldedLabel.Length))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    private static bool IsBoundary(string text, int end) =>
        end >= text.Length || !char.IsLetter(text[end]);

    private static bool StartsWithLabel(string line, string label)
    {
        string folded = TextValueParser.Fold(line).TrimStart();
        string foldedLabel = TextValueParser.Fold(label);

        return folded.StartsWith(foldedLabel, StringComparison.Ordinal) && IsBoundary(folded, foldedLabel.Length);
    }

    private static string SkipSeparator(string text)
    {
        int index = 0;

        while (index < text.Length && (char.IsWhiteSpace(text[index]) || text[index] is ':' or '-' or '–' or '—' or '.' or 'º' or 'ª' or '°'))
        {
            index++;
        }

        return text[index..];
    }

    private static bool ContainsWord(string foldedText, string term)
    {
        string foldedTerm = TextValueParser.Fold(term);
        int index = 0;

        while ((index = foldedText.IndexOf(foldedTerm, index, StringComparison.Ordinal)) >= 0)
        {
            bool startOk = index == 0 || !char.IsLetterOrDigit(foldedText[index - 1]);
            int end = index + foldedTerm.Length;
            bool endOk = end >= foldedText.Length || !char.IsLetterOrDigit(foldedText[end]);

            if (startOk && endOk)
            {
                return true;
            }

            index++;
        }

        return false;
    }

    private static TextLine? FindTerm(DocumentText content, string term) =>
        content.Lines.FirstOrDefault(line => ContainsWord(TextValueParser.Fold(line.Text), term));

    private static void AddEvidence(List<TextReference> evidence, DocumentText content, TextLine line)
    {
        if (evidence.Count < 3 && evidence.All(e => e.Line != line.Number))
        {
            evidence.Add(content.Reference(line));
        }
    }

    /// <summary>
    /// More matching signals raise confidence, with diminishing returns; a close runner-up lowers it,
    /// because an ambiguous document should be looked at by a person.
    /// </summary>
    private static decimal ClassificationConfidence(int score, int competitor)
    {
        double strength = 1 - Math.Exp(-score / 4.0);
        double ratio = score == 0 ? 1 : Math.Min(1.0, competitor / (double)score);

        // Squared, so a distant runner-up barely matters and a close one matters a lot.
        double confidence = Math.Min(0.97, strength * (1 - (0.5 * ratio * ratio)));

        return Math.Round((decimal)confidence, 2);
    }

    private static bool IsEnglish(string question)
    {
        string folded = TextValueParser.Fold(question);
        return DemoVocabulary.EnglishMarkers.Any(marker => ContainsWord(folded, marker));
    }

    private static string FormatValue(FieldProfile field, string value, string currency, bool english)
    {
        CultureInfo culture = english ? English : Portuguese;

        switch (field.FieldType)
        {
            case MetadataFieldType.Date when DateOnly.TryParse(value, CultureInfo.InvariantCulture, out DateOnly date):
                return date.ToString(english ? "d MMMM yyyy" : "d 'de' MMMM 'de' yyyy", culture);

            case MetadataFieldType.Currency when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount):
                return $"{amount.ToString("N2", culture)} {currency}".TrimEnd();

            default:
                return value;
        }
    }

    private static DocumentAnswer NotFound(bool english) =>
        new(false,
            english
                ? "I could not find this information in the document."
                : "Não encontrei esta informação no documento.",
            0m,
            []);
}
