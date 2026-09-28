import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type ApiError } from '@/lib/api'
import { catalogueKeys, type AdminRetentionPolicy } from './catalogue'

export interface AdminTag {
  id: string
  name: string
  colour: string | null
  /** Quantos documentos a têm. É o que deixa o ecrã dizer o custo antes de a remover. */
  documentCount: number
  isDeleted: boolean
}

export interface SaveTagInput {
  name: string
  colour?: string | null
}

export interface SaveRetentionPolicyInput {
  code: string
  name: string
  description?: string
  legalBasis?: string
  retentionMonths: number
  trigger: number
}

/** Espelha `RetentionTrigger` do domínio: o evento que arranca o relógio. */
export const RetentionTrigger = {
  onCreation: 0,
  onApproval: 1,
  onArchive: 2,
} as const

export const tagKeys = {
  all: ['admin', 'tags'] as const,
  list: (includeDeleted: boolean) => ['admin', 'tags', includeDeleted] as const,
}

export function useTags(includeDeleted: boolean) {
  return useQuery<AdminTag[], ApiError>({
    queryKey: tagKeys.list(includeDeleted),
    queryFn: () => api.get<AdminTag[]>(`/api/admin/tags?includeDeleted=${includeDeleted}`),
  })
}

function useTagMutation<TInput, TResult>(run: (input: TInput) => Promise<TResult>) {
  const queryClient = useQueryClient()

  return useMutation<TResult, ApiError, TInput>({
    mutationFn: run,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: tagKeys.all }),
  })
}

export function useSaveTag() {
  return useTagMutation<{ id?: string; input: SaveTagInput }, AdminTag>(({ id, input }) =>
    id ? api.put<AdminTag>(`/api/admin/tags/${id}`, input) : api.post<AdminTag>('/api/admin/tags', input),
  )
}

export function useDeleteTag() {
  return useTagMutation<string, void>((id) => api.delete<void>(`/api/admin/tags/${id}`))
}

export function useRestoreTag() {
  return useTagMutation<string, AdminTag>((id) => api.post<AdminTag>(`/api/admin/tags/${id}/restore`))
}

function useRetentionMutation<TInput, TResult>(run: (input: TInput) => Promise<TResult>) {
  const queryClient = useQueryClient()

  return useMutation<TResult, ApiError, TInput>({
    mutationFn: run,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: catalogueKeys.all }),
  })
}

export function useSaveRetentionPolicy() {
  return useRetentionMutation<{ id?: string; input: SaveRetentionPolicyInput }, AdminRetentionPolicy>(
    ({ id, input }) =>
      id
        ? api.put<AdminRetentionPolicy>(`/api/admin/retention-policies/${id}`, input)
        : api.post<AdminRetentionPolicy>('/api/admin/retention-policies', input),
  )
}

export function useSetPolicyActive() {
  return useRetentionMutation<{ id: string; isActive: boolean }, AdminRetentionPolicy>(({ id, isActive }) =>
    api.post<AdminRetentionPolicy>(`/api/admin/retention-policies/${id}/active`, { isActive }),
  )
}
