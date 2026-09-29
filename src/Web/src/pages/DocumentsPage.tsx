import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { FilterX, Plus, Search } from 'lucide-react'
import clsx from 'clsx'
import { api, type ApiError } from '@/lib/api'
import type { Department } from '@/lib/types'
import { Permission } from '@/lib/types'
import { hasPermission, useSession } from '@/features/auth/session'
import { useDocuments, useDocumentTypes } from '@/features/documents/api'
import { UploadDialog } from '@/features/documents/UploadDialog'
import { ClassificationBadge, StatusBadge } from '@/features/documents/presentation'
import { formatBytes, formatDate } from '@/features/documents/format'
import {
  DocumentStatus,
  defaultFilters,
  type DocumentFilters,
  type DocumentStatusValue,
} from '@/features/documents/types'
import { Button } from '@/components/ui/Button'
import { Badge, Card, EmptyState, ErrorState, LoadingState, TableScroller } from '@/components/ui/Surface'

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

export function DocumentsPage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const { data: user } = useSession()

  const [filters, setFilters] = useState<DocumentFilters>(defaultFilters)
  const [searchText, setSearchText] = useState('')
  const [uploadOpen, setUploadOpen] = useState(false)

  // Debounced: typing must not fire a request per keystroke. Returning the same object when the
  // text has not changed keeps the query key stable, so mounting the page costs one request.
  useEffect(() => {
    const timer = setTimeout(() => {
      setFilters((current) =>
        current.query === searchText ? current : { ...current, query: searchText, page: 1 },
      )
    }, 300)

    return () => clearTimeout(timer)
  }, [searchText])

  const documentsQuery = useDocuments(filters)
  const typesQuery = useDocumentTypes()

  const departmentsQuery = useQuery<Department[], ApiError>({
    queryKey: ['tenant', 'departments'],
    queryFn: () => api.get<Department[]>('/api/tenants/current/departments'),
    staleTime: 5 * 60_000,
  })

  const canCreate = hasPermission(user, Permission.documentsCreate)

  const filtersActive = useMemo(
    () =>
      filters.query !== '' ||
      filters.typeIds.length > 0 ||
      filters.statuses.length > 0 ||
      filters.departmentIds.length > 0 ||
      filters.onlyMine ||
      !filters.includeArchived,
    [filters],
  )

  const toggleStatus = (status: DocumentStatusValue) =>
    setFilters((current) => ({
      ...current,
      page: 1,
      statuses: current.statuses.includes(status)
        ? current.statuses.filter((value) => value !== status)
        : [...current.statuses, status],
    }))

  const resetFilters = () => {
    setSearchText('')
    setFilters(defaultFilters)
  }

  const result = documentsQuery.data

  return (
    <div className="mx-auto flex max-w-[95rem] flex-col gap-4">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-lg font-semibold text-ink">{t('documents.title')}</h1>
          <p className="text-sm text-ink-muted">
            {t('documents.subtitle', { tenant: user?.tenantName ?? '' })}
          </p>
        </div>

        {canCreate && (
          <Button onClick={() => setUploadOpen(true)}>
            <Plus aria-hidden className="size-4" />
            {t('documents.upload')}
          </Button>
        )}
      </header>

      <Card className="p-3">
        <div className="flex flex-col gap-3">
          <div className="flex flex-wrap items-center gap-2">
            <div className="relative min-w-[16rem] flex-1">
              <Search
                aria-hidden
                className="pointer-events-none absolute left-2.5 top-2.5 size-4 text-ink-muted"
              />
              <input
                type="search"
                value={searchText}
                onChange={(event) => setSearchText(event.target.value)}
                placeholder={t('documents.searchPlaceholder')}
                aria-label={t('documents.searchPlaceholder')}
                className="h-9 w-full rounded-md border border-strong bg-raised pl-8 pr-3 text-sm text-ink placeholder:text-ink-muted"
              />
            </div>

            <select
              value={filters.typeIds[0] ?? ''}
              onChange={(event) =>
                setFilters((current) => ({
                  ...current,
                  page: 1,
                  typeIds: event.target.value ? [event.target.value] : [],
                }))
              }
              aria-label={t('documents.type')}
              className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
            >
              <option value="">{t('documents.type')}: —</option>
              {typesQuery.data?.map((type) => (
                <option key={type.id} value={type.id}>
                  {type.name}
                </option>
              ))}
            </select>

            <select
              value={filters.departmentIds[0] ?? ''}
              onChange={(event) =>
                setFilters((current) => ({
                  ...current,
                  page: 1,
                  departmentIds: event.target.value ? [event.target.value] : [],
                }))
              }
              aria-label={t('documents.department')}
              className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
            >
              <option value="">{t('documents.department')}: —</option>
              {departmentsQuery.data?.map((department) => (
                <option key={department.id} value={department.id}>
                  {department.name}
                </option>
              ))}
            </select>

            <label className="flex items-center gap-1.5 text-xs text-ink-secondary">
              <input
                type="checkbox"
                checked={filters.onlyMine}
                onChange={(event) =>
                  setFilters((current) => ({ ...current, page: 1, onlyMine: event.target.checked }))
                }
                className="size-4 accent-teal-700"
              />
              {t('documents.onlyMine')}
            </label>

            <label className="flex items-center gap-1.5 text-xs text-ink-secondary">
              <input
                type="checkbox"
                checked={filters.includeArchived}
                onChange={(event) =>
                  setFilters((current) => ({ ...current, page: 1, includeArchived: event.target.checked }))
                }
                className="size-4 accent-teal-700"
              />
              {t('documents.includeArchived')}
            </label>

            {filtersActive && (
              <Button variant="ghost" size="sm" onClick={resetFilters}>
                <FilterX aria-hidden className="size-4" />
                {t('documents.clearFilters')}
              </Button>
            )}
          </div>

          <div className="flex flex-wrap gap-1.5">
            {statusOptions.map((status) => {
              const active = filters.statuses.includes(status)

              return (
                <button
                  key={status}
                  type="button"
                  aria-pressed={active}
                  onClick={() => toggleStatus(status)}
                  className={clsx(
                    'rounded-xs border px-2 py-0.5 text-[11px] font-medium transition-colors',
                    active
                      ? 'border-teal-600 bg-teal-50 text-teal-800 dark:bg-teal-950 dark:text-teal-200'
                      : 'border-subtle text-ink-secondary hover:bg-sunken',
                  )}
                >
                  {t(`documents.statusValues.${status}`)}
                </button>
              )
            })}
          </div>
        </div>
      </Card>

      <Card>
        {documentsQuery.isLoading && <LoadingState label={t('common.loading')} />}

        {documentsQuery.isError && (
          <ErrorState
            title={t('errors.genericTitle')}
            body={documentsQuery.error.message}
            correlationId={documentsQuery.error.problem.correlationId}
            action={
              <Button variant="secondary" size="sm" onClick={() => void documentsQuery.refetch()}>
                {t('common.retry')}
              </Button>
            }
          />
        )}

        {result && result.items.length === 0 && (
          <EmptyState title={t('documents.empty')} body={t('documents.emptyBody')} />
        )}

        {result && result.items.length > 0 && (
          <>
            <TableScroller label={t('documents.title')}>
              <table className="w-full min-w-[60rem] text-left text-sm">
                <thead className="bg-sunken text-[11px] uppercase tracking-wide text-ink-muted">
                  <tr>
                    <th scope="col" className="px-3 py-2 font-medium">
                      {t('documents.reference')}
                    </th>
                    <th scope="col" className="px-3 py-2 font-medium">
                      {t('documents.documentTitle')}
                    </th>
                    <th scope="col" className="px-3 py-2 font-medium">
                      {t('documents.type')}
                    </th>
                    <th scope="col" className="px-3 py-2 font-medium">
                      {t('documents.status')}
                    </th>
                    <th scope="col" className="px-3 py-2 font-medium">
                      {t('documents.classification')}
                    </th>
                    <th scope="col" className="px-3 py-2 font-medium">
                      {t('documents.owner')}
                    </th>
                    <th scope="col" className="px-3 py-2 text-right font-medium">
                      {t('documents.version')}
                    </th>
                    <th scope="col" className="px-3 py-2 text-right font-medium">
                      {t('documents.size')}
                    </th>
                    <th scope="col" className="px-3 py-2 text-right font-medium">
                      {t('documents.created')}
                    </th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-subtle">
                  {result.items.map((document) => (
                    <tr key={document.id} className="hover:bg-sunken">
                      <td className="whitespace-nowrap px-3 py-2 font-mono text-xs text-ink-secondary">
                        <Link
                          to={`/app/documents/${document.id}`}
                          className="hover:text-teal-700 hover:underline dark:hover:text-teal-300"
                        >
                          {document.reference}
                        </Link>
                      </td>
                      <td className="max-w-[28rem] px-3 py-2">
                        <Link
                          to={`/app/documents/${document.id}`}
                          className="block truncate font-medium text-ink hover:text-teal-700 hover:underline dark:hover:text-teal-300"
                          title={document.title}
                        >
                          {document.title}
                        </Link>
                        {document.tags.length > 0 && (
                          <span className="mt-0.5 flex gap-1 overflow-hidden whitespace-nowrap">
                            {document.tags.slice(0, 3).map((tag) => (
                              <Badge key={tag.id}>{tag.name}</Badge>
                            ))}
                            {document.tags.length > 3 && (
                              <span className="text-[11px] text-ink-muted">+{document.tags.length - 3}</span>
                            )}
                          </span>
                        )}
                      </td>
                      <td className="whitespace-nowrap px-3 py-2 text-xs text-ink-secondary">
                        {document.typeName}
                      </td>
                      <td className="whitespace-nowrap px-3 py-2">
                        <StatusBadge status={document.status} />
                      </td>
                      <td className="whitespace-nowrap px-3 py-2">
                        <ClassificationBadge value={document.classification} />
                      </td>
                      <td className="whitespace-nowrap px-3 py-2 text-xs text-ink-secondary">
                        {document.ownerName}
                        {document.departmentCode && (
                          <span className="ml-1 text-ink-muted">· {document.departmentCode}</span>
                        )}
                      </td>
                      <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums text-ink-secondary">
                        v{document.currentVersionNumber}
                      </td>
                      <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums text-xs text-ink-secondary">
                        {formatBytes(document.fileSizeBytes)}
                      </td>
                      <td className="whitespace-nowrap px-3 py-2 text-right text-xs text-ink-secondary">
                        {formatDate(document.createdAtUtc, i18n.language)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </TableScroller>

            <div className="flex flex-wrap items-center justify-between gap-2 border-t border-subtle px-3 py-2">
              <p className="text-xs text-ink-muted">
                {t('documents.resultsCount', { count: result.totalCount })} ·{' '}
                {t('documents.page', { page: result.page, total: result.totalPages })}
              </p>

              <div className="flex gap-2">
                <Button
                  variant="secondary"
                  size="sm"
                  disabled={!result.hasPrevious}
                  onClick={() => setFilters((current) => ({ ...current, page: current.page - 1 }))}
                >
                  {t('documents.previous')}
                </Button>
                <Button
                  variant="secondary"
                  size="sm"
                  disabled={!result.hasNext}
                  onClick={() => setFilters((current) => ({ ...current, page: current.page + 1 }))}
                >
                  {t('documents.next')}
                </Button>
              </div>
            </div>
          </>
        )}
      </Card>

      {uploadOpen && (
        <UploadDialog
          departments={departmentsQuery.data ?? []}
          onClose={() => setUploadOpen(false)}
          onCreated={(documentId) => {
            setUploadOpen(false)
            navigate(`/app/documents/${documentId}`)
          }}
        />
      )}
    </div>
  )
}
