import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type ApiError } from '@/lib/api'
import { documentKeys } from '@/features/documents/api'
import type { SignatureRequest } from './types'

export const signatureKeys = {
  all: ['signatures'] as const,
  forDocument: (documentId: string) => ['signatures', 'document', documentId] as const,
  mine: ['signatures', 'mine'] as const,
}

export function useDocumentSignatures(documentId: string) {
  return useQuery<SignatureRequest[], ApiError>({
    queryKey: signatureKeys.forDocument(documentId),
    queryFn: () => api.get<SignatureRequest[]>(`/api/documents/${documentId}/signatures`),
  })
}

export function useMySignatures(enabled: boolean) {
  return useQuery<SignatureRequest[], ApiError>({
    queryKey: signatureKeys.mine,
    queryFn: () => api.get<SignatureRequest[]>('/api/signatures/mine'),
    enabled,
  })
}

/**
 * Signing changes the document's status, so the document itself is invalidated alongside the
 * signature list — otherwise the header would keep saying "awaiting signature" over a signed one.
 */
function useSignatureMutation<TInput>(documentId: string, run: (input: TInput) => Promise<SignatureRequest>) {
  const queryClient = useQueryClient()

  return useMutation<SignatureRequest, ApiError, TInput>({
    mutationFn: run,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: signatureKeys.all })
      void queryClient.invalidateQueries({ queryKey: documentKeys.detail(documentId) })
    },
  })
}

export interface RequestSignatureInput {
  signerUserId: string
  reason?: string
}

export function useRequestSignature(documentId: string) {
  return useSignatureMutation<RequestSignatureInput>(documentId, (input) =>
    api.post<SignatureRequest>(`/api/documents/${documentId}/signatures`, input),
  )
}

export function useSignDocument(documentId: string) {
  return useSignatureMutation<string>(documentId, (requestId) =>
    api.post<SignatureRequest>(`/api/signatures/${requestId}/sign`),
  )
}

export function useDeclineSignature(documentId: string) {
  return useSignatureMutation<{ requestId: string; reason: string }>(documentId, ({ requestId, reason }) =>
    api.post<SignatureRequest>(`/api/signatures/${requestId}/decline`, { reason }),
  )
}

export function useCancelSignature(documentId: string) {
  return useSignatureMutation<string>(documentId, (requestId) =>
    api.post<SignatureRequest>(`/api/signatures/${requestId}/cancel`),
  )
}
