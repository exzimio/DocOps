import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type ApiError } from '@/lib/api'

/** Espelha `MetadataFieldType` do domínio. O servidor envia o número, não o nome. */
export const FieldType = {
  text: 0,
  number: 1,
  date: 2,
  boolean: 3,
  currency: 4,
  select: 5,
} as const

export type FieldTypeValue = (typeof FieldType)[keyof typeof FieldType]

export interface AdminDocumentType {
  id: string
  code: string
  name: string
  description: string | null
  icon: string | null
  referencePrefix: string
  defaultClassification: number
  defaultRetentionPolicyId: string | null
  defaultRetentionPolicyName: string | null
  isActive: boolean
  fieldCount: number
  documentCount: number
  /** O tipo por onde entra um ficheiro antes de alguém o ter lido. Não se desativa. */
  isIntakeType: boolean
}

export interface AdminMetadataField {
  id: string
  documentTypeId: string
  code: string
  label: string
  fieldType: FieldTypeValue
  isRequired: boolean
  displayOrder: number
  helpText: string | null
  options: string[]
  /** Quantos documentos já têm valor neste campo. É o que explica as recusas. */
  valueCount: number
  canChangeType: boolean
  canDelete: boolean
}

export interface AdminRetentionPolicy {
  id: string
  code: string
  name: string
  retentionMonths: number
  trigger: number
}

export interface SaveDocumentTypeInput {
  code: string
  name: string
  description?: string
  referencePrefix: string
  defaultClassification: number
  defaultRetentionPolicyId?: string | null
}

export interface SaveFieldInput {
  code: string
  label: string
  fieldType: FieldTypeValue
  isRequired: boolean
  displayOrder: number
  helpText?: string
  options: string[]
}

export const catalogueKeys = {
  all: ['admin', 'catalogue'] as const,
  types: (includeInactive: boolean) => ['admin', 'catalogue', 'types', includeInactive] as const,
  fields: (typeId: string) => ['admin', 'catalogue', 'fields', typeId] as const,
  retention: ['admin', 'catalogue', 'retention'] as const,
}

export function useDocumentTypes(includeInactive: boolean) {
  return useQuery<AdminDocumentType[], ApiError>({
    queryKey: catalogueKeys.types(includeInactive),
    queryFn: () =>
      api.get<AdminDocumentType[]>(`/api/admin/document-types?includeInactive=${includeInactive}`),
  })
}

export function useTypeFields(typeId: string | null) {
  return useQuery<AdminMetadataField[], ApiError>({
    queryKey: catalogueKeys.fields(typeId ?? ''),
    queryFn: () => api.get<AdminMetadataField[]>(`/api/admin/document-types/${typeId}/fields`),
    enabled: typeId !== null,
  })
}

export function useRetentionPolicies() {
  return useQuery<AdminRetentionPolicy[], ApiError>({
    queryKey: catalogueKeys.retention,
    queryFn: () => api.get<AdminRetentionPolicy[]>('/api/admin/retention-policies'),
    staleTime: 5 * 60_000,
  })
}

function useCatalogueMutation<TInput, TResult>(run: (input: TInput) => Promise<TResult>) {
  const queryClient = useQueryClient()

  return useMutation<TResult, ApiError, TInput>({
    mutationFn: run,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: catalogueKeys.all }),
  })
}

export function useSaveDocumentType() {
  return useCatalogueMutation<{ id?: string; input: SaveDocumentTypeInput }, AdminDocumentType>(
    ({ id, input }) =>
      id
        ? api.put<AdminDocumentType>(`/api/admin/document-types/${id}`, input)
        : api.post<AdminDocumentType>('/api/admin/document-types', input),
  )
}

export function useSetTypeActive() {
  return useCatalogueMutation<{ id: string; isActive: boolean }, AdminDocumentType>(({ id, isActive }) =>
    api.post<AdminDocumentType>(`/api/admin/document-types/${id}/active`, { isActive }),
  )
}

export function useSaveField() {
  return useCatalogueMutation<
    { typeId: string; fieldId?: string; input: SaveFieldInput },
    AdminMetadataField
  >(({ typeId, fieldId, input }) =>
    fieldId
      ? api.put<AdminMetadataField>(`/api/admin/document-types/${typeId}/fields/${fieldId}`, input)
      : api.post<AdminMetadataField>(`/api/admin/document-types/${typeId}/fields`, input),
  )
}

export function useDeleteField() {
  return useCatalogueMutation<{ typeId: string; fieldId: string }, void>(({ typeId, fieldId }) =>
    api.delete<void>(`/api/admin/document-types/${typeId}/fields/${fieldId}`),
  )
}
