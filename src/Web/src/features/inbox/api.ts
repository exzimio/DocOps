import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type ApiError } from '@/lib/api'
import type { DocumentDetail } from '@/features/documents/types'
import type {
  EmailDetail,
  InboxFilters,
  InboxPage,
  InboxSummary,
  MailConnector,
  MailSyncResult,
} from './types'

export const inboxKeys = {
  all: ['inbox'] as const,
  list: (filters: InboxFilters) => ['inbox', 'list', filters] as const,
  detail: (id: string) => ['inbox', 'detail', id] as const,
  summary: ['inbox', 'summary'] as const,
  connector: ['inbox', 'connector'] as const,
}

function buildSearchParams(filters: InboxFilters): string {
  const params = new URLSearchParams()

  if (filters.query.trim()) params.set('query', filters.query.trim())
  filters.statuses.forEach((status) => params.append('statuses', String(status)))
  if (filters.mailbox) params.set('mailbox', filters.mailbox)
  if (filters.onlyWithAttachments) params.set('onlyWithAttachments', 'true')
  params.set('page', String(filters.page))
  params.set('pageSize', String(filters.pageSize))

  return params.toString()
}

export function useInboxMessages(filters: InboxFilters) {
  return useQuery<InboxPage, ApiError>({
    queryKey: inboxKeys.list(filters),
    queryFn: () => api.get<InboxPage>(`/api/inbox?${buildSearchParams(filters)}`),
    placeholderData: (previous) => previous,
  })
}

export function useInboxMessage(id: string | null) {
  return useQuery<EmailDetail, ApiError>({
    queryKey: inboxKeys.detail(id ?? ''),
    queryFn: () => api.get<EmailDetail>(`/api/inbox/${id}`),
    enabled: id !== null,
  })
}

export function useInboxSummary() {
  return useQuery<InboxSummary, ApiError>({
    queryKey: inboxKeys.summary,
    queryFn: () => api.get<InboxSummary>('/api/inbox/summary'),
    staleTime: 30_000,
  })
}

export function useMailConnector() {
  return useQuery<MailConnector, ApiError>({
    queryKey: inboxKeys.connector,
    queryFn: () => api.get<MailConnector>('/api/inbox/connector'),
    staleTime: 5 * 60_000,
  })
}

/** Invalidates everything a mutation on one message can have changed: its own detail, every list, the summary. */
function useInvalidateInbox() {
  const queryClient = useQueryClient()

  return (emailId?: string) => {
    void queryClient.invalidateQueries({ queryKey: inboxKeys.all })
    if (emailId) {
      void queryClient.invalidateQueries({ queryKey: inboxKeys.detail(emailId) })
    }
  }
}

export function useSyncInbox() {
  const invalidate = useInvalidateInbox()

  return useMutation<MailSyncResult, ApiError, void>({
    mutationFn: () => api.post<MailSyncResult>('/api/inbox/sync'),
    onSuccess: () => invalidate(),
  })
}

export function useMarkRead() {
  const invalidate = useInvalidateInbox()

  return useMutation<EmailDetail, ApiError, string>({
    mutationFn: (emailId) => api.post<EmailDetail>(`/api/inbox/${emailId}/read`),
    onSuccess: (_, emailId) => invalidate(emailId),
  })
}

export function useArchiveEmail() {
  const invalidate = useInvalidateInbox()

  return useMutation<EmailDetail, ApiError, string>({
    mutationFn: (emailId) => api.post<EmailDetail>(`/api/inbox/${emailId}/archive`),
    onSuccess: (_, emailId) => invalidate(emailId),
  })
}

export function useReopenEmail() {
  const invalidate = useInvalidateInbox()

  return useMutation<EmailDetail, ApiError, string>({
    mutationFn: (emailId) => api.post<EmailDetail>(`/api/inbox/${emailId}/reopen`),
    onSuccess: (_, emailId) => invalidate(emailId),
  })
}

export function useMarkProcessed() {
  const invalidate = useInvalidateInbox()

  return useMutation<EmailDetail, ApiError, string>({
    mutationFn: (emailId) => api.post<EmailDetail>(`/api/inbox/${emailId}/processed`),
    onSuccess: (_, emailId) => invalidate(emailId),
  })
}

export function useLinkEmailToDocument() {
  const invalidate = useInvalidateInbox()

  return useMutation<EmailDetail, ApiError, { emailId: string; documentId: string }>({
    mutationFn: ({ emailId, documentId }) =>
      api.post<EmailDetail>(`/api/inbox/${emailId}/link`, { documentId }),
    onSuccess: (_, { emailId }) => invalidate(emailId),
  })
}

export interface SaveAttachmentInput {
  emailId: string
  attachmentId: string
  documentTypeId: string
  title?: string
  departmentId?: string
  classification?: number
  tags: string[]
  metadata: Record<string, string>
}

export function useSaveAttachmentAsDocument() {
  const invalidate = useInvalidateInbox()
  const queryClient = useQueryClient()

  return useMutation<DocumentDetail, ApiError, SaveAttachmentInput>({
    mutationFn: ({ emailId, ...body }) =>
      api.post<DocumentDetail>(`/api/inbox/${emailId}/attachments/save`, body),
    onSuccess: (_, { emailId }) => {
      invalidate(emailId)
      void queryClient.invalidateQueries({ queryKey: ['documents'] })
    },
  })
}

export interface CreateDocumentFromEmailInput {
  emailId: string
  documentTypeId: string
  title?: string
  departmentId?: string
  classification?: number
  tags: string[]
  metadata: Record<string, string>
}

export function useCreateDocumentFromEmail() {
  const invalidate = useInvalidateInbox()
  const queryClient = useQueryClient()

  return useMutation<DocumentDetail, ApiError, CreateDocumentFromEmailInput>({
    mutationFn: ({ emailId, ...body }) => api.post<DocumentDetail>(`/api/inbox/${emailId}/document`, body),
    onSuccess: (_, { emailId }) => {
      invalidate(emailId)
      void queryClient.invalidateQueries({ queryKey: ['documents'] })
    },
  })
}

export function useStartWorkflowFromEmail() {
  const invalidate = useInvalidateInbox()

  return useMutation<
    EmailDetail,
    ApiError,
    { emailId: string; workflowDefinitionId: string; comment?: string }
  >({
    mutationFn: ({ emailId, ...body }) => api.post<EmailDetail>(`/api/inbox/${emailId}/workflow`, body),
    onSuccess: (_, { emailId }) => invalidate(emailId),
  })
}
