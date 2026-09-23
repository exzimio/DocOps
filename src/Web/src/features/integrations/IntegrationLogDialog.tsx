import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import { useIntegrationLog } from './api'
import { LogLevelBadge } from './presentation'
import { IntegrationLogLevel, type IntegrationCard, type IntegrationLogLevelValue } from './types'
import { formatDateTime } from '@/features/documents/format'
import { Button } from '@/components/ui/Button'
import { EmptyState, ErrorState, LoadingState } from '@/components/ui/Surface'

interface IntegrationLogDialogProps {
  integration: IntegrationCard
  onClose: () => void
}

const levelFilters: IntegrationLogLevelValue[] = [
  IntegrationLogLevel.Information,
  IntegrationLogLevel.Warning,
  IntegrationLogLevel.Error,
]

const PAGE_SIZE = 20

/**
 * The integration's own record — what it tried, what came of it, and when. Everything shown here is
 * something the adapter actually reported for a run that actually happened; there is no separate
 * "demo log" dressed up to look busy.
 */
export function IntegrationLogDialog({ integration, onClose }: IntegrationLogDialogProps) {
  const { t, i18n } = useTranslation()
  const [levels, setLevels] = useState<IntegrationLogLevelValue[]>([])
  const [page, setPage] = useState(1)

  const logQuery = useIntegrationLog(integration.id, { levels, page, pageSize: PAGE_SIZE })

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }

    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  const toggleLevel = (level: IntegrationLogLevelValue) => {
    setPage(1)
    setLevels((current) =>
      current.includes(level) ? current.filter((l) => l !== level) : [...current, level],
    )
  }

  const result = logQuery.data

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-graphite-950/50 p-4"
      role="dialog"
      aria-modal="true"
      aria-label={t('integrations.logTitle', { name: integration.name })}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <div className="mt-8 w-full min-w-0 max-w-3xl rounded-lg border border-subtle bg-raised shadow-overlay">
        <div className="flex items-center justify-between border-b border-subtle px-4 py-3">
          <h2 className="text-sm font-semibold text-ink">
            {t('integrations.logTitle', { name: integration.name })}
          </h2>
          <Button variant="ghost" size="sm" onClick={onClose} aria-label={t('common.close')}>
            <X aria-hidden className="size-4" />
          </Button>
        </div>

        <div className="flex flex-col gap-3 p-4">
          <div className="flex flex-wrap gap-1.5">
            {levelFilters.map((level) => {
              const active = levels.includes(level)

              return (
                <button
                  key={level}
                  type="button"
                  aria-pressed={active}
                  onClick={() => toggleLevel(level)}
                  className={`rounded-xs border px-2 py-0.5 text-[11px] font-medium transition-colors ${
                    active
                      ? 'border-teal-600 bg-teal-50 text-teal-800 dark:bg-teal-950 dark:text-teal-200'
                      : 'border-subtle text-ink-secondary hover:bg-sunken'
                  }`}
                >
                  {t(`integrations.logLevels.${level}`)}
                </button>
              )
            })}
          </div>

          {logQuery.isLoading && <LoadingState label={t('common.loading')} />}

          {logQuery.isError && (
            <ErrorState
              title={t('errors.genericTitle')}
              body={logQuery.error.message}
              correlationId={logQuery.error.problem.correlationId}
            />
          )}

          {result && result.items.length === 0 && (
            <EmptyState title={t('integrations.logEmpty')} body={t('integrations.logEmptyBody')} />
          )}

          {result && result.items.length > 0 && (
            <>
              <ul
                aria-label={t('integrations.logTitle', { name: integration.name })}
                className="flex flex-col gap-2"
              >
                {result.items.map((entry) => (
                  <li key={entry.id} className="rounded-md border border-subtle p-2.5">
                    <div className="flex flex-wrap items-center gap-2">
                      <LogLevelBadge level={entry.level} />
                      <span className="font-mono text-[11px] text-ink-muted">{entry.operation}</span>
                      <span className="text-[11px] text-ink-muted">
                        {formatDateTime(entry.occurredAtUtc, i18n.language)}
                      </span>
                      {entry.manual && (
                        <span className="text-[11px] text-ink-muted">· {t('integrations.manual')}</span>
                      )}
                      {entry.recordsProcessed > 0 && (
                        <span className="text-[11px] text-ink-muted">
                          · {t('integrations.recordsShort', { count: entry.recordsProcessed })}
                        </span>
                      )}
                    </div>
                    <p className="mt-1 break-words text-sm text-ink">{entry.summary}</p>
                    {entry.detail && (
                      <p className="mt-0.5 break-words text-xs text-ink-secondary">{entry.detail}</p>
                    )}
                  </li>
                ))}
              </ul>

              <div className="flex items-center justify-between gap-2 border-t border-subtle pt-2">
                <p className="text-xs text-ink-muted">
                  {t('documents.resultsCount', { count: result.totalCount })}
                </p>
                <div className="flex gap-2">
                  <Button
                    variant="secondary"
                    size="sm"
                    disabled={!result.hasPrevious}
                    onClick={() => setPage((current) => current - 1)}
                  >
                    {t('documents.previous')}
                  </Button>
                  <Button
                    variant="secondary"
                    size="sm"
                    disabled={!result.hasNext}
                    onClick={() => setPage((current) => current + 1)}
                  >
                    {t('documents.next')}
                  </Button>
                </div>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  )
}
