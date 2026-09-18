import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, api } from '@/lib/api'
import type {
  DocumentAuditEntry,
  DocumentDetail,
  DocumentFilters,
  DocumentListItem,
  DocumentPermission,
  DocumentType,
  PagedResult,
  Tag,
} from './types'

export const documentKeys = {
  all: ['documents'] as const,
  list: (filters: DocumentFilters) => ['documents', 'list', filters] as const,
  detail: (id: string) => ['documents', 'detail', id] as const,
  audit: (id: string, page: number) => ['documents', 'audit', id, page] as const,
  types: ['documents', 'types'] as const,
  tags: ['documents', 'tags'] as const,
}

function buildSearchParams(filters: DocumentFilters): string {
  const params = new URLSearchParams()

  if (filters.query.trim()) params.set('query', filters.query.trim())
  filters.typeIds.forEach((id) => params.append('typeIds', id))
  filters.statuses.forEach((status) => params.append('statuses', String(status)))
  filters.departmentIds.forEach((id) => params.append('departmentIds', id))
  filters.tagIds.forEach((id) => params.append('tagIds', id))
  filters.classifications.forEach((value) => params.append('classifications', String(value)))
  if (filters.onlyMine) params.set('onlyMine', 'true')
  if (!filters.includeArchived) params.set('includeArchived', 'false')
  params.set('sortBy', filters.sortBy)
  params.set('sortDescending', String(filters.sortDescending))
  params.set('page', String(filters.page))
  params.set('pageSize', String(filters.pageSize))

  return params.toString()
}

export function useDocuments(filters: DocumentFilters) {
  return useQuery<PagedResult<DocumentListItem>, ApiError>({
    queryKey: documentKeys.list(filters),
    queryFn: () => api.get<PagedResult<DocumentListItem>>(`/api/documents?${buildSearchParams(filters)}`),
    placeholderData: (previous) => previous,
  })
}

export function useDocument(id: string) {
  return useQuery<DocumentDetail, ApiError>({
    queryKey: documentKeys.detail(id),
    queryFn: () => api.get<DocumentDetail>(`/api/documents/${id}`),
  })
}

export function useDocumentAudit(id: string, page = 1) {
  return useQuery<PagedResult<DocumentAuditEntry>, ApiError>({
    queryKey: documentKeys.audit(id, page),
    queryFn: () =>
      api.get<PagedResult<DocumentAuditEntry>>(`/api/documents/${id}/audit?page=${page}&pageSize=25`),
  })
}

export function useDocumentTypes() {
  return useQuery<DocumentType[], ApiError>({
    queryKey: documentKeys.types,
    queryFn: () => api.get<DocumentType[]>('/api/document-catalogue/types'),
    staleTime: 5 * 60_000,
  })
}

export function useTags() {
  return useQuery<Tag[], ApiError>({
    queryKey: documentKeys.tags,
    queryFn: () => api.get<Tag[]>('/api/document-catalogue/tags'),
    staleTime: 5 * 60_000,
  })
}

export interface CreateDocumentInput {
  file: File
  title: string
  documentTypeId: string
  departmentId?: string
  description?: string
  classification?: number
  tags: string[]
  metadata: Record<string, string>
}

/** Multipart upload: the browser sets the boundary, so no Content-Type header is passed here. */
export function useCreateDocument() {
  const queryClient = useQueryClient()

  return useMutation<DocumentDetail, ApiError, CreateDocumentInput>({
    mutationFn: async (input) => {
      const form = new FormData()
      form.append('File', input.file)
      form.append('Title', input.title)
      form.append('DocumentTypeId', input.documentTypeId)
      if (input.departmentId) form.append('DepartmentId', input.departmentId)
      if (input.description) form.append('Description', input.description)
      if (input.classification !== undefined) form.append('Classification', String(input.classification))
      input.tags.forEach((tag) => form.append('Tags', tag))
      Object.entries(input.metadata)
        .filter(([, value]) => value !== '')
        .forEach(([code, value]) => form.append('Metadata', `${code}=${value}`))

      const response = await fetch('/api/documents', {
        method: 'POST',
        credentials: 'include',
        body: form,
      })

      if (!response.ok) {
        throw new ApiError(response.status, await response.json())
      }

      return (await response.json()) as DocumentDetail
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: documentKeys.all }),
  })
}

export function useAddVersion(documentId: string) {
  const queryClient = useQueryClient()

  return useMutation<DocumentDetail, ApiError, { file: File; comment?: string }>({
    mutationFn: async ({ file, comment }) => {
      const form = new FormData()
      form.append('File', file)
      if (comment) form.append('Comment', comment)

      const response = await fetch(`/api/documents/${documentId}/versions`, {
        method: 'POST',
        credentials: 'include',
        body: form,
      })

      if (!response.ok) {
        throw new ApiError(response.status, await response.json())
      }

      return (await response.json()) as DocumentDetail
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: documentKeys.all }),
  })
}

export interface UpdateDocumentInput {
  title: string
  description?: string
  departmentId?: string | null
  businessEntity?: string | null
  classification: number
  tags: string[]
  metadata: Record<string, string>
  concurrencyToken: string
}

export function useUpdateDocument(documentId: string) {
  const queryClient = useQueryClient()

  return useMutation<DocumentDetail, ApiError, UpdateDocumentInput>({
    mutationFn: (input) => api.put<DocumentDetail>(`/api/documents/${documentId}`, input),
    onSuccess: (detail) => {
      queryClient.setQueryData(documentKeys.detail(documentId), detail)
      void queryClient.invalidateQueries({ queryKey: documentKeys.all })
    },
  })
}

export function useChangeStatus(documentId: string) {
  const queryClient = useQueryClient()

  return useMutation<DocumentDetail, ApiError, { status: number; comment?: string }>({
    mutationFn: (input) => api.post<DocumentDetail>(`/api/documents/${documentId}/status`, input),
    onSuccess: (detail) => {
      queryClient.setQueryData(documentKeys.detail(documentId), detail)
      void queryClient.invalidateQueries({ queryKey: documentKeys.all })
    },
  })
}

export function useArchiveDocument(documentId: string) {
  const queryClient = useQueryClient()

  return useMutation<DocumentDetail, ApiError, { restore: boolean }>({
    mutationFn: ({ restore }) =>
      api.post<DocumentDetail>(`/api/documents/${documentId}/${restore ? 'restore' : 'archive'}`),
    onSuccess: (detail) => {
      queryClient.setQueryData(documentKeys.detail(documentId), detail)
      void queryClient.invalidateQueries({ queryKey: documentKeys.all })
    },
  })
}

export function useDeleteDocument() {
  const queryClient = useQueryClient()

  return useMutation<void, ApiError, string>({
    mutationFn: (documentId) => api.delete<void>(`/api/documents/${documentId}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: documentKeys.all }),
  })
}

export function useGrantPermission(documentId: string) {
  const queryClient = useQueryClient()

  return useMutation<
    DocumentPermission,
    ApiError,
    { subjectType: number; subjectId?: string; roleName?: string; accessLevels: string[] }
  >({
    mutationFn: (input) => api.post<DocumentPermission>(`/api/documents/${documentId}/permissions`, input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: documentKeys.detail(documentId) }),
  })
}

export function useRevokePermission(documentId: string) {
  const queryClient = useQueryClient()

  return useMutation<void, ApiError, string>({
    mutationFn: (permissionId) =>
      api.delete<void>(`/api/documents/${documentId}/permissions/${permissionId}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: documentKeys.detail(documentId) }),
  })
}

export function downloadUrl(documentId: string, version?: number): string {
  return version === undefined
    ? `/api/documents/${documentId}/content`
    : `/api/documents/${documentId}/content?version=${version}`
}
