namespace DocOps.Domain.Auditing;

/// <summary>Stable action identifiers written to the audit trail. Values are persisted, so they are never renamed.</summary>
public static class AuditActions
{
    public const string UserLoginSucceeded = "user.login.succeeded";
    public const string UserLoginFailed = "user.login.failed";
    public const string UserLoginLockedOut = "user.login.locked_out";
    public const string UserLogout = "user.logout";
    public const string UserPasswordChanged = "user.password.changed";
    public const string UserCreated = "user.created";
    public const string UserUpdated = "user.updated";
    public const string UserDeactivated = "user.deactivated";
    public const string UserActivated = "user.activated";
    public const string UserRolesChanged = "user.roles.changed";
    public const string UserDepartmentsChanged = "user.departments.changed";

    public const string RolePermissionsChanged = "role.permissions.changed";
    public const string DepartmentCreated = "department.created";
    public const string DepartmentUpdated = "department.updated";
    public const string TenantSettingsChanged = "tenant.settings.changed";

    public const string DocumentTypeCreated = "documenttype.created";
    public const string DocumentTypeUpdated = "documenttype.updated";
    public const string DocumentTypeActivationChanged = "documenttype.activation.changed";
    public const string MetadataFieldCreated = "metadatafield.created";
    public const string MetadataFieldUpdated = "metadatafield.updated";
    public const string MetadataFieldDeleted = "metadatafield.deleted";
    public const string TagCreated = "tag.created";
    public const string TagUpdated = "tag.updated";
    public const string TagDeleted = "tag.deleted";
    public const string TagRestored = "tag.restored";
    public const string RetentionPolicyCreated = "retentionpolicy.created";
    public const string RetentionPolicyUpdated = "retentionpolicy.updated";
    public const string RetentionPolicyActivationChanged = "retentionpolicy.activation.changed";

    public const string DocumentRetentionDue = "document.retention.due";
    public const string DocumentCreated = "document.created";
    public const string DocumentViewed = "document.viewed";
    public const string DocumentDownloaded = "document.downloaded";
    public const string DocumentPreviewed = "document.previewed";
    public const string DocumentMetadataChanged = "document.metadata.changed";
    public const string DocumentStatusChanged = "document.status.changed";
    public const string DocumentVersionCreated = "document.version.created";
    public const string DocumentTagsChanged = "document.tags.changed";
    public const string DocumentPermissionGranted = "document.permission.granted";
    public const string DocumentPermissionRevoked = "document.permission.revoked";
    public const string DocumentArchived = "document.archived";
    public const string DocumentRestored = "document.restored";
    public const string DocumentDeleted = "document.deleted";
    public const string DocumentAccessDenied = "document.access.denied";

    public const string WorkflowDefinitionCreated = "workflow.definition.created";
    public const string WorkflowDefinitionUpdated = "workflow.definition.updated";
    public const string WorkflowDefinitionActivated = "workflow.definition.activated";
    public const string WorkflowDefinitionDeactivated = "workflow.definition.deactivated";
    public const string WorkflowInstanceStarted = "workflow.instance.started";
    public const string WorkflowInstanceCompleted = "workflow.instance.completed";
    public const string WorkflowInstanceCancelled = "workflow.instance.cancelled";
    public const string WorkflowInstanceFailed = "workflow.instance.failed";
    public const string WorkflowTaskCompleted = "workflow.task.completed";
    public const string WorkflowTaskReassigned = "workflow.task.reassigned";
    public const string WorkflowTaskEscalated = "workflow.task.escalated";

    public const string DocumentReclassified = "document.reclassified";
    public const string DocumentQuestionAsked = "document.question.asked";

    public const string AiRunCreated = "ai.run.created";
    public const string AiRunFailed = "ai.run.failed";
    public const string AiRunApplied = "ai.run.applied";
    public const string AiRunDiscarded = "ai.run.discarded";
    public const string AiClassificationAccepted = "ai.classification.accepted";
    public const string AiClassificationRejected = "ai.classification.rejected";
    public const string AiSuggestionAccepted = "ai.suggestion.accepted";
    public const string AiSuggestionEdited = "ai.suggestion.edited";
    public const string AiSuggestionRejected = "ai.suggestion.rejected";

    public const string MailSynchronised = "inbox.synchronised";
    public const string EmailRead = "inbox.email.read";
    public const string EmailArchived = "inbox.email.archived";
    public const string EmailReopened = "inbox.email.reopened";
    public const string EmailProcessed = "inbox.email.processed";
    public const string EmailLinkedToDocument = "inbox.email.linked";
    public const string EmailAttachmentSaved = "inbox.attachment.saved";
    public const string EmailDocumentCreated = "inbox.document.created";
    public const string EmailWorkflowStarted = "inbox.workflow.started";

    public const string IntegrationRun = "integration.run";
    public const string IntegrationConfigured = "integration.configured";

    public const string SignatureRequested = "signature.requested";
    public const string SignatureSigned = "signature.signed";
    public const string SignatureDeclined = "signature.declined";
    public const string SignatureCancelled = "signature.cancelled";

    public const string PortalDocumentSubmitted = "portal.document.submitted";
    public const string PortalInformationProvided = "portal.information.provided";

    public const string AuthorizationDenied = "security.authorization.denied";
    public const string TenantIsolationViolationBlocked = "security.tenant_isolation.blocked";
}

/// <summary>Stable entity type identifiers used by the audit trail and its filters.</summary>
public static class AuditEntityTypes
{
    public const string User = "User";
    public const string Role = "Role";
    public const string Department = "Department";
    public const string DocumentType = "DocumentType";
    public const string MetadataField = "MetadataField";
    public const string Tag = "Tag";
    public const string RetentionPolicy = "RetentionPolicy";
    public const string Tenant = "Tenant";
    public const string Session = "Session";
    public const string Document = "Document";
    public const string DocumentVersion = "DocumentVersion";
    public const string DocumentPermission = "DocumentPermission";
    public const string WorkflowDefinition = "WorkflowDefinition";
    public const string WorkflowInstance = "WorkflowInstance";
    public const string WorkflowTask = "WorkflowTask";
    public const string EmailMessage = "EmailMessage";
    public const string Integration = "Integration";
    public const string SignatureRequest = "SignatureRequest";
}
