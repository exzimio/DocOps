namespace DocOps.Domain.Documents;

/// <summary>Lifecycle state of a document. Transitions are validated by <see cref="DocumentLifecycle"/>.</summary>
public enum DocumentStatus
{
    Draft = 0,
    InReview = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    SignatureRequired = 5,
    Signed = 6,
    Archived = 7
}

/// <summary>Sensitivity label. Drives the default access rules when no explicit permission exists.</summary>
public enum SecurityClassification
{
    Public = 0,
    Internal = 1,
    Confidential = 2,
    Restricted = 3
}

/// <summary>What a subject may do with a document. Combinable, so one grant can carry several rights.</summary>
[Flags]
public enum DocumentAccessLevel
{
    None = 0,
    View = 1 << 0,
    Download = 1 << 1,
    Edit = 1 << 2,
    Delete = 1 << 3,
    Share = 1 << 4,
    Approve = 1 << 5,
    Administer = 1 << 6,

    Reader = View | Download,
    Contributor = View | Download | Edit,
    Approver = View | Download | Approve,
    Owner = View | Download | Edit | Delete | Share | Approve,
    Full = Owner | Administer
}

/// <summary>Who a document permission is granted to.</summary>
public enum PermissionSubjectType
{
    User = 0,
    Department = 1,
    Role = 2
}

/// <summary>Type of a metadata field, which determines validation, storage column and filter behaviour.</summary>
public enum MetadataFieldType
{
    Text = 0,
    Number = 1,
    Date = 2,
    Boolean = 3,
    Currency = 4,
    Select = 5
}
