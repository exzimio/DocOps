using DocOps.Application.Documents;
using DocOps.Domain.Documents;
using DocOps.Domain.Search;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Persistence;

namespace DocOps.Infrastructure.Documents;

/// <summary>
/// The filters a document query can carry, in one place. The document list and the search screen ask
/// for the same things in different combinations; one implementation means a filter cannot mean one
/// thing on one screen and something else on the other.
/// </summary>
public static class DocumentFilters
{
    /// <summary>
    /// Narrows <paramref name="query"/> by everything in the request except the free text, which the
    /// caller applies itself: the document list matches it by substring, the search screen by the
    /// full-text index, and the two rank their results differently.
    /// </summary>
    public static IQueryable<Document> ApplyFacets(
        DocOpsDbContext dbContext, IQueryable<Document> query, DocumentSearchRequest request, Guid callerUserId)
    {
        if (request.TypeIds.Count > 0)
        {
            Guid[] typeIds = [.. request.TypeIds];
            query = query.Where(d => typeIds.Contains(d.DocumentTypeId));
        }

        if (request.Statuses.Count > 0)
        {
            DocumentStatus[] statuses = [.. request.Statuses];
            query = query.Where(d => statuses.Contains(d.Status));
        }

        if (request.DepartmentIds.Count > 0)
        {
            Guid[] departmentIds = [.. request.DepartmentIds];
            query = query.Where(d => d.DepartmentId != null && departmentIds.Contains(d.DepartmentId.Value));
        }

        if (request.Classifications.Count > 0)
        {
            SecurityClassification[] classifications = [.. request.Classifications];
            query = query.Where(d => classifications.Contains(d.Classification));
        }

        if (request.TagIds.Count > 0)
        {
            Guid[] tagIds = [.. request.TagIds];
            query = query.Where(d => d.Tags.Any(t => tagIds.Contains(t.TagId)));
        }

        if (request.OwnerUserId is { } ownerId)
        {
            query = query.Where(d => d.OwnerUserId == ownerId);
        }

        if (request.OnlyMine)
        {
            query = query.Where(d => d.OwnerUserId == callerUserId);
        }

        if (request.CreatedFromUtc is { } from)
        {
            query = query.Where(d => d.CreatedAtUtc >= from);
        }

        if (request.CreatedToUtc is { } to)
        {
            query = query.Where(d => d.CreatedAtUtc <= to);
        }

        if (!request.IncludeArchived)
        {
            query = query.Where(d => d.Status != DocumentStatus.Archived);
        }

        query = ApplyWorkflowStates(dbContext, query, request.WorkflowStates);

        return ApplyApprovalStates(dbContext, query, request.ApprovalStates);
    }

    /// <summary>Matches free text by substring: what the document list does, and what search falls back to.</summary>
    public static IQueryable<Document> ApplyTextContains(IQueryable<Document> query, string? rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return query;
        }

        string term = rawQuery.Trim();

        return query.Where(d =>
            d.Title.Contains(term)
            || d.Reference.Contains(term)
            || (d.Summary != null && d.Summary.Contains(term))
            || d.MetadataValues.Any(v => v.TextValue != null && v.TextValue.Contains(term)));
    }

    public static IQueryable<Document> ApplySort(IQueryable<Document> query, DocumentSearchRequest request) =>
        (request.SortBy, request.SortDescending) switch
        {
            (DocumentSortField.Title, true) => query.OrderByDescending(d => d.Title),
            (DocumentSortField.Title, false) => query.OrderBy(d => d.Title),
            (DocumentSortField.Reference, true) => query.OrderByDescending(d => d.Reference),
            (DocumentSortField.Reference, false) => query.OrderBy(d => d.Reference),
            (DocumentSortField.Status, true) => query.OrderByDescending(d => d.Status).ThenByDescending(d => d.CreatedAtUtc),
            (DocumentSortField.Status, false) => query.OrderBy(d => d.Status).ThenByDescending(d => d.CreatedAtUtc),
            (DocumentSortField.ModifiedAt, true) => query.OrderByDescending(d => d.ModifiedAtUtc ?? d.CreatedAtUtc),
            (DocumentSortField.ModifiedAt, false) => query.OrderBy(d => d.ModifiedAtUtc ?? d.CreatedAtUtc),
            (_, false) => query.OrderBy(d => d.CreatedAtUtc),
            _ => query.OrderByDescending(d => d.CreatedAtUtc)
        };

    /// <summary>
    /// A document is in a workflow state when the newest instance started on it is in that state —
    /// newest, because a document resubmitted after a rejection is in progress again, not rejected.
    /// </summary>
    private static IQueryable<Document> ApplyWorkflowStates(
        DocOpsDbContext dbContext, IQueryable<Document> query, IReadOnlyList<DocumentWorkflowState> states)
    {
        if (states.Count == 0)
        {
            return query;
        }

        bool none = states.Contains(DocumentWorkflowState.None);
        bool inProgress = states.Contains(DocumentWorkflowState.InProgress);

        WorkflowInstanceStatus[] finished =
        [
            .. states
                .Select(state => state switch
                {
                    DocumentWorkflowState.Completed => (WorkflowInstanceStatus?)WorkflowInstanceStatus.Completed,
                    DocumentWorkflowState.Rejected => WorkflowInstanceStatus.Rejected,
                    DocumentWorkflowState.Cancelled => WorkflowInstanceStatus.Cancelled,
                    DocumentWorkflowState.Failed => WorkflowInstanceStatus.Failed,
                    _ => null
                })
                .Where(status => status is not null)
                .Select(status => status!.Value)
        ];

        // The whole filter is one OR: asking for "rejected or cancelled" must return both, not the
        // documents that are somehow in both states at once.
        return query.Where(d =>
            (none && !dbContext.WorkflowInstances.Any(i => i.DocumentId == d.Id))
            || (inProgress && dbContext.WorkflowInstances
                .Where(i => i.DocumentId == d.Id)
                .OrderByDescending(i => i.StartedAtUtc)
                .Select(i => (WorkflowInstanceStatus?)i.Status)
                .FirstOrDefault() == WorkflowInstanceStatus.Running)
            || (inProgress && dbContext.WorkflowInstances
                .Where(i => i.DocumentId == d.Id)
                .OrderByDescending(i => i.StartedAtUtc)
                .Select(i => (WorkflowInstanceStatus?)i.Status)
                .FirstOrDefault() == WorkflowInstanceStatus.Waiting)
            || finished.Contains(
                dbContext.WorkflowInstances
                    .Where(i => i.DocumentId == d.Id)
                    .OrderByDescending(i => i.StartedAtUtc)
                    .Select(i => i.Status)
                    .FirstOrDefault()));
    }

    /// <summary>
    /// An approval is a workflow task that asks for a decision. The state is read from those tasks:
    /// anything still open wins, because a document waiting on an approver is not approved yet even
    /// when an earlier approver has already said yes.
    /// </summary>
    private static IQueryable<Document> ApplyApprovalStates(
        DocOpsDbContext dbContext, IQueryable<Document> query, IReadOnlyList<DocumentApprovalState> states)
    {
        if (states.Count == 0)
        {
            return query;
        }

        bool notRequested = states.Contains(DocumentApprovalState.NotRequested);
        bool awaiting = states.Contains(DocumentApprovalState.Awaiting);
        bool approved = states.Contains(DocumentApprovalState.Approved);
        bool rejected = states.Contains(DocumentApprovalState.Rejected);
        bool informationRequested = states.Contains(DocumentApprovalState.InformationRequested);

        return query.Where(d =>
            (notRequested && !dbContext.WorkflowTasks.Any(t =>
                t.RequiresDecision && t.Instance!.DocumentId == d.Id))
            || (awaiting && dbContext.WorkflowTasks.Any(t =>
                t.RequiresDecision
                && t.Instance!.DocumentId == d.Id
                && (t.Status == WorkflowTaskStatus.Pending || t.Status == WorkflowTaskStatus.InProgress)))
            || (informationRequested && dbContext.WorkflowTasks.Any(t =>
                t.RequiresDecision
                && t.Instance!.DocumentId == d.Id
                && t.Decision == WorkflowTaskDecision.InformationRequested))
            || (rejected
                && dbContext.WorkflowTasks.Any(t =>
                    t.RequiresDecision && t.Instance!.DocumentId == d.Id
                    && t.Decision == WorkflowTaskDecision.Rejected)
                && !dbContext.WorkflowTasks.Any(t =>
                    t.RequiresDecision && t.Instance!.DocumentId == d.Id
                    && (t.Status == WorkflowTaskStatus.Pending || t.Status == WorkflowTaskStatus.InProgress)))
            || (approved
                && dbContext.WorkflowTasks.Any(t =>
                    t.RequiresDecision && t.Instance!.DocumentId == d.Id
                    && t.Decision == WorkflowTaskDecision.Approved)
                && !dbContext.WorkflowTasks.Any(t =>
                    t.RequiresDecision && t.Instance!.DocumentId == d.Id
                    && (t.Status == WorkflowTaskStatus.Pending
                        || t.Status == WorkflowTaskStatus.InProgress
                        || t.Decision == WorkflowTaskDecision.Rejected))));
    }
}
