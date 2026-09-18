import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type ApiError } from '@/lib/api'
import type { PagedResult } from '@/features/documents/types'
import type {
  Notification,
  TaskAction,
  TaskFilters,
  TaskListItem,
  TaskStatistics,
  WorkflowDefinitionDetail,
  WorkflowDefinitionSummary,
  WorkflowInstanceDetail,
  WorkflowInstanceListItem,
  WorkflowValidationIssue,
} from './types'

export const workflowKeys = {
  all: ['workflows'] as const,
  definitions: ['workflows', 'definitions'] as const,
  definition: (id: string) => ['workflows', 'definition', id] as const,
  instances: (filters: unknown) => ['workflows', 'instances', filters] as const,
  instance: (id: string) => ['workflows', 'instance', id] as const,
  tasks: (filters: TaskFilters) => ['tasks', 'list', filters] as const,
  taskStatistics: (scope: string) => ['tasks', 'statistics', scope] as const,
  notifications: ['notifications'] as const,
  unreadCount: ['notifications', 'unread'] as const,
}

export function useWorkflowDefinitions() {
  return useQuery<WorkflowDefinitionSummary[], ApiError>({
    queryKey: workflowKeys.definitions,
    queryFn: () => api.get<WorkflowDefinitionSummary[]>('/api/workflows'),
    staleTime: 60_000,
  })
}

export function useWorkflowDefinition(id: string) {
  return useQuery<WorkflowDefinitionDetail, ApiError>({
    queryKey: workflowKeys.definition(id),
    queryFn: () => api.get<WorkflowDefinitionDetail>(`/api/workflows/${id}`),
  })
}

export function useWorkflowInstances(filters: { definitionId?: string; documentId?: string; page: number }) {
  return useQuery<PagedResult<WorkflowInstanceListItem>, ApiError>({
    queryKey: workflowKeys.instances(filters),
    queryFn: () => {
      const params = new URLSearchParams({ page: String(filters.page), pageSize: '20' })
      if (filters.definitionId) params.set('definitionId', filters.definitionId)
      if (filters.documentId) params.set('documentId', filters.documentId)

      return api.get<PagedResult<WorkflowInstanceListItem>>(`/api/workflows/instances?${params}`)
    },
    placeholderData: (previous) => previous,
  })
}

export function useWorkflowInstance(id: string) {
  return useQuery<WorkflowInstanceDetail, ApiError>({
    queryKey: workflowKeys.instance(id),
    queryFn: () => api.get<WorkflowInstanceDetail>(`/api/workflows/instances/${id}`),
  })
}

export function useSetDefinitionActive(definitionId: string) {
  const queryClient = useQueryClient()

  return useMutation<WorkflowDefinitionDetail, ApiError, boolean>({
    mutationFn: (isActive) =>
      api.post<WorkflowDefinitionDetail>(
        `/api/workflows/${definitionId}/${isActive ? 'activate' : 'deactivate'}`,
      ),
    onSuccess: (detail) => {
      queryClient.setQueryData(workflowKeys.definition(definitionId), detail)
      void queryClient.invalidateQueries({ queryKey: workflowKeys.definitions })
    },
  })
}

export interface SaveWorkflowNodeInput {
  key: string
  name: string
  description: string | null
  type: number
  configJson: string | null
  positionX: number
  positionY: number
}

export interface SaveWorkflowEdgeInput {
  fromNodeKey: string
  toNodeKey: string
  label: string | null
  branch: string | null
}

export interface SaveWorkflowInput {
  name: string
  description: string | null
  documentTypeId: string | null
  trigger: number
  nodes: SaveWorkflowNodeInput[]
  edges: SaveWorkflowEdgeInput[]
  concurrencyToken: string
}

export function useCreateWorkflow() {
  const queryClient = useQueryClient()

  return useMutation<
    WorkflowDefinitionDetail,
    ApiError,
    { name: string; description?: string | null; documentTypeId?: string | null }
  >({
    mutationFn: (input) => api.post<WorkflowDefinitionDetail>('/api/workflows', input),
    onSuccess: (detail) => {
      queryClient.setQueryData(workflowKeys.definition(detail.summary.id), detail)
      void queryClient.invalidateQueries({ queryKey: workflowKeys.definitions })
    },
  })
}

/**
 * Saving may return a *different* definition id: once a process has instances the server supersedes
 * it with a new version instead of rewriting the graph those instances still resolve against.
 */
export function useSaveWorkflow(definitionId: string) {
  const queryClient = useQueryClient()

  return useMutation<WorkflowDefinitionDetail, ApiError, SaveWorkflowInput>({
    mutationFn: (input) => api.put<WorkflowDefinitionDetail>(`/api/workflows/${definitionId}`, input),
    onSuccess: (detail) => {
      queryClient.setQueryData(workflowKeys.definition(detail.summary.id), detail)
      void queryClient.invalidateQueries({ queryKey: workflowKeys.definition(definitionId) })
      void queryClient.invalidateQueries({ queryKey: workflowKeys.definitions })
    },
  })
}

export function useDuplicateWorkflow() {
  const queryClient = useQueryClient()

  return useMutation<WorkflowDefinitionDetail, ApiError, { definitionId: string; name: string }>({
    mutationFn: ({ definitionId, name }) =>
      api.post<WorkflowDefinitionDetail>(`/api/workflows/${definitionId}/duplicate`, { name }),
    onSuccess: (detail) => {
      queryClient.setQueryData(workflowKeys.definition(detail.summary.id), detail)
      void queryClient.invalidateQueries({ queryKey: workflowKeys.definitions })
    },
  })
}

export function useDeleteWorkflow() {
  const queryClient = useQueryClient()

  return useMutation<void, ApiError, string>({
    mutationFn: (definitionId) => api.delete<void>(`/api/workflows/${definitionId}`),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: workflowKeys.definitions })
    },
  })
}

/** Runs the server-side validator against the stored definition and returns what it found. */
export function useValidateWorkflow(definitionId: string) {
  const queryClient = useQueryClient()

  return useMutation<WorkflowValidationIssue[], ApiError, void>({
    mutationFn: () => api.post<WorkflowValidationIssue[]>(`/api/workflows/${definitionId}/test`),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: workflowKeys.definition(definitionId) })
    },
  })
}

export function useStartWorkflow() {
  const queryClient = useQueryClient()

  return useMutation<WorkflowInstanceDetail, ApiError, { definitionId: string; documentId?: string }>({
    mutationFn: (input) => api.post<WorkflowInstanceDetail>('/api/workflows/instances', input),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: workflowKeys.all })
      void queryClient.invalidateQueries({ queryKey: ['documents'] })
      void queryClient.invalidateQueries({ queryKey: ['tasks'] })
    },
  })
}

export function useCancelWorkflow(instanceId: string) {
  const queryClient = useQueryClient()

  return useMutation<WorkflowInstanceDetail, ApiError, string>({
    mutationFn: (reason) =>
      api.post<WorkflowInstanceDetail>(`/api/workflows/instances/${instanceId}/cancel`, { reason }),
    onSuccess: (detail) => {
      queryClient.setQueryData(workflowKeys.instance(instanceId), detail)
      void queryClient.invalidateQueries({ queryKey: workflowKeys.all })
      void queryClient.invalidateQueries({ queryKey: ['tasks'] })
    },
  })
}

// ---------------------------------------------------------------------------------------------
// Tasks
// ---------------------------------------------------------------------------------------------

export function useTasks(filters: TaskFilters) {
  return useQuery<PagedResult<TaskListItem>, ApiError>({
    queryKey: workflowKeys.tasks(filters),
    queryFn: () => {
      const params = new URLSearchParams({
        scope: filters.scope,
        page: String(filters.page),
        pageSize: String(filters.pageSize),
      })

      filters.statuses.forEach((status) => params.append('statuses', String(status)))
      if (filters.onlyOverdue) params.set('onlyOverdue', 'true')

      return api.get<PagedResult<TaskListItem>>(`/api/tasks?${params}`)
    },
    placeholderData: (previous) => previous,
  })
}

export function useTaskStatistics(scope: string) {
  return useQuery<TaskStatistics, ApiError>({
    queryKey: workflowKeys.taskStatistics(scope),
    queryFn: () => api.get<TaskStatistics>(`/api/tasks/statistics?scope=${scope}`),
  })
}

const actionEndpoints: Record<Exclude<TaskAction, 'reassign'>, string> = {
  approve: 'approve',
  reject: 'reject',
  complete: 'complete',
  'request-info': 'request-information',
  claim: 'claim',
}

export function useTaskAction() {
  const queryClient = useQueryClient()

  return useMutation<
    TaskListItem,
    ApiError,
    { taskId: string; action: Exclude<TaskAction, 'reassign'>; comment?: string }
  >({
    mutationFn: ({ taskId, action, comment }) =>
      api.post<TaskListItem>(`/api/tasks/${taskId}/${actionEndpoints[action]}`, { comment }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['tasks'] })
      void queryClient.invalidateQueries({ queryKey: workflowKeys.all })
      void queryClient.invalidateQueries({ queryKey: ['documents'] })
      void queryClient.invalidateQueries({ queryKey: workflowKeys.unreadCount })
    },
  })
}

export function useReassignTask() {
  const queryClient = useQueryClient()

  return useMutation<TaskListItem, ApiError, { taskId: string; toUserId: string; comment?: string }>({
    mutationFn: ({ taskId, toUserId, comment }) =>
      api.post<TaskListItem>(`/api/tasks/${taskId}/reassign`, { toUserId, comment }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['tasks'] })
      void queryClient.invalidateQueries({ queryKey: workflowKeys.all })
    },
  })
}

// ---------------------------------------------------------------------------------------------
// Notifications
// ---------------------------------------------------------------------------------------------

export function useNotifications(unreadOnly = false) {
  return useQuery<Notification[], ApiError>({
    queryKey: [...workflowKeys.notifications, unreadOnly],
    queryFn: () => api.get<Notification[]>(`/api/notifications?unreadOnly=${unreadOnly}&limit=15`),
  })
}

export function useUnreadNotificationCount() {
  return useQuery<number, ApiError>({
    queryKey: workflowKeys.unreadCount,
    queryFn: () => api.get<number>('/api/notifications/unread-count'),
    refetchInterval: 60_000,
  })
}

export function useMarkNotificationsRead() {
  const queryClient = useQueryClient()

  return useMutation<void, ApiError, string | 'all'>({
    mutationFn: (id) =>
      id === 'all'
        ? api.post<void>('/api/notifications/read-all')
        : api.post<void>(`/api/notifications/${id}/read`),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: workflowKeys.notifications })
      void queryClient.invalidateQueries({ queryKey: workflowKeys.unreadCount })
    },
  })
}
