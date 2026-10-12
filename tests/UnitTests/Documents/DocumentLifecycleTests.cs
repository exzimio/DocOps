using DocOps.Application.Documents;
using DocOps.Domain.Common;
using DocOps.Domain.Documents;

namespace DocOps.UnitTests.Documents;

public class DocumentLifecycleTests
{
    [Fact]
    public void Signed_is_never_offered_by_hand()
    {
        Assert.DoesNotContain(DocumentStatus.Signed, DocumentLifecycle.ManualNextStatuses(DocumentStatus.SignatureRequired, processRunning: false));
        Assert.Contains(DocumentStatus.Rejected, DocumentLifecycle.ManualNextStatuses(DocumentStatus.SignatureRequired, processRunning: false));
    }

    [Fact]
    public void A_running_process_keeps_the_approval_decision_to_itself()
    {
        Assert.Equal(
            [DocumentStatus.InReview],
            DocumentLifecycle.ManualNextStatuses(DocumentStatus.PendingApproval, processRunning: true));

        Assert.Equal(
            [DocumentStatus.Approved, DocumentStatus.Rejected, DocumentStatus.InReview],
            DocumentLifecycle.ManualNextStatuses(DocumentStatus.PendingApproval, processRunning: false));
    }

    [Theory]
    [InlineData(DocumentStatus.Draft, DocumentStatus.InReview)]
    [InlineData(DocumentStatus.Draft, DocumentStatus.PendingApproval)]
    [InlineData(DocumentStatus.InReview, DocumentStatus.PendingApproval)]
    [InlineData(DocumentStatus.InReview, DocumentStatus.Draft)]
    [InlineData(DocumentStatus.PendingApproval, DocumentStatus.Approved)]
    [InlineData(DocumentStatus.PendingApproval, DocumentStatus.Rejected)]
    [InlineData(DocumentStatus.Approved, DocumentStatus.SignatureRequired)]
    [InlineData(DocumentStatus.SignatureRequired, DocumentStatus.Signed)]
    [InlineData(DocumentStatus.Signed, DocumentStatus.Archived)]
    [InlineData(DocumentStatus.Rejected, DocumentStatus.Draft)]
    public void Allows_the_transitions_the_business_process_needs(DocumentStatus from, DocumentStatus to)
    {
        Assert.True(DocumentLifecycle.CanTransition(from, to));
    }

    [Theory]
    [InlineData(DocumentStatus.Draft, DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Draft, DocumentStatus.Signed)]
    [InlineData(DocumentStatus.Rejected, DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Approved, DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Archived, DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Signed, DocumentStatus.Draft)]
    public void Refuses_transitions_that_would_skip_or_undo_a_decision(DocumentStatus from, DocumentStatus to)
    {
        Assert.False(DocumentLifecycle.CanTransition(from, to));
        Assert.Throws<DomainException>(() => DocumentLifecycle.EnsureCanTransition(from, to));
    }

    [Fact]
    public void A_status_never_transitions_to_itself()
    {
        foreach (DocumentStatus status in Enum.GetValues<DocumentStatus>())
        {
            Assert.False(DocumentLifecycle.CanTransition(status, status));
        }
    }

    [Fact]
    public void Archive_is_terminal_except_through_restore()
    {
        Assert.Empty(DocumentLifecycle.NextStatuses(DocumentStatus.Archived));
    }

    [Theory]
    [InlineData(DocumentStatus.Draft, true)]
    [InlineData(DocumentStatus.InReview, true)]
    [InlineData(DocumentStatus.Rejected, true)]
    [InlineData(DocumentStatus.PendingApproval, false)]
    [InlineData(DocumentStatus.Approved, false)]
    [InlineData(DocumentStatus.Archived, false)]
    public void Content_changes_are_allowed_only_before_a_decision_is_pending(DocumentStatus status, bool allowed)
    {
        Assert.Equal(allowed, DocumentLifecycle.AllowsContentChange(status));

        if (allowed)
        {
            DocumentLifecycle.EnsureCanChangeContent(status);
        }
        else
        {
            DomainException exception = Assert.Throws<DomainException>(() => DocumentLifecycle.EnsureCanChangeContent(status));
            Assert.Equal("content_locked", exception.Code);
        }
    }

    [Theory]
    [InlineData(DocumentStatus.Draft, true)]
    [InlineData(DocumentStatus.PendingApproval, true)]
    [InlineData(DocumentStatus.Approved, true)]
    [InlineData(DocumentStatus.Signed, false)]
    [InlineData(DocumentStatus.Archived, false)]
    public void Metadata_stays_correctable_until_the_document_is_signed_or_archived(DocumentStatus status, bool allowed)
    {
        Assert.Equal(allowed, DocumentLifecycle.AllowsMetadataChange(status));
    }

    [Theory]
    [InlineData(DocumentStatus.Approved, DocumentStatus.Approved)]
    [InlineData(DocumentStatus.InReview, DocumentStatus.InReview)]
    [InlineData(null, DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Archived, DocumentStatus.Draft)]
    public void Restore_returns_the_document_to_where_it_was(DocumentStatus? preArchive, DocumentStatus expected)
    {
        Assert.Equal(expected, DocumentLifecycle.RestoreTarget(preArchive));
    }
}

public class AccessLevelNamesTests
{
    [Fact]
    public void Expands_a_flags_value_into_its_names()
    {
        IReadOnlyList<string> names = AccessLevelNames.ToNames(DocumentAccessLevel.Reader);

        Assert.Equal(["view", "download"], names);
    }

    [Fact]
    public void Round_trips_a_combination_of_levels()
    {
        DocumentAccessLevel level = DocumentAccessLevel.View | DocumentAccessLevel.Approve | DocumentAccessLevel.Share;

        Assert.Equal(level, AccessLevelNames.FromNames(AccessLevelNames.ToNames(level)));
    }

    [Fact]
    public void Ignores_unknown_names_instead_of_failing_open()
    {
        Assert.Equal(
            DocumentAccessLevel.View,
            AccessLevelNames.FromNames(["view", "superuser", "root"]));
    }

    [Fact]
    public void Recognises_only_the_documented_level_names()
    {
        Assert.True(AccessLevelNames.IsKnown("approve"));
        Assert.True(AccessLevelNames.IsKnown("APPROVE"));
        Assert.False(AccessLevelNames.IsKnown("everything"));
    }

    [Fact]
    public void None_expands_to_an_empty_list()
    {
        Assert.Empty(AccessLevelNames.ToNames(DocumentAccessLevel.None));
    }
}
