import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { FilterX } from 'lucide-react'
import clsx from 'clsx'
import { api, type ApiError } from '@/lib/api'
import type { Department } from '@/lib/types'
import type { TenantMember } from '@/features/search/members'
import { useDocumentTypes, useTags } from '@/features/documents/api'
import {
  DocumentStatus,
  SecurityClassification,
  type DocumentStatusValue,
  type SecurityClassificationValue,
} from '@/features/documents/types'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Surface'
import {
  ApprovalState,
  WorkflowState,
  type ApprovalStateValue,
  type SearchFilters,
  type SearchSortField,
  type WorkflowStateValue,
} from './types'

const statusOptions: DocumentStatusValue[] = [
  DocumentStatus.Draft,
  DocumentStatus.InReview,
  DocumentStatus.PendingApproval,
  DocumentStatus.Approved,
  DocumentStatus.Rejected,
  DocumentStatus.SignatureRequired,
  DocumentStatus.Signed,
  DocumentStatus.Archived,
]

const classificationOptions: SecurityClassificationValue[] = [
  SecurityClassification.Public,
  SecurityClassification.Internal,
  SecurityClassification.Confidential,
  SecurityClassification.Restricted,
]

const workflowOptions: WorkflowStateValue[] = [
  WorkflowState.None,
  WorkflowState.InProgress,
  WorkflowState.Completed,
  WorkflowState.Rejected,
  WorkflowState.Cancelled,
  WorkflowState.Failed,
]

const approvalOptions: ApprovalStateValue[] = [
  ApprovalState.NotRequested,
  ApprovalState.Awaiting,
  ApprovalState.Approved,
  ApprovalState.Rejected,
  ApprovalState.InformationRequested,
]

const workflowNames = ['None', 'InProgress', 'Completed', 'Rejected', 'Cancelled', 'Failed']
const approvalNames = ['NotRequested', 'Awaiting', 'Approved', 'Rejected', 'InformationRequested']

const sortOptions: SearchSortField[] = [
  'Relevance',
  'CreatedAt',
  'ModifiedAt',
  'Title',
  'Reference',
  'Status',
]

interface SearchCriteriaProps {
  filters: SearchFilters
  onChange: (next: SearchFilters) => void
  onReset: () => void
  showReset: boolean
}

/**
 * Every filter the search understands, on screen. They are the same ones the server accepts, and the
 * server applies them whether or not they were offered here — what this panel does is make them
 * reachable, not make them true.
 */
export function SearchCriteria({ filters, onChange, onReset, showReset }: SearchCriteriaProps) {
  const { t } = useTranslation()
  const typesQuery = useDocumentTypes()
  const tagsQuery = useTags()

  const departmentsQuery = useQuery<Department[], ApiError>({
    queryKey: ['tenant', 'departments'],
    queryFn: () => api.get<Department[]>('/api/tenants/current/departments'),
    staleTime: 5 * 60_000,
  })

  const membersQuery = useQuery<TenantMember[], ApiError>({
    queryKey: ['tenant', 'members'],
    queryFn: () => api.get<TenantMember[]>('/api/tenants/current/users'),
    staleTime: 5 * 60_000,
  })

  const set = (patch: Partial<SearchFilters>) => onChange({ ...filters, ...patch, page: 1 })

  function toggle<T>(list: T[], value: T): T[] {
    return list.includes(value) ? list.filter((item) => item !== value) : [...list, value]
  }

  return (
    <Card className="p-3">
      <div className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <select
            value={filters.typeIds[0] ?? ''}
            onChange={(event) => set({ typeIds: event.target.value ? [event.target.value] : [] })}
            aria-label={t('documents.type')}
            className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
          >
            <option value="">{t('documents.type')}: -</option>
            {typesQuery.data?.map((type) => (
              <option key={type.id} value={type.id}>
                {type.name}
              </option>
            ))}
          </select>

          <select
            value={filters.departmentIds[0] ?? ''}
            onChange={(event) => set({ departmentIds: event.target.value ? [event.target.value] : [] })}
            aria-label={t('documents.department')}
            className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
          >
            <option value="">{t('documents.department')}: -</option>
            {departmentsQuery.data?.map((department) => (
              <option key={department.id} value={department.id}>
                {department.name}
              </option>
            ))}
          </select>

          <select
            value={filters.ownerUserId ?? ''}
            onChange={(event) => set({ ownerUserId: event.target.value || null })}
            aria-label={t('documents.owner')}
            className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
          >
            <option value="">{t('documents.owner')}: -</option>
            {membersQuery.data?.map((member) => (
              <option key={member.id} value={member.id}>
                {member.displayName}
                {member.departmentCode ? ` · ${member.departmentCode}` : ''}
              </option>
            ))}
          </select>

          <select
            value={filters.tagIds[0] ?? ''}
            onChange={(event) => set({ tagIds: event.target.value ? [event.target.value] : [] })}
            aria-label={t('documents.tags')}
            className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
          >
            <option value="">{t('documents.tags')}: -</option>
            {tagsQuery.data?.map((tag) => (
              <option key={tag.id} value={tag.id}>
                {tag.name}
              </option>
            ))}
          </select>

          {/* The two ends of one range, kept together: wrapping between them reads as two filters. */}
          <div className="flex items-center gap-2">
            <label className="flex items-center gap-1.5 text-xs text-ink-secondary">
              {t('search.createdFrom')}
              <input
                type="date"
                value={filters.createdFromUtc?.slice(0, 10) ?? ''}
                onChange={(event) =>
                  set({ createdFromUtc: event.target.value ? `${event.target.value}T00:00:00Z` : null })
                }
                className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
              />
            </label>

            <label className="flex items-center gap-1.5 text-xs text-ink-secondary">
              {t('search.createdTo')}
              <input
                type="date"
                value={filters.createdToUtc?.slice(0, 10) ?? ''}
                onChange={(event) =>
                  // The chosen day is included: someone picking today means "up to the end of today".
                  set({ createdToUtc: event.target.value ? `${event.target.value}T23:59:59Z` : null })
                }
                className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
              />
            </label>
          </div>

          <select
            value={filters.sortBy}
            onChange={(event) => set({ sortBy: event.target.value as SearchSortField })}
            aria-label={t('search.sortBy')}
            className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
          >
            {sortOptions.map((option) => (
              <option key={option} value={option}>
                {t('search.sortBy')}: {t(`search.sortValues.${option}`)}
              </option>
            ))}
          </select>

          <label className="flex items-center gap-1.5 text-xs text-ink-secondary">
            <input
              type="checkbox"
              checked={filters.onlyMine}
              onChange={(event) => set({ onlyMine: event.target.checked })}
              className="size-4 accent-teal-700"
            />
            {t('documents.onlyMine')}
          </label>

          <label className="flex items-center gap-1.5 text-xs text-ink-secondary">
            <input
              type="checkbox"
              checked={filters.includeArchived}
              onChange={(event) => set({ includeArchived: event.target.checked })}
              className="size-4 accent-teal-700"
            />
            {t('documents.includeArchived')}
          </label>

          {showReset && (
            <Button variant="ghost" size="sm" onClick={onReset}>
              <FilterX aria-hidden className="size-4" />
              {t('documents.clearFilters')}
            </Button>
          )}
        </div>

        <ChipRow label={t('documents.status')}>
          {statusOptions.map((status) => (
            <Chip
              key={status}
              active={filters.statuses.includes(status)}
              onClick={() => set({ statuses: toggle(filters.statuses, status) })}
            >
              {t(`documents.statusValues.${status}`)}
            </Chip>
          ))}
        </ChipRow>

        <ChipRow label={t('documents.classification')}>
          {classificationOptions.map((value) => (
            <Chip
              key={value}
              active={filters.classifications.includes(value)}
              onClick={() => set({ classifications: toggle(filters.classifications, value) })}
            >
              {t(`documents.classificationValues.${value}`)}
            </Chip>
          ))}
        </ChipRow>

        <ChipRow label={t('search.workflowState')}>
          {workflowOptions.map((value) => (
            <Chip
              key={value}
              active={filters.workflowStates.includes(value)}
              onClick={() => set({ workflowStates: toggle(filters.workflowStates, value) })}
            >
              {t(`search.workflowValues.${workflowNames[value]}`)}
            </Chip>
          ))}
        </ChipRow>

        <ChipRow label={t('search.approvalState')}>
          {approvalOptions.map((value) => (
            <Chip
              key={value}
              active={filters.approvalStates.includes(value)}
              onClick={() => set({ approvalStates: toggle(filters.approvalStates, value) })}
            >
              {t(`search.approvalValues.${approvalNames[value]}`)}
            </Chip>
          ))}
        </ChipRow>
      </div>
    </Card>
  )
}

function ChipRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-wrap items-center gap-1.5">
      <span className="w-28 shrink-0 text-[11px] uppercase tracking-wide text-ink-muted">{label}</span>
      {children}
    </div>
  )
}

function Chip({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onClick}
      className={clsx(
        'rounded-xs border px-2 py-0.5 text-[11px] font-medium transition-colors',
        active
          ? 'border-teal-600 bg-teal-50 text-teal-800 dark:bg-teal-950 dark:text-teal-200'
          : 'border-subtle text-ink-secondary hover:bg-sunken',
      )}
    >
      {children}
    </button>
  )
}
