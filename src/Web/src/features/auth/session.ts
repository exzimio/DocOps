import { useMutation, useQuery, useQueryClient, type UseQueryResult } from '@tanstack/react-query'
import { ApiError, api } from '@/lib/api'
import type { CurrentUser } from '@/lib/types'

export const sessionQueryKey = ['session'] as const

export function useSession(): UseQueryResult<CurrentUser | null, ApiError> {
  return useQuery<CurrentUser | null, ApiError>({
    queryKey: sessionQueryKey,
    queryFn: async () => {
      try {
        return await api.get<CurrentUser>('/api/auth/me')
      } catch (error) {
        // An absent or expired session is a normal state, not a failure to retry.
        if (error instanceof ApiError && error.isUnauthenticated) {
          return null
        }
        throw error
      }
    },
    retry: false,
    staleTime: 60_000,
  })
}

export interface LoginInput {
  email: string
  password: string
  rememberMe: boolean
}

export function useLogin() {
  const queryClient = useQueryClient()

  return useMutation<CurrentUser, ApiError, LoginInput>({
    mutationFn: (input) => api.post<CurrentUser>('/api/auth/login', input),
    onSuccess: (user) => {
      queryClient.setQueryData(sessionQueryKey, user)
    },
  })
}

export function useLogout() {
  const queryClient = useQueryClient()

  return useMutation<void, ApiError, void>({
    mutationFn: () => api.post<void>('/api/auth/logout'),
    onSettled: async () => {
      queryClient.setQueryData(sessionQueryKey, null)
      await queryClient.invalidateQueries()
    },
  })
}

export function hasPermission(user: CurrentUser | null | undefined, permission: string): boolean {
  return user?.permissions.includes(permission) ?? false
}
