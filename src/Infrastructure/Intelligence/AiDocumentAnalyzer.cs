using System.Text.Json;
using DocOps.Application.Intelligence;
using DocOps.Domain.Documents;
using DocOps.Domain.Intelligence;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocOps.Infrastructure.Intelligence;

/// <summary>
/// Runs the provider over one version of a document and records what it suggested on a run.
/// It authorises nothing and audits nothing — its callers do both — so the same analysis serves a
/// person pressing a button and a workflow step processed by the worker.
///
/// It is also where provider output is checked before it is kept: a citation that does not match
/// the document's text is dropped here, whichever provider produced it.
/// </summary>
public sealed class AiDocumentAnalyzer(
    DocOpsDbContext dbContext,
    IAiDocumentService provider,
    ILogger<AiDocumentAnalyzer> logger)
{
    public AiProviderInfo Provider => provider.Provider;

    /// <summary>
    /// Profiles of every active type of the tenant, in the shape a provider understands. Filtered by
    /// tenant explicitly rather than by the ambient query filter: the worker runs without a tenant in
    /// context, and there the filter lets every tenant through — a Vértice document type must never be
    /// a candidate for an Asteria document.
    /// </summary>
    public async Task<List<(DocumentType Type, DocumentTypeProfile Profile)>> LoadTypesAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        List<DocumentType> types = await dbContext.DocumentTypes
            .Include(t => t.MetadataFields)
            .Where(t => t.TenantId == tenantId && t.IsActive)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

        return [.. types.Select(t => (t, ToProfile(t)))];
    }

    /// <summary>
    /// Fills a run with the provider's suggestions. The run ends awaiting review, or failed with a
    /// reason a person can read — never half-filled.
    /// </summary>
    public async Task AnalyzeAsync(
        AiProcessingRun run, DocumentVersion version, DateTimeOffset now, CancellationToken cancellationToken)
    {
        run.Source = provider.Provider.Source;
        run.ProviderName = provider.Provider.Name;

        DocumentText text = new(version.ExtractedText);

        if (text.IsEmpty)
        {
            // Binary formats are stored without extracted text, and inventing content for them
            // would be worse than saying so.
            Fail(run, now,
                $"A versão {version.VersionNumber} ('{version.FileName}') não tem texto extraível. " +
                "Documentos digitalizados precisariam de OCR, que não faz parte desta demonstração.");
            return;
        }

        List<(DocumentType Type, DocumentTypeProfile Profile)> types = await LoadTypesAsync(run.TenantId, cancellationToken);
        (DocumentType Type, DocumentTypeProfile Profile)? current = types.FirstOrDefault(t => t.Type.Id == run.OriginalDocumentTypeId);

        try
        {
            List<DocumentTypeProfile> candidates =
            [
                .. types.Where(t => t.Type.Code != DocumentTypeCodes.Unclassified).Select(t => t.Profile)
            ];

            ClassificationResult classification = await provider.ClassifyAsync(text, candidates, cancellationToken);
            ClassificationCandidate? best = classification.Candidates.Count > 0 ? classification.Candidates[0] : null;

            (DocumentType Type, DocumentTypeProfile Profile)? suggested = best is null
                ? null
                : types.FirstOrDefault(t => string.Equals(t.Type.Code, best.TypeCode, StringComparison.OrdinalIgnoreCase));

            // A suggestion is only a suggestion when it differs from what the document already is.
            if (suggested is { } s && s.Type.Id != run.OriginalDocumentTypeId)
            {
                run.SuggestedDocumentTypeId = s.Type.Id;
                run.ClassificationConfidence = best!.Confidence;
                run.ClassificationEvidence = Serialize(best.Evidence.FirstOrDefault(text.Verifies));
            }

            // Fields are suggested for the type the document has and for the one it may become, so
            // a reviewer who rejects the classification still has something to review.
            List<(DocumentType Type, DocumentTypeProfile Profile)> schemas = [];

            if (current is { } c && c.Type.Code != DocumentTypeCodes.Unclassified)
            {
                schemas.Add(c);
            }

            if (run.SuggestedDocumentTypeId is not null)
            {
                schemas.Add(suggested!.Value);
            }

            foreach ((DocumentType type, DocumentTypeProfile profile) in schemas)
            {
                ExtractionResult extraction = await provider.ExtractAsync(text, profile, cancellationToken);

                foreach (ExtractedField field in extraction.Fields)
                {
                    if (!profile.Fields.Any(f => string.Equals(f.Code, field.FieldCode, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    // The citation guard: evidence that is not really in the text is not kept.
                    TextReference? evidence = field.Evidence is not null && text.Verifies(field.Evidence)
                        ? field.Evidence
                        : null;

                    if (field.Evidence is not null && evidence is null)
                    {
                        logger.LogWarning(
                            "Provider {Provider} cited line {Line} for {Field}, which does not contain the quoted text; citation withheld",
                            run.ProviderName, field.Evidence.Line, field.FieldCode);
                    }

                    run.Fields.Add(new AiFieldSuggestion
                    {
                        TenantId = run.TenantId,
                        RunId = run.Id,
                        DocumentTypeId = type.Id,
                        FieldCode = profile.Fields.First(f =>
                            string.Equals(f.Code, field.FieldCode, StringComparison.OrdinalIgnoreCase)).Code,
                        SuggestedValue = Truncate(field.Value, 1000),
                        Confidence = Math.Clamp(field.Confidence, 0m, 1m),
                        EvidenceLine = evidence?.Line,
                        EvidencePage = evidence?.Page,
                        EvidenceText = Truncate(evidence?.Excerpt, 500)
                    });
                }
            }

            run.Status = AiRunStatus.AwaitingReview;
            run.CompletedAtUtc = now;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A provider failure is recorded on the run rather than surfacing as a 500: the
            // document is untouched and the person can try again.
            logger.LogError(exception, "AI provider {Provider} failed on document version {VersionId}", run.ProviderName, version.Id);

            // Whatever was gathered before the failure is dropped: a run is complete or it is empty.
            run.Fields.Clear();
            run.SuggestedDocumentTypeId = null;
            run.ClassificationConfidence = null;
            run.ClassificationEvidence = null;

            Fail(run, now, "O fornecedor de IA não conseguiu processar o documento. Tente novamente mais tarde.");
        }
    }

    public static DocumentTypeProfile ToProfile(DocumentType type) => new(
        type.Id,
        type.Code,
        type.Name,
        [
            .. type.MetadataFields
                .OrderBy(f => f.DisplayOrder)
                .Select(f => new FieldProfile(f.Code, f.Label, f.FieldType, f.IsRequired, ParseOptions(f.OptionsJson)))
        ]);

    public static IReadOnlyList<string> ParseOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(optionsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>A stored evidence reference, as "line|page|section|excerpt" JSON.</summary>
    public static string? Serialize(TextReference? reference) =>
        reference is null ? null : Truncate(JsonSerializer.Serialize(reference), 500);

    public static TextReference? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TextReference>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Fail(AiProcessingRun run, DateTimeOffset now, string reason)
    {
        run.Status = AiRunStatus.Failed;
        run.FailureReason = reason;
        run.CompletedAtUtc = now;
    }

    private static string? Truncate(string? value, int length) =>
        value is null || value.Length <= length ? value : value[..length];
}
