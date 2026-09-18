export const DocumentStatus = {
  Draft: 0,
  InReview: 1,
  PendingApproval: 2,
  Approved: 3,
  Rejected: 4,
  SignatureRequired: 5,
  Signed: 6,
  Archived: 7,
} as const

export type DocumentStatusValue = (typeof DocumentStatus)[keyof typeof DocumentStatus]

export const SecurityClassification = {
  Public: 0,
  Internal: 1,
  Confidential: 2,
  Restricted: 3,
} as const

export type SecurityClassificationValue = (typeof SecurityClassification)[keyof typeof SecurityClassification]

export const PermissionSubjectType = {
  User: 0,
  Department: 1,
  Role: 2,
} as const

export type PermissionSubjectTypeValue = (typeof PermissionSubjectType)[keyof typeof PermissionSubjectType]

export const MetadataFieldType = {
  Text: 0,
  Number: 1,
  Date: 2,
  Boolean: 3,
  Currency: 4,
  Select: 5,
} as const

export type MetadataFieldTypeValue = (typeof MetadataFieldType)[keyof typeof MetadataFieldType]

export type AccessLevel = 'view' | 'download' | 'edit' | 'delete' | 'share' | 'approve' | 'administer'

export interface Tag {
  id: string
  name: string
  colour: string | null
}

export interface DocumentListItem {
  id: string
  reference: string
  title: string
  typeCode: string
  typeName: string
  typeIcon: string | null
  status: DocumentStatusValue
  classification: SecurityClassificationValue
  ownerName: string
  departmentName: string | null
  departmentCode: string | null
  currentVersionNumber: number
  fileSizeBytes: number | null
  createdAtUtc: string
  modifiedAtUtc: string | null
  retentionDueAtUtc: string | null
  tags: Tag[]
}

export interface DocumentVersion {
  id: string
  versionNumber: number
  fileName: string
  contentType: string
  fileSizeBytes: number
  contentHash: string
  comment: string | null
  createdAtUtc: string
  createdByName: string
  isCurrent: boolean
}

export interface DocumentMetadataValue {
  fieldId: string
  code: string
  label: string
  fieldType: MetadataFieldTypeValue
  isRequired: boolean
  value: string | null
  aiConfidence: number | null
}

export interface DocumentPermission {
  id: string
  subjectType: PermissionSubjectTypeValue
  subjectId: string | null
  roleName: string | null
  subjectDisplayName: string
  accessLevels: AccessLevel[]
  grantedByName: string | null
  grantedAtUtc: string
  expiresAtUtc: string | null
}

export interface AccessReason {
  source: string
  granted: AccessLevel[]
  description: string
}

export interface AccessRestriction {
  removed: AccessLevel[]
  description: string
}

export interface EffectiveAccess {
  levels: AccessLevel[]
  reasons: AccessReason[]
  restrictions: AccessRestriction[]
}

export interface DocumentDetail {
  summary: DocumentListItem
  description: string | null
  businessEntity: string | null
  documentTypeId: string
  departmentId: string | null
  ownerUserId: string
  ownerEmail: string | null
  retentionPolicyName: string | null
  archivedAtUtc: string | null
  versions: DocumentVersion[]
  metadata: DocumentMetadataValue[]
  permissions: DocumentPermission[]
  effectiveAccess: EffectiveAccess
  allowedNextStatuses: DocumentStatusValue[]
  canChangeContent: boolean
  canChangeMetadata: boolean
  concurrencyToken: string
}

export interface DocumentAuditEntry {
  id: string
  occurredAtUtc: string
  action: string
  actorDisplayName: string
  summary: string | null
  severity: string
  correlationId: string
  beforeJson: string | null
  afterJson: string | null
}

export interface DocumentMetadataField {
  id: string
  code: string
  label: string
  fieldType: MetadataFieldTypeValue
  isRequired: boolean
  displayOrder: number
  helpText: string | null
  options: string[]
}

export interface DocumentType {
  id: string
  code: string
  name: string
  description: string | null
  icon: string | null
  referencePrefix: string
  defaultClassification: SecurityClassificationValue
  isActive: boolean
  fields: DocumentMetadataField[]
}

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasPrevious: boolean
  hasNext: boolean
}

export interface DocumentFilters {
  query: string
  typeIds: string[]
  statuses: DocumentStatusValue[]
  departmentIds: string[]
  tagIds: string[]
  classifications: SecurityClassificationValue[]
  onlyMine: boolean
  includeArchived: boolean
  page: number
  pageSize: number
  sortBy: 'CreatedAt' | 'ModifiedAt' | 'Title' | 'Reference' | 'Status'
  sortDescending: boolean
}

export const defaultFilters: DocumentFilters = {
  query: '',
  typeIds: [],
  statuses: [],
  departmentIds: [],
  tagIds: [],
  classifications: [],
  onlyMine: false,
  includeArchived: true,
  page: 1,
  pageSize: 20,
  sortBy: 'CreatedAt',
  sortDescending: true,
}
