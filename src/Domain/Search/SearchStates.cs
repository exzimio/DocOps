namespace DocOps.Domain.Search;

/// <summary>
/// Where a document stands in a process, as a search filter. It is derived from the workflow
/// instances that ran on the document rather than stored on it: the instances are the record, and a
/// copy on the document would be one more thing able to disagree with them.
/// </summary>
public enum DocumentWorkflowState
{
    /// <summary>No workflow has ever run on the document.</summary>
    None = 0,

    /// <summary>A workflow is running or parked on a task.</summary>
    InProgress = 1,

    /// <summary>Every workflow that ran on it finished, and the last one finished well.</summary>
    Completed = 2,

    /// <summary>The last workflow ended in rejection.</summary>
    Rejected = 3,

    /// <summary>The last workflow was cancelled.</summary>
    Cancelled = 4,

    /// <summary>The last workflow stopped on an error and needs an administrator.</summary>
    Failed = 5
}

/// <summary>
/// Where a document stands in its approvals, as a search filter. An approval is a workflow task that
/// asks for a decision; a document with no such task was never put up for approval.
/// </summary>
public enum DocumentApprovalState
{
    /// <summary>Never sent for approval.</summary>
    NotRequested = 0,

    /// <summary>An approval task is open and waiting on someone.</summary>
    Awaiting = 1,

    /// <summary>Approved, with nothing still waiting.</summary>
    Approved = 2,

    /// <summary>Rejected at least once and not approved since.</summary>
    Rejected = 3,

    /// <summary>An approver asked for more information before deciding.</summary>
    InformationRequested = 4
}

/// <summary>Which part of a document a search term was found in.</summary>
public enum SearchMatchField
{
    Title = 0,
    Reference = 1,
    Summary = 2,

    /// <summary>The extracted text of the current version.</summary>
    Content = 3,

    /// <summary>A metadata value — a supplier name, an invoice number.</summary>
    Metadata = 4,

    /// <summary>A tag on the document.</summary>
    Tag = 5
}
