import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '@/lib/api'
import type { PortalSubmission, PortalSubmissionDetail } from './types'

export const portalKeys = {
  all: ['portal'] as const,
  submissions: ['portal', 'submissions'] as const,
  submission: (id: string) => ['portal', 'submissions', id] as const,
}

export function usePortalSubmissions() {
  return useQuery<PortalSubmission[], ApiError>({
    queryKey: portalKeys.submissions,
    queryFn: () => api.get<PortalSubmission[]>('/api/portal/submissions'),
    staleTime: 10_000,
  })
}

export function usePortalSubmission(id: string | undefined) {
  return useQuery<PortalSubmissionDetail, ApiError>({
    queryKey: portalKeys.submission(id ?? ''),
    queryFn: () => api.get<PortalSubmissionDetail>(`/api/portal/submissions/${id}`),
    enabled: !!id,
  })
}

export interface SubmitInput {
  file: File
  title: string
  note?: string
}

/** Multipart upload: the browser sets the boundary, so no Content-Type header is passed here. */
export function useSubmitToPortal() {
  const queryClient = useQueryClient()

  return useMutation<PortalSubmission, ApiError, SubmitInput>({
    mutationFn: async (input) => {
      const form = new FormData()
      form.append('File', input.file)
      form.append('Title', input.title)
      if (input.note) form.append('Note', input.note)

      const response = await fetch('/api/portal/submissions', {
        method: 'POST',
        credentials: 'include',
        body: form,
      })

      if (!response.ok) {
        throw new ApiError(response.status, await response.json())
      }

      return (await response.json()) as PortalSubmission
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: portalKeys.all }),
  })
}

export function useProvideInformation(documentId: string) {
  const queryClient = useQueryClient()

  return useMutation<PortalSubmissionDetail, ApiError, string>({
    mutationFn: (body) =>
      api.post<PortalSubmissionDetail>(`/api/portal/submissions/${documentId}/information`, { body }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: portalKeys.all }),
  })
}
