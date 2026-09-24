using DocOps.Application.Portal;
using DocOps.Domain.Documents;
using DocOps.Domain.Workflows;

namespace DocOps.UnitTests.Portal;

/// <summary>
/// What a supplier is told about its own submission.
///
/// The map exists because the internal vocabulary is not the supplier's business: an outsider has no
/// use for "waiting on a human task at node approve-2", and telling it would leak how the
/// organisation works. These tests pin the two judgement calls that are easy to get wrong when
/// someone reads the enum later and decides to be more precise.
/// </summary>
public class PortalProgressMapTests
{
    [Theory]
    [InlineData(DocumentStatus.Draft, PortalProgress.Received)]
    [InlineData(DocumentStatus.InReview, PortalProgress.Received)]
    [InlineData(DocumentStatus.Approved, PortalProgress.Accepted)]
    [InlineData(DocumentStatus.Signed, PortalProgress.Accepted)]
    [InlineData(DocumentStatus.Archived, PortalProgress.Accepted)]
    [InlineData(DocumentStatus.Rejected, PortalProgress.Declined)]
    public void Without_a_process_the_document_status_is_all_there_is_to_go_on(
        DocumentStatus document, PortalProgress expected) =>
        Assert.Equal(expected, PortalProgressMap.From(null, document));

    [Theory]
    [InlineData(WorkflowInstanceStatus.Running)]
    [InlineData(WorkflowInstanceStatus.Waiting)]
    public void A_process_under_way_reads_as_in_progress(WorkflowInstanceStatus status) =>
        Assert.Equal(PortalProgress.InProgress, PortalProgressMap.From(status, DocumentStatus.InReview));

    /// <summary>
    /// A process that broke on an internal fault is the organisation's problem to fix, not a
    /// decision against the supplier — and the supplier can do nothing with the distinction. Saying
    /// "failed" would invite a phone call about something nobody outside can act on.
    /// </summary>
    [Fact]
    public void An_internal_failure_is_not_reported_as_a_decision()
    {
        Assert.Equal(
            PortalProgress.InProgress,
            PortalProgressMap.From(WorkflowInstanceStatus.Failed, DocumentStatus.InReview));
    }

    [Theory]
    [InlineData(WorkflowInstanceStatus.Rejected)]
    [InlineData(WorkflowInstanceStatus.Cancelled)]
    public void A_process_that_ended_against_the_document_reads_as_declined(WorkflowInstanceStatus status) =>
        Assert.Equal(PortalProgress.Declined, PortalProgressMap.From(status, DocumentStatus.InReview));

    /// <summary>
    /// A completed process usually means acceptance, but not when the document itself was rejected:
    /// the process ran to its end along the rejection branch, which is completion of the process and
    /// a refusal of the document. The document has the final word.
    /// </summary>
    [Fact]
    public void A_completed_process_over_a_rejected_document_still_reads_as_declined()
    {
        Assert.Equal(
            PortalProgress.Declined,
            PortalProgressMap.From(WorkflowInstanceStatus.Completed, DocumentStatus.Rejected));

        Assert.Equal(
            PortalProgress.Accepted,
            PortalProgressMap.From(WorkflowInstanceStatus.Completed, DocumentStatus.Approved));
    }

    [Fact]
    public void Every_process_status_maps_to_something_a_supplier_can_read()
    {
        foreach (WorkflowInstanceStatus status in Enum.GetValues<WorkflowInstanceStatus>())
        {
            PortalProgress progress = PortalProgressMap.From(status, DocumentStatus.InReview);

            Assert.True(
                Enum.IsDefined(progress),
                $"O estado de processo {status} não tem tradução para o portal.");
        }
    }
}
