import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type ApiError } from '@/lib/api'
import type {
  IntegrationCard,
  IntegrationLogLevelValue,
  IntegrationLogPage,
  IntegrationRunResult,
} from './types'

export const integrationKeys = {
  all: ['integrations'] as const,
  list: ['integrations', 'list'] as const,
  detail: (id: string) => ['integrations', 'detail', id] as const,
  log: (id: string, levels: IntegrationLogLevelValue[], page: number) =>
    ['integrations', 'log', id, levels, page] as const,
}

export function useIntegrations() {
  return useQuery<IntegrationCard[], ApiError>({
    queryKey: integrationKeys.list,
    queryFn: () => api.get<IntegrationCard[]>('/api/integrations'),
    // Cards change on demand (a run, a toggle); a short stale time keeps the screen from refetching
    // on every focus while still catching a run made from another tab within a few seconds.
    staleTime: 10_000,
  })
}

export interface IntegrationLogFilters {
  levels: IntegrationLogLevelValue[]
  page: number
  pageSize: number
}

export function useIntegrationLog(integrationId: string, filters: IntegrationLogFilters) {
  return useQuery<IntegrationLogPage, ApiError>({
    queryKey: integrationKeys.log(integrationId, filters.levels, filters.page),
    queryFn: () => {
      const params = new URLSearchParams()
      filters.levels.forEach((level) => params.append('levels', String(level)))
      params.set('page', String(filters.page))
      params.set('pageSize', String(filters.pageSize))

      return api.get<IntegrationLogPage>(`/api/integrations/${integrationId}/log?${params}`)
    },
    placeholderData: (previous) => previous,
  })
}

function useInvalidateIntegrations() {
  const queryClient = useQueryClient()

  return () => void queryClient.invalidateQueries({ queryKey: integrationKeys.all })
}

export function useRunIntegration() {
  const invalidate = useInvalidateIntegrations()

  return useMutation<IntegrationRunResult, ApiError, string>({
    mutationFn: (integrationId) => api.post<IntegrationRunResult>(`/api/integrations/${integrationId}/run`),
    onSuccess: invalidate,
  })
}

export interface UpdateIntegrationInput {
  integrationId: string
  isEnabled: boolean
  /** Non-secret settings as JSON, or undefined to leave them as they are. */
  settingsJson?: string
}

export function useUpdateIntegration() {
  const invalidate = useInvalidateIntegrations()

  return useMutation<IntegrationCard, ApiError, UpdateIntegrationInput>({
    mutationFn: ({ integrationId, ...body }) =>
      api.put<IntegrationCard>(`/api/integrations/${integrationId}`, body),
    onSuccess: invalidate,
  })
}
