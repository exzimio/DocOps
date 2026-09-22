import type { PagedResult } from '@/features/documents/types'

/** Mirrors DocOps.Domain.Inbox.EmailStatus. */
export const EmailStatus = {
  Unread: 0,
  Read: 1,
  Processed: 2,
  Archived: 3,
} as const

export type EmailStatusValue = (typeof EmailStatus)[keyof typeof EmailStatus]

/** Mirrors DocOps.Domain.Inbox.EmailImportance. */
export const EmailImportance = {
  Low: 0,
  Normal: 1,
  High: 2,
} as const

export type EmailImportanceValue = (typeof EmailImportance)[keyof typeof EmailImportance]

/** Mirrors DocOps.Domain.Inbox.MailSource. A demo mailbox is never shown as a connected one. */
export const MailSource = {
  Demo: 0,
  Microsoft365: 1,
} as const

export type MailSourceValue = (typeof MailSource)[keyof typeof MailSource]

export interface EmailAttachment {
  id: string
  fileName: string
  contentType: string
  sizeBytes: number
  savedAsDocumentId: string | null
  savedAsDocumentReference: string | null
}

export interface EmailListItem {
  id: string
  subject: string
  fromName: string
  fromAddress: string
  mailbox: string
  receivedAtUtc: string
  status: EmailStatusValue
  importance: EmailImportanceValue
  source: MailSourceValue
  attachmentCount: number
  preview: string
  linkedDocumentId: string | null
  linkedDocumentReference: string | null
  linkedDocumentTitle: string | null
}

export interface EmailDetail {
  summary: EmailListItem
  body: string
  recipients: string | null
  processedAtUtc: string | null
  processedByName: string | null
  startedWorkflowInstanceId: string | null
  startedWorkflowReference: string | null
  attachments: EmailAttachment[]
  canAct: boolean
  canArchive: boolean
  canReopen: boolean
  canMarkProcessed: boolean
}

export interface MailSyncResult {
  source: MailSourceValue
  connectorName: string
  mailboxes: string[]
  fetched: number
  added: number
  alreadyKnown: number
  ranAtUtc: string
}

export interface MailConnector {
  source: MailSourceValue
  name: string
  description: string
  requiresCredentials: boolean
}

export interface InboxSummary {
  unread: number
  open: number
  processed: number
  archived: number
}

export interface InboxFilters {
  query: string
  statuses: EmailStatusValue[]
  mailbox: string | null
  onlyWithAttachments: boolean
  page: number
  pageSize: number
}

export const defaultInboxFilters: InboxFilters = {
  query: '',
  statuses: [EmailStatus.Unread, EmailStatus.Read],
  mailbox: null,
  onlyWithAttachments: false,
  page: 1,
  pageSize: 20,
}

export type InboxPage = PagedResult<EmailListItem>
