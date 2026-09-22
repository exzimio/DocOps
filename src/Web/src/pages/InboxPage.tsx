import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { Paperclip, RefreshCw } from 'lucide-react'
import clsx from 'clsx'
import { api, type ApiError } from '@/lib/api'
import type { Department } from '@/lib/types'
import { Permission } from '@/lib/types'
import { hasPermission, useSession } from '@/features/auth/session'
import { useInboxMessages, useInboxSummary, useMailConnector, useSyncInbox } from '@/features/inbox/api'
import { EmailDetailPanel } from '@/features/inbox/EmailDetailPanel'
import { ConnectorNotice, ImportanceBadge, StatusBadge } from '@/features/inbox/presentation'
import { formatDateTime } from '@/features/documents/format'
import {
  EmailStatus,
  defaultInboxFilters,
  type EmailStatusValue,
  type InboxFilters,
} from '@/features/inbox/types'
import { Button } from '@/components/ui/Button'
import { Checkbox } from '@/components/ui/Field'
import { Badge, Card, EmptyState, ErrorState, LoadingState } from '@/components/ui/Surface'

const statusFilters: EmailStatusValue[] = [
  EmailStatus.Unread,
  EmailStatus.Read,
  EmailStatus.Processed,
  EmailStatus.Archived,
]

export function InboxPage() {
  const { t, i18n } = useTranslation()
  const { data: user } = useSession()

  const [filters, setFilters] = useState<InboxFilters>(defaultInboxFilters)
  const [searchText, setSearchText] = useState('')
  const [selectedId, setSelectedId] = useState<string | null>(null)

  useEffect(() => {
    const timer = setTimeout(() => {
      setFilters((current) =>
        current.query === searchText ? current : { ...current, query: searchText, page: 1 },
      )
    }, 300)

    return () => clearTimeout(timer)
  }, [searchText])

  const messagesQuery = useInboxMessages(filters)
  const summaryQuery = useInboxSummary()
  const connectorQuery = useMailConnector()
  const syncInbox = useSyncInbox()

  const departmentsQuery = useQuery<Department[], ApiError>({
    queryKey: ['tenant', 'departments'],
    queryFn: () => api.get<Department[]>('/api/tenants/current/departments'),
    staleTime: 5 * 60_000,
  })

  const canProcess = hasPermission(user, Permission.inboxProcess)
  const result = messagesQuery.data

  // Picks a default only when nothing is open yet, or when the list has become empty. It
  // deliberately does not chase a selection out of the current filter: archiving or processing the
  // open message changes its own status, which can drop it out of the active filter chips, and
  // jumping the reading pane to an unrelated message right after the user's own action would be a
  // surprise, not a convenience. The detail panel fetches by id on its own, so it keeps showing the
  // message the user was looking at — with its new state — until they pick another one.
  useEffect(() => {
    if (!result || result.items.length === 0) {
      setSelectedId(null)
      return
    }

    if (!selectedId) {
      setSelectedId(result.items[0]?.id ?? null)
    }
    // Only re-run when the page of results itself changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [result])

  const toggleStatus = (status: EmailStatusValue) =>
    setFilters((current) => ({
      ...current,
      page: 1,
      statuses: current.statuses.includes(status)
        ? current.statuses.filter((value) => value !== status)
        : [...current.statuses, status],
    }))

  return (
    <div className="mx-auto flex max-w-[95rem] flex-col gap-4">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-lg font-semibold text-ink">{t('inbox.title')}</h1>
          <p className="text-sm text-ink-muted">{t('inbox.subtitle')}</p>
        </div>

        {canProcess && (
          <Button variant="secondary" onClick={() => syncInbox.mutate()} loading={syncInbox.isPending}>
            <RefreshCw aria-hidden className="size-4" />
            {t('inbox.sync')}
          </Button>
        )}
      </header>

      {connectorQuery.data && (
        <ConnectorNotice
          source={connectorQuery.data.source}
          name={connectorQuery.data.name}
          description={connectorQuery.data.description}
        />
      )}

      {syncInbox.data && (
        <p className="text-xs text-ink-muted">
          {t('inbox.syncResult', {
            added: syncInbox.data.added,
            fetched: syncInbox.data.fetched,
            date: formatDateTime(syncInbox.data.ranAtUtc, i18n.language),
          })}
        </p>
      )}

      {summaryQuery.data && (
        <div className="flex flex-wrap gap-2 text-xs text-ink-secondary">
          <span>{t('inbox.summary.unread', { count: summaryQuery.data.unread })}</span>
          <span>·</span>
          <span>{t('inbox.summary.open', { count: summaryQuery.data.open })}</span>
          <span>·</span>
          <span>{t('inbox.summary.processed', { count: summaryQuery.data.processed })}</span>
          <span>·</span>
          <span>{t('inbox.summary.archived', { count: summaryQuery.data.archived })}</span>
        </div>
      )}

      <div className="grid gap-4 lg:grid-cols-[22rem_1fr]">
        <div className="flex flex-col gap-3">
          <Card className="p-3">
            <div className="flex flex-col gap-2.5">
              <input
                type="search"
                value={searchText}
                onChange={(event) => setSearchText(event.target.value)}
                placeholder={t('inbox.searchPlaceholder')}
                aria-label={t('inbox.searchPlaceholder')}
                className="h-9 w-full rounded-md border border-strong bg-raised px-3 text-sm text-ink placeholder:text-ink-muted"
              />

              <div className="flex flex-wrap gap-1.5">
                {statusFilters.map((status) => {
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
                      {t(`inbox.statusValues.${status}`)}
                    </button>
                  )
                })}
              </div>

              <Checkbox
                label={t('inbox.onlyWithAttachments')}
                checked={filters.onlyWithAttachments}
                onChange={(event) =>
                  setFilters((current) => ({
                    ...current,
                    page: 1,
                    onlyWithAttachments: event.target.checked,
                  }))
                }
              />
            </div>
          </Card>

          <Card className="flex-1 overflow-hidden">
            {messagesQuery.isLoading && <LoadingState label={t('common.loading')} />}

            {messagesQuery.isError && (
              <ErrorState
                title={t('errors.genericTitle')}
                body={messagesQuery.error.message}
                correlationId={messagesQuery.error.problem.correlationId}
                action={
                  <Button variant="secondary" size="sm" onClick={() => void messagesQuery.refetch()}>
                    {t('common.retry')}
                  </Button>
                }
              />
            )}

            {result && result.items.length === 0 && (
              <EmptyState title={t('inbox.empty')} body={t('inbox.emptyBody')} />
            )}

            {result && result.items.length > 0 && (
              <>
                <ul
                  aria-label={t('inbox.messagesLabel')}
                  className="max-h-[48rem] divide-y divide-subtle overflow-y-auto"
                >
                  {result.items.map((item) => (
                    <li key={item.id}>
                      <button
                        type="button"
                        onClick={() => setSelectedId(item.id)}
                        aria-current={selectedId === item.id}
                        className={clsx(
                          'flex w-full flex-col gap-1 px-3 py-2.5 text-left transition-colors hover:bg-sunken',
                          selectedId === item.id && 'bg-sunken',
                          item.status === EmailStatus.Unread && 'font-semibold',
                        )}
                      >
                        <div className="flex items-center justify-between gap-2">
                          <span className="truncate text-sm text-ink">{item.fromName}</span>
                          <span className="shrink-0 text-[11px] text-ink-muted">
                            {formatDateTime(item.receivedAtUtc, i18n.language)}
                          </span>
                        </div>
                        <span className="truncate text-xs text-ink-secondary">{item.subject}</span>
                        <div className="flex flex-wrap items-center gap-1.5">
                          <StatusBadge status={item.status} />
                          <ImportanceBadge importance={item.importance} />
                          {item.attachmentCount > 0 && (
                            <Badge>
                              <Paperclip aria-hidden className="mr-0.5 size-3" />
                              {item.attachmentCount}
                            </Badge>
                          )}
                        </div>
                      </button>
                    </li>
                  ))}
                </ul>

                <div className="flex items-center justify-between gap-2 border-t border-subtle px-3 py-2">
                  <p className="text-xs text-ink-muted">
                    {t('documents.resultsCount', { count: result.totalCount })}
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
        </div>

        <div>
          {selectedId ? (
            <EmailDetailPanel emailId={selectedId} departments={departmentsQuery.data ?? []} />
          ) : (
            <Card>
              <EmptyState title={t('inbox.selectTitle')} body={t('inbox.selectBody')} />
            </Card>
          )}
        </div>
      </div>
    </div>
  )
}
