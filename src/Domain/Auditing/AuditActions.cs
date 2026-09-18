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
    public const string UserRolesChanged = "user.roles.changed";
    public const string UserDepartmentsChanged = "user.departments.changed";

    public const string RolePermissionsChanged = "role.permissions.changed";
    public const string DepartmentCreated = "department.created";
    public const string DepartmentUpdated = "department.updated";
    public const string TenantSettingsChanged = "tenant.settings.changed";

    public const string AuthorizationDenied = "security.authorization.denied";
    public const string TenantIsolationViolationBlocked = "security.tenant_isolation.blocked";
}

/// <summary>Stable entity type identifiers used by the audit trail and its filters.</summary>
public static class AuditEntityTypes
{
    public const string User = "User";
    public const string Role = "Role";
    public const string Department = "Department";
    public const string Tenant = "Tenant";
    public const string Session = "Session";
}
