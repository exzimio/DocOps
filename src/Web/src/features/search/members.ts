/** Mirrors `TenantMemberDto`: who works here, as a filter needs them. */
export interface TenantMember {
  id: string
  displayName: string
  departmentCode: string | null
}
