using DocOps.Domain.Common;
using DocOps.Domain.Intelligence;

namespace DocOps.UnitTests.Intelligence;

/// <summary>"The AI suggests, the person decides" (ADR-004), as rules rather than as interface behaviour.</summary>
public class AiReviewTests
{
    private static readonly Guid Unclassified = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Invoice = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Contract = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid Reviewer = Guid.Parse("00000000-0000-0000-0000-0000000000ff");
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 10, 0, 0, TimeSpan.Zero);

    private static AiProcessingRun Run(params AiFieldSuggestion[] fields)
    {
        AiProcessingRun run = new()
        {
            Status = AiRunStatus.AwaitingReview,
            OriginalDocumentTypeId = Unclassified,
            SuggestedDocumentTypeId = Invoice,
            ClassificationConfidence = 0.9m
        };

        foreach (AiFieldSuggestion field in fields)
        {
            run.Fields.Add(field);
        }

        return run;
    }

    private static AiFieldSuggestion Suggestion(string code, string? value, Guid? type = null) => new()
    {
        FieldCode = code,
        SuggestedValue = value,
        Confidence = value is null ? 0 : 0.9m,
        DocumentTypeId = type ?? Invoice
    };

    [Theory]
    [InlineData(0.97, ConfidenceBand.High)]
    [InlineData(0.85, ConfidenceBand.High)]
    [InlineData(0.84, ConfidenceBand.Medium)]
    [InlineData(0.60, ConfidenceBand.Medium)]
    [InlineData(0.59, ConfidenceBand.Low)]
    public void Bands_confidence(decimal confidence, ConfidenceBand expected) =>
        Assert.Equal(expected, AiReview.Band(confidence));

    [Fact]
    public void Accepting_keeps_the_suggested_value_and_records_who_and_when()
    {
        AiFieldSuggestion total = Suggestion("totalAmount", "1906.50");
        AiProcessingRun run = Run(total);

        AiReview.Decide(run, total, AiSuggestionDecision.Accepted, null, Reviewer, Now);

        Assert.Equal("1906.50", total.FinalValue);
        Assert.Equal(Reviewer, total.DecidedByUserId);
        Assert.Equal(Now, total.DecidedAtUtc);
    }

    [Fact]
    public void A_field_that_was_not_found_cannot_be_accepted_only_filled_in()
    {
        AiFieldSuggestion due = Suggestion("dueDate", null);
        AiProcessingRun run = Run(due);

        DomainException error = Assert.Throws<DomainException>(() =>
            AiReview.Decide(run, due, AiSuggestionDecision.Accepted, null, Reviewer, Now));

        Assert.Equal("ai_nothing_to_accept", error.Code);

        AiReview.Decide(run, due, AiSuggestionDecision.Edited, "2026-10-03", Reviewer, Now);
        Assert.Equal("2026-10-03", due.FinalValue);
    }

    [Fact]
    public void An_edit_needs_a_value()
    {
        AiFieldSuggestion total = Suggestion("totalAmount", "1906.50");

        DomainException error = Assert.Throws<DomainException>(() =>
            AiReview.Decide(Run(total), total, AiSuggestionDecision.Edited, "  ", Reviewer, Now));

        Assert.Equal("ai_edit_requires_value", error.Code);
    }

    [Fact]
    public void Rejecting_leaves_nothing_to_write()
    {
        AiFieldSuggestion total = Suggestion("totalAmount", "1906.50");
        AiProcessingRun run = Run(total);

        AiReview.Decide(run, total, AiSuggestionDecision.Rejected, null, Reviewer, Now);

        Assert.Null(total.FinalValue);
        Assert.Empty(AiReview.ValuesToApply(run));
    }

    [Fact]
    public void A_finished_run_cannot_be_changed()
    {
        AiFieldSuggestion total = Suggestion("totalAmount", "1906.50");
        AiProcessingRun run = Run(total);
        run.Status = AiRunStatus.Applied;

        DomainException error = Assert.Throws<DomainException>(() =>
            AiReview.Decide(run, total, AiSuggestionDecision.Rejected, null, Reviewer, Now));

        Assert.Equal("ai_run_not_reviewable", error.Code);
    }

    [Fact]
    public void Only_values_for_the_type_the_document_will_have_are_applied()
    {
        AiFieldSuggestion invoiceTotal = Suggestion("totalAmount", "1906.50", Invoice);
        AiFieldSuggestion contractValue = Suggestion("contractValue", "84000.00", Contract);
        AiProcessingRun run = Run(invoiceTotal, contractValue);

        AiReview.DecideClassification(run, accept: true);
        AiReview.Decide(run, invoiceTotal, AiSuggestionDecision.Accepted, null, Reviewer, Now);
        AiReview.Decide(run, contractValue, AiSuggestionDecision.Accepted, null, Reviewer, Now);

        Assert.Equal(Invoice, run.EffectiveDocumentTypeId);
        Assert.Equal(["totalAmount"], AiReview.ValuesToApply(run).Keys);
    }

    [Fact]
    public void While_the_classification_is_undecided_the_review_is_about_the_suggested_type_but_cannot_be_applied()
    {
        AiProcessingRun run = Run();

        // The reviewer sees the invoice fields — that is what they are deciding about…
        Assert.Equal(Invoice, run.EffectiveDocumentTypeId);

        // …but changing a document's type is never a default.
        DomainException error = Assert.Throws<DomainException>(() => AiReview.EnsureCanApply(run));
        Assert.Equal("ai_classification_undecided", error.Code);

        AiReview.DecideClassification(run, accept: true);
        AiReview.EnsureCanApply(run);
    }

    [Fact]
    public void Rejecting_the_classification_keeps_the_type_the_document_already_has()
    {
        AiProcessingRun run = Run();

        AiReview.DecideClassification(run, accept: false);

        Assert.Equal(Unclassified, run.EffectiveDocumentTypeId);
    }

    [Fact]
    public void Required_fields_left_empty_block_applying()
    {
        AiFieldSuggestion total = Suggestion("totalAmount", "1906.50");
        AiFieldSuggestion supplier = Suggestion("supplier", null);
        AiProcessingRun run = Run(total, supplier);

        AiReview.DecideClassification(run, accept: true);
        AiReview.Decide(run, total, AiSuggestionDecision.Accepted, null, Reviewer, Now);

        IReadOnlyList<string> missing = AiReview.MissingRequiredFields(
            run, ["supplier", "totalAmount"], new HashSet<string>());

        Assert.Equal(["supplier"], missing);
    }

    [Fact]
    public void A_value_already_on_the_document_counts_only_when_the_type_does_not_change()
    {
        AiProcessingRun keepsType = Run();
        keepsType.SuggestedDocumentTypeId = null;
        keepsType.OriginalDocumentTypeId = Invoice;

        AiProcessingRun changesType = Run();
        AiReview.DecideClassification(changesType, accept: true);

        HashSet<string> filled = ["supplier"];

        Assert.Empty(AiReview.MissingRequiredFields(keepsType, ["supplier"], filled));

        // The supplier belonged to the old type's schema; it says nothing about the new one.
        Assert.Equal(["supplier"], AiReview.MissingRequiredFields(changesType, ["supplier"], filled));
    }

    [Fact]
    public void Accept_all_accepts_the_classification_and_every_value_found_but_not_the_missing_ones()
    {
        AiFieldSuggestion total = Suggestion("totalAmount", "1906.50");
        AiFieldSuggestion number = Suggestion("invoiceNumber", "FT 2026/0815");
        AiFieldSuggestion due = Suggestion("dueDate", null);
        AiFieldSuggestion rejected = Suggestion("supplier", "Atlas");
        AiProcessingRun run = Run(total, number, due, rejected);

        AiReview.Decide(run, rejected, AiSuggestionDecision.Rejected, null, Reviewer, Now);

        int accepted = AiReview.AcceptAllFound(run, Reviewer, Now);

        Assert.Equal(2, accepted);
        Assert.Equal(AiSuggestionDecision.Accepted, run.ClassificationDecision);
        Assert.Equal(AiSuggestionDecision.Pending, due.Decision);

        // A decision already taken is not overturned by "accept all".
        Assert.Equal(AiSuggestionDecision.Rejected, rejected.Decision);
    }
}
