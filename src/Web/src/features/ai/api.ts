import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type ApiError } from '@/lib/api'
import type { PagedResult } from '@/features/documents/types'
import type {
  AiProvider,
  AiRun,
  AiRunListItem,
  AiRunStatusValue,
  AiStatistics,
  AiSuggestionDecisionValue,
  DocumentAnswer,
  DocumentText,
} from './types'

export const aiKeys = {
  all: ['ai'] as const,
  provider: ['ai', 'provider'] as const,
  statistics: ['ai', 'statistics'] as const,
  runs: (statuses: AiRunStatusValue[], page: number) => ['ai', 'runs', statuses, page] as const,
  latest: (documentId: string) => ['ai', 'latest', documentId] as const,
  text: (documentId: string) => ['documents', 'text', documentId] as const,
}

export function useAiProvider() {
  return useQuery<AiProvider, ApiError>({
    queryKey: aiKeys.provider,
    queryFn: () => api.get<AiProvider>('/api/ai/provider'),
    staleTime: 10 * 60_000,
  })
}

export function useAiStatistics() {
  return useQuery<AiStatistics, ApiError>({
    queryKey: aiKeys.statistics,
    queryFn: () => api.get<AiStatistics>('/api/ai/statistics'),
  })
}

export function useAiRuns(statuses: AiRunStatusValue[], page: number) {
  return useQuery<PagedResult<AiRunListItem>, ApiError>({
    queryKey: aiKeys.runs(statuses, page),
    queryFn: () => {
      const params = new URLSearchParams({ page: String(page), pageSize: '20' })
      statuses.forEach((status) => params.append('statuses', String(status)))
      return api.get<PagedResult<AiRunListItem>>(`/api/ai/runs?${params}`)
    },
    placeholderData: (previous) => previous,
  })
}

/** The latest analysis of a document; null when it was never analysed (the API answers 204). */
export function useLatestAiRun(documentId: string, enabled = true) {
  return useQuery<AiRun | null, ApiError>({
    queryKey: aiKeys.latest(documentId),
    queryFn: async () =>
      (await api.get<AiRun | undefined>(`/api/documents/${documentId}/ai-runs/latest`)) ?? null,
    enabled,
  })
}

export function useDocumentText(documentId: string, enabled = true) {
  return useQuery<DocumentText, ApiError>({
    queryKey: aiKeys.text(documentId),
    queryFn: () => api.get<DocumentText>(`/api/documents/${documentId}/text`),
    enabled,
    staleTime: 5 * 60_000,
  })
}

/**
 * Every review action returns the updated run, which replaces the cached one. Applying also changes
 * the document, so its detail is refetched rather than guessed at.
 */
function useRunMutation<TVariables>(documentId: string, request: (variables: TVariables) => Promise<AiRun>) {
  const queryClient = useQueryClient()

  return useMutation<AiRun, ApiError, TVariables>({
    mutationFn: request,
    onSuccess: (run) => {
      queryClient.setQueryData(aiKeys.latest(documentId), run)
      void queryClient.invalidateQueries({ queryKey: ['ai', 'runs'] })
      void queryClient.invalidateQueries({ queryKey: aiKeys.statistics })
      // Every decision is written to the document's audit trail.
      void queryClient.invalidateQueries({ queryKey: ['documents', 'audit', documentId] })
    },
  })
}

export function useProcessDocument(documentId: string) {
  const queryClient = useQueryClient()

  return useMutation<AiRun, ApiError, void>({
    mutationFn: () => api.post<AiRun>(`/api/documents/${documentId}/process-ai`),
    onSuccess: (run) => {
      queryClient.setQueryData(aiKeys.latest(documentId), run)
      void queryClient.invalidateQueries({ queryKey: ['ai', 'runs'] })
      void queryClient.invalidateQueries({ queryKey: aiKeys.statistics })
      void queryClient.invalidateQueries({ queryKey: ['documents', 'audit', documentId] })
    },
  })
}

export function useDecideField(documentId: string, runId: string) {
  return useRunMutation<{ suggestionId: string; decision: AiSuggestionDecisionValue; value?: string | null }>(
    documentId,
    ({ suggestionId, decision, value }) =>
      api.post<AiRun>(`/api/ai/runs/${runId}/fields/${suggestionId}/decision`, {
        decision,
        value: value ?? null,
      }),
  )
}

export function useDecideClassification(documentId: string, runId: string) {
  return useRunMutation<boolean>(documentId, (accept) =>
    api.post<AiRun>(`/api/ai/runs/${runId}/classification`, { accept }),
  )
}

export function useAcceptAll(documentId: string, runId: string) {
  return useRunMutation<void>(documentId, () => api.post<AiRun>(`/api/ai/runs/${runId}/accept-all`))
}

export function useDiscardRun(documentId: string, runId: string) {
  return useRunMutation<void>(documentId, () => api.post<AiRun>(`/api/ai/runs/${runId}/discard`))
}

export function useApplyRun(documentId: string, runId: string) {
  const queryClient = useQueryClient()

  return useMutation<AiRun, ApiError, void>({
    mutationFn: () => api.post<AiRun>(`/api/ai/runs/${runId}/apply`),
    onSuccess: (run) => {
      queryClient.setQueryData(aiKeys.latest(documentId), run)
      void queryClient.invalidateQueries({ queryKey: ['documents'] })
      void queryClient.invalidateQueries({ queryKey: aiKeys.all })
    },
  })
}

export function useAskDocument(documentId: string) {
  return useMutation<DocumentAnswer, ApiError, string>({
    mutationFn: (question) => api.post<DocumentAnswer>('/api/ai/document-question', { documentId, question }),
  })
}
