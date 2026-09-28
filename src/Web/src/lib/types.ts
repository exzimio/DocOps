export const UserType = {
  Internal: 0,
  External: 1,
} as const

export type UserTypeValue = (typeof UserType)[keyof typeof UserType]

export interface DepartmentMembership {
  id: string
  name: string
  code: string
  isPrimary: boolean
}

export interface CurrentUser {
  id: string
  email: string
  fullName: string
  initials: string
  jobTitle: string | null
  tenantId: string
  tenantName: string
  userType: UserTypeValue
  /** For an external user, the organisation it represents. Null for everyone inside. */
  externalOrganisation: string | null
  preferredCulture: string
  isPlatformAdministrator: boolean
  lastLoginAtUtc: string | null
  roles: string[]
  permissions: string[]
  departments: DepartmentMembership[]
}

export interface Department {
  id: string
  name: string
  code: string
  description: string | null
  parentDepartmentId: string | null
  managerUserId: string | null
  managerDisplayName: string | null
  memberCount: number
  isActive: boolean
}

export interface TenantProfile {
  id: string
  name: string
  legalName: string | null
  slug: string
  defaultCulture: string
  timeZoneId: string
  isActive: boolean
  departments: Department[]
}

/** Mirrors DocOps.Domain.Identity.Permissions on the server. The server remains the only enforcement point. */
export const Permission = {
  documentsView: 'documents.view',
  documentsCreate: 'documents.create',
  documentsEdit: 'documents.edit',
  documentsApprove: 'documents.approve',
  workflowsView: 'workflows.view',
  workflowsStart: 'workflows.start',
  workflowsActivate: 'workflows.activate',
  workflowsCancel: 'workflows.cancel',
  tasksAct: 'tasks.act',
  tasksReassign: 'tasks.reassign',
  workflowsDesign: 'workflows.design',
  tasksViewOwn: 'tasks.view.own',
  tasksViewAll: 'tasks.view.all',
  aiUse: 'ai.use',
  searchUse: 'search.use',
  inboxView: 'inbox.view',
  inboxProcess: 'inbox.process',
  integrationsView: 'integrations.view',
  integrationsManage: 'integrations.manage',
  auditView: 'audit.view',
  reportsView: 'reports.view',
  reportsExport: 'reports.export',
  adminUsers: 'admin.users',
  adminDepartments: 'admin.departments',
  adminDocumentTypes: 'admin.documenttypes',
  signaturesRequest: 'signatures.request',
  signaturesSign: 'signatures.sign',
  portalAccess: 'portal.access',
  portalSubmit: 'portal.submit',
} as const
