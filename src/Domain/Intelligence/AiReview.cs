using DocOps.Domain.Common;

namespace DocOps.Domain.Intelligence;

public enum ConfidenceBand
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>
/// The rules of reviewing AI output. "The AI suggests, the person decides" (ADR-004) is enforced
/// here rather than in the interface: a suggestion cannot be accepted with nothing in it, a run
/// cannot be applied while a required field would be left empty, and a finished run cannot be
/// changed.
/// </summary>
public static class AiReview
{
    public const decimal HighConfidence = 0.85m;
    public const decimal MediumConfidence = 0.60m;

    public static ConfidenceBand Band(decimal confidence) => confidence switch
    {
        >= HighConfidence => ConfidenceBand.High,
        >= MediumConfidence => ConfidenceBand.Medium,
        _ => ConfidenceBand.Low
    };

    public static void EnsureReviewable(AiProcessingRun run)
    {
        if (run.Status != AiRunStatus.AwaitingReview)
        {
            throw new DomainException(
                "Esta análise já não está em revisão.", "ai_run_not_reviewable");
        }
    }

    /// <summary>Records a decision about one field suggestion.</summary>
    public static void Decide(
        AiProcessingRun run,
        AiFieldSuggestion suggestion,
        AiSuggestionDecision decision,
        string? editedValue,
        Guid userId,
        DateTimeOffset now)
    {
        EnsureReviewable(run);

        switch (decision)
        {
            case AiSuggestionDecision.Accepted:
                if (!suggestion.WasFound)
                {
                    // Accepting "nothing" would silently write an empty value: the person has to
                    // type one, which is an edit.
                    throw new DomainException(
                        "Este campo não foi encontrado no documento; indique o valor em vez de aceitar.",
                        "ai_nothing_to_accept");
                }

                suggestion.FinalValue = suggestion.SuggestedValue;
                break;

            case AiSuggestionDecision.Edited:
                if (string.IsNullOrWhiteSpace(editedValue))
                {
                    throw new DomainException("Indique o valor corrigido.", "ai_edit_requires_value");
                }

                suggestion.FinalValue = editedValue.Trim();
                break;

            case AiSuggestionDecision.Rejected:
                suggestion.FinalValue = null;
                break;

            default:
                throw new DomainException("Decisão inválida.", "ai_invalid_decision");
        }

        suggestion.Decision = decision;
        suggestion.DecidedByUserId = userId;
        suggestion.DecidedAtUtc = now;
    }

    public static void DecideClassification(AiProcessingRun run, bool accept)
    {
        EnsureReviewable(run);

        if (run.SuggestedDocumentTypeId is null)
        {
            throw new DomainException(
                "Esta análise não sugeriu nenhuma classificação.", "ai_no_classification");
        }

        run.ClassificationDecision = accept ? AiSuggestionDecision.Accepted : AiSuggestionDecision.Rejected;
    }

    /// <summary>
    /// Values that will be written when the run is applied, for the type the document will have.
    /// Rejected and undecided suggestions contribute nothing.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ValuesToApply(AiProcessingRun run)
    {
        Guid targetType = run.EffectiveDocumentTypeId;

        return run.Fields
            .Where(f => f.DocumentTypeId == targetType
                && f.Decision is AiSuggestionDecision.Accepted or AiSuggestionDecision.Edited
                && !string.IsNullOrWhiteSpace(f.FinalValue))
            .ToDictionary(f => f.FieldCode, f => f.FinalValue!, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Required fields of the target type that would still be empty after applying. The document's
    /// existing values count only when the type does not change: a value that belonged to another
    /// type's schema says nothing about this one.
    /// </summary>
    public static IReadOnlyList<string> MissingRequiredFields(
        AiProcessingRun run,
        IEnumerable<string> requiredFieldCodes,
        IReadOnlySet<string> fieldsAlreadyFilledOnDocument)
    {
        IReadOnlyDictionary<string, string> values = ValuesToApply(run);
        bool typeChanges = run.EffectiveDocumentTypeId != run.OriginalDocumentTypeId;

        return
        [
            .. requiredFieldCodes.Where(code =>
                !values.ContainsKey(code)
                && (typeChanges || !fieldsAlreadyFilledOnDocument.Contains(code)))
        ];
    }

    /// <summary>
    /// A run with a suggested classification is applied only after that classification was
    /// explicitly accepted or rejected: changing a document's type is never a default.
    /// </summary>
    public static void EnsureCanApply(AiProcessingRun run)
    {
        EnsureReviewable(run);

        if (run.ClassificationUndecided)
        {
            throw new DomainException(
                "Aceite ou rejeite a classificação sugerida antes de aplicar.", "ai_classification_undecided");
        }
    }

    /// <summary>Accepts every suggestion that has a value and is still undecided.</summary>
    public static int AcceptAllFound(AiProcessingRun run, Guid userId, DateTimeOffset now)
    {
        EnsureReviewable(run);

        if (run.SuggestedDocumentTypeId is not null && run.ClassificationDecision == AiSuggestionDecision.Pending)
        {
            run.ClassificationDecision = AiSuggestionDecision.Accepted;
        }

        int accepted = 0;
        Guid targetType = run.EffectiveDocumentTypeId;

        foreach (AiFieldSuggestion suggestion in run.Fields.Where(f =>
                     f.DocumentTypeId == targetType && f.WasFound && f.Decision == AiSuggestionDecision.Pending))
        {
            Decide(run, suggestion, AiSuggestionDecision.Accepted, null, userId, now);
            accepted++;
        }

        return accepted;
    }
}
