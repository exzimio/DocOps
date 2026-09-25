import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type ApiError } from '@/lib/api'

export interface AdminDepartmentMembership {
  id: string
  name: string
  code: string
  isPrimary: boolean
}

export interface AdminUser {
  id: string
  email: string
  fullName: string
  jobTitle: string | null
  userType: number
  externalOrganisation: string | null
  isActive: boolean
  lastLoginAtUtc: string | null
  roles: string[]
  departments: AdminDepartmentMembership[]
  /** False na própria conta: ninguém se edita a si mesmo por aqui. */
  canEdit: boolean
  /** Conta que administra a plataforma acima desta organização — não editável, por outro motivo. */
  isPlatformAccount: boolean
}

export interface AdminRole {
  name: string
  description: string
  permissionCount: number
  permissions: string[]
}

export interface AdminDepartment {
  id: string
  name: string
  code: string
  description: string | null
  memberCount: number
}

export interface CreatedUser {
  user: AdminUser
  /** Mostrada uma vez. O servidor não a sabe devolver outra vez. */
  temporaryPassword: string
}

export const adminKeys = {
  all: ['admin'] as const,
  users: (includeInactive: boolean) => ['admin', 'users', includeInactive] as const,
  roles: ['admin', 'roles'] as const,
  departments: ['admin', 'departments'] as const,
}

export function useAdminUsers(includeInactive: boolean) {
  return useQuery<AdminUser[], ApiError>({
    queryKey: adminKeys.users(includeInactive),
    queryFn: () => api.get<AdminUser[]>(`/api/admin/users?includeInactive=${includeInactive}`),
  })
}

export function useAdminRoles() {
  return useQuery<AdminRole[], ApiError>({
    queryKey: adminKeys.roles,
    queryFn: () => api.get<AdminRole[]>('/api/admin/roles'),
    staleTime: 5 * 60_000,
  })
}

export function useAdminDepartments() {
  return useQuery<AdminDepartment[], ApiError>({
    queryKey: adminKeys.departments,
    queryFn: () => api.get<AdminDepartment[]>('/api/admin/departments'),
  })
}

function useAdminMutation<TInput, TResult>(run: (input: TInput) => Promise<TResult>) {
  const queryClient = useQueryClient()

  return useMutation<TResult, ApiError, TInput>({
    mutationFn: run,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: adminKeys.all }),
  })
}

export interface SaveUserInput {
  email?: string
  firstName: string
  lastName: string
  jobTitle?: string
  roles: string[]
  departmentIds: string[]
}

export function useCreateUser() {
  return useAdminMutation<SaveUserInput, CreatedUser>((input) =>
    api.post<CreatedUser>('/api/admin/users', input),
  )
}

export function useUpdateUser() {
  return useAdminMutation<{ id: string; input: SaveUserInput }, AdminUser>(({ id, input }) =>
    api.put<AdminUser>(`/api/admin/users/${id}`, input),
  )
}

export function useSetUserActive() {
  return useAdminMutation<{ id: string; isActive: boolean }, AdminUser>(({ id, isActive }) =>
    api.post<AdminUser>(`/api/admin/users/${id}/active`, { isActive }),
  )
}

export interface SaveDepartmentInput {
  name: string
  code: string
  description?: string
}

export function useSaveDepartment() {
  return useAdminMutation<{ id?: string; input: SaveDepartmentInput }, AdminDepartment>(({ id, input }) =>
    id
      ? api.put<AdminDepartment>(`/api/admin/departments/${id}`, input)
      : api.post<AdminDepartment>('/api/admin/departments', input),
  )
}
