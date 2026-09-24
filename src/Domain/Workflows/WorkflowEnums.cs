namespace DocOps.Domain.Workflows;

/// <summary>
/// The kinds of node a workflow definition can contain. Executors are registered per type; a
/// definition that uses a type without an executor fails loudly at execution time rather than
/// silently skipping the step.
/// </summary>
public enum WorkflowNodeType
{
    Start = 0,
    HumanTask = 1,
    Approval = 2,
    Condition = 3,
    Notification = 4,
    ParallelBranch = 5,
    Join = 6,
    Delay = 7,
    End = 8,

    /// <summary>Implemented in the AI phase; definitions may already be designed with it.</summary>
    AiClassification = 9,

    /// <summary>Implemented in the AI phase.</summary>
    AiExtraction = 10,

    /// <summary>Implemented in the integrations phase.</summary>
    Integration = 11,

    /// <summary>
    /// Asks a named person to sign the document the process is running on, and waits. Implemented in
    /// the signature phase; the brief's contract-review example ends on one of these.
    /// </summary>
    SignatureRequest = 12
}

public enum WorkflowTriggerType
{
    Manual = 0,
    OnDocumentCreated = 1,
    OnDocumentStatusChanged = 2
}

public enum WorkflowInstanceStatus
{
    Running = 0,

    /// <summary>Parked on a human task or a timer. Nothing moves until someone acts or the clock does.</summary>
    Waiting = 1,

    Completed = 2,
    Rejected = 3,
    Cancelled = 4,

    /// <summary>Execution hit an error — an unknown node type, a missing edge, a broken configuration.</summary>
    Failed = 5
}

public enum WorkflowTaskStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Rejected = 3,
    Cancelled = 4
}

public enum WorkflowTaskDecision
{
    Approved = 0,
    Rejected = 1,
    InformationRequested = 2,
    Completed = 3
}

public enum WorkflowTaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

/// <summary>How a task finds its assignee when the instance reaches the node.</summary>
public enum AssignmentStrategy
{
    /// <summary>A named user.</summary>
    User = 0,

    /// <summary>Anyone in a department may claim it.</summary>
    Department = 1,

    /// <summary>Anyone holding a role may claim it.</summary>
    Role = 2,

    /// <summary>The owner of the document the instance is running on.</summary>
    DocumentOwner = 3,

    /// <summary>The head of the document's department; falls back to the department itself.</summary>
    DepartmentManager = 4
}

/// <summary>Comparison used by a condition node. Deliberately a closed set rather than an expression language.</summary>
public enum ConditionOperator
{
    Equals = 0,
    NotEquals = 1,
    GreaterThan = 2,
    GreaterThanOrEqual = 3,
    LessThan = 4,
    LessThanOrEqual = 5,
    Contains = 6,
    IsEmpty = 7,
    IsNotEmpty = 8
}
