import { useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { FileSearch, Inbox, ListChecks } from 'lucide-react'
import { useDocumentTypes, useDocuments } from '@/features/documents/api'
import { defaultFilters, type DocumentFilters } from '@/features/documents/types'
import { formatDateTime } from '@/features/documents/format'
import { useAiProvider, useAiRuns, useAiStatistics, useProcessDocument } from '@/features/ai/api'
import { AiSourceNotice } from '@/features/ai/AiSourceNotice'
import { percent, runStatusTone } from '@/features/ai/presentation'
import { AiRunOrigin, AiRunStatus, type AiRunStatusValue } from '@/features/ai/types'
import { Button } from '@/components/ui/Button'
import {
  Badge,
  Card,
  CardBody,
  CardHeader,
  EmptyState,
  ErrorState,
  LoadingState,
  TableScroller,
} from '@/components/ui/Surface'

const UnclassifiedTypeCode = 'Unclassified'

const queueTabs: { status: AiRunStatusValue; labelKey: string }[] = [
  { status: AiRunStatus.AwaitingReview, labelKey: 'ai.center.tabs.review' },
  { status: AiRunStatus.Applied, labelKey: 'ai.center.tabs.applied' },
  { status: AiRunStatus.Discarded, labelKey: 'ai.center.tabs.discarded' },
  { status: AiRunStatus.Failed, labelKey: 'ai.center.tabs.failed' },
]

/**
 * The AI intelligence centre: what arrived and nobody has read yet, what the AI suggested and is
 * waiting for a person, and how often those suggestions turned out to be right. Everything is
 * limited, on the server, to documents the viewer can open.
 */
export function AiCenterPage() {
  const { t, i18n } = useTranslation()
  const providerQuery = useAiProvider()
  const statsQuery = useAiStatistics()
  const typesQuery = useDocumentTypes()

  const [status, setStatus] = useState<AiRunStatusValue>(AiRunStatus.AwaitingReview)
  const [page, setPage] = useState(1)
  const runsQuery = useAiRuns([status], page)

  const unclassifiedTypeId = typesQuery.data?.find((type) => type.code === UnclassifiedTypeCode)?.id

  const intakeFilters = useMemo<DocumentFilters>(
    () => ({
      ...defaultFilters,
      typeIds: unclassifiedTypeId ? [unclassifiedTypeId] : [],
      pageSize: 10,
    }),
    [unclassifiedTypeId],
  )

  const intakeQuery = useDocuments(intakeFilters)
  const intake = unclassifiedTypeId ? intakeQuery.data : undefined
  const stats = statsQuery.data

  return (
    <div className="mx-auto flex max-w-[95rem] flex-col gap-4">
      <header>
        <h1 className="text-lg font-semibold text-ink">{t('ai.center.title')}</h1>
        <p className="text-sm text-ink-muted">{t('ai.center.subtitle')}</p>
      </header>

      {providerQuery.data && (
        <AiSourceNotice source={providerQuery.data.source} providerName={providerQuery.data.name} />
      )}

      <section className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4" aria-label={t('ai.center.statistics')}>
        <Tile label={t('ai.center.awaiting')} value={stats?.awaitingReview} />
        <Tile label={t('ai.center.applied30')} value={stats?.appliedLast30Days} />
        <Tile
          label={t('ai.center.decided')}
          value={stats?.suggestionsDecided}
          detail={
            stats
              ? t('ai.center.decidedDetail', {
                  accepted: stats.suggestionsAccepted,
                  edited: stats.suggestionsEdited,
                  rejected: stats.suggestionsRejected,
                })
              : undefined
          }
        />
        <Tile
          label={t('ai.center.acceptanceRate')}
          value={stats?.acceptanceRate === null || stats === undefined ? '-' : percent(stats.acceptanceRate)}
          detail={t('ai.center.acceptanceRateDetail')}
        />
      </section>

      <Card>
        <CardHeader
          title={t('ai.center.intakeTitle')}
          description={t('ai.center.intakeDescription')}
          icon={<Inbox className="size-4" />}
          actions={intake ? <Badge>{intake.totalCount}</Badge> : undefined}
        />
        <CardBody className="p-0">
          {(typesQuery.isLoading || intakeQuery.isLoading) && <LoadingState label={t('common.loading')} />}

          {intake && intake.items.length === 0 && (
            <EmptyState title={t('ai.center.intakeEmpty')} body={t('ai.center.intakeEmptyBody')} />
          )}

          {intake && intake.items.length > 0 && (
            <ul className="divide-y divide-subtle">
              {intake.items.map((document) => (
                <IntakeRow
                  key={document.id}
                  id={document.id}
                  reference={document.reference}
                  title={document.title}
                  owner={document.ownerName}
                  createdAt={formatDateTime(document.createdAtUtc, i18n.language)}
                />
              ))}
            </ul>
          )}
        </CardBody>
      </Card>

      <Card>
        <CardHeader title={t('ai.center.queueTitle')} icon={<ListChecks className="size-4" />} />
        <div className="flex gap-1 border-b border-subtle px-3" role="tablist">
          {queueTabs.map((entry) => (
            <button
              key={entry.status}
              type="button"
              role="tab"
              aria-selected={status === entry.status}
              onClick={() => {
                setStatus(entry.status)
                setPage(1)
              }}
              className={
                status === entry.status
                  ? '-mb-px border-b-2 border-teal-600 px-3 py-2 text-sm font-medium text-ink'
                  : '-mb-px border-b-2 border-transparent px-3 py-2 text-sm text-ink-muted hover:text-ink'
              }
            >
              {t(entry.labelKey)}
            </button>
          ))}
        </div>

        {runsQuery.isLoading && <LoadingState label={t('common.loading')} />}
        {runsQuery.isError && <ErrorState title={t('errors.genericTitle')} body={runsQuery.error.message} />}

        {runsQuery.data && runsQuery.data.items.length === 0 && (
          <EmptyState title={t('ai.center.queueEmpty')} />
        )}

        {runsQuery.data && runsQuery.data.items.length > 0 && (
          <>
            <TableScroller label={t('ai.title')}>
              <table className="w-full min-w-[52rem] text-left text-sm">
                <thead className="bg-sunken text-[11px] uppercase tracking-wide text-ink-muted">
                  <tr>
                    <th scope="col" className="px-3 py-2 font-medium">
                      {t('ai.center.columns.document')}
                    </th>
                    <th scope="col" className="px-3 py-2 font-medium">
                      {t('ai.center.columns.suggestion')}
                    </th>
                    <th scope="col" className="px-3 py-2 text-right font-medium">
                      {t('ai.center.columns.fields')}
                    </th>
                    <th scope="col" className="px-3 py-2 font-medium">
                      {t('ai.center.columns.origin')}
                    </th>
                    <th scope="col" className="px-3 py-2 font-medium">
                      {t('ai.center.columns.status')}
                    </th>
                    <th scope="col" className="px-3 py-2 text-right font-medium">
                      {t('ai.center.columns.when')}
                    </th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-subtle">
                  {runsQuery.data.items.map((run) => (
                    <tr key={run.id} className="hover:bg-sunken">
                      <td className="max-w-[22rem] px-3 py-2">
                        <Link
                          to={`/app/documents/${run.documentId}?tab=intelligence`}
                          className="block truncate text-ink-secondary hover:text-teal-700 hover:underline dark:hover:text-teal-300"
                        >
                          <span className="font-mono text-xs text-ink-muted">{run.documentReference}</span>{' '}
                          {run.documentTitle}
                        </Link>
                      </td>
                      <td className="px-3 py-2 text-xs text-ink-secondary">
                        {run.suggestedTypeName ? (
                          <>
                            {run.documentTypeName} →{' '}
                            <span className="font-medium text-ink">{run.suggestedTypeName}</span>{' '}
                            <span className="text-ink-muted">({percent(run.classificationConfidence)})</span>
                          </>
                        ) : (
                          run.documentTypeName
                        )}
                      </td>
                      <td className="px-3 py-2 text-right text-xs tabular-nums text-ink-secondary">
                        {t('ai.center.fieldsSummary', {
                          found: run.fieldsFound,
                          missing: run.fieldsMissing,
                          decided: run.fieldsDecided,
                        })}
                      </td>
                      <td className="px-3 py-2 text-xs text-ink-secondary">
                        {run.origin === AiRunOrigin.Workflow
                          ? t('ai.review.byWorkflow')
                          : (run.requestedByName ?? '-')}
                      </td>
                      <td className="px-3 py-2">
                        <Badge tone={runStatusTone(run.status)}>{t(`ai.status.${run.status}`)}</Badge>
                      </td>
                      <td className="whitespace-nowrap px-3 py-2 text-right text-xs text-ink-secondary">
                        {formatDateTime(run.createdAtUtc, i18n.language)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </TableScroller>

            <div className="flex items-center justify-between border-t border-subtle px-3 py-2">
              <p className="text-xs text-ink-muted">
                {t('documents.page', {
                  page: runsQuery.data.page,
                  total: Math.max(runsQuery.data.totalPages, 1),
                })}
              </p>
              <div className="flex gap-2">
                <Button
                  variant="secondary"
                  size="sm"
                  disabled={!runsQuery.data.hasPrevious}
                  onClick={() => setPage((p) => p - 1)}
                >
                  {t('documents.previous')}
                </Button>
                <Button
                  variant="secondary"
                  size="sm"
                  disabled={!runsQuery.data.hasNext}
                  onClick={() => setPage((p) => p + 1)}
                >
                  {t('documents.next')}
                </Button>
              </div>
            </div>
          </>
        )}
      </Card>
    </div>
  )
}

function IntakeRow({
  id,
  reference,
  title,
  owner,
  createdAt,
}: {
  id: string
  reference: string
  title: string
  owner: string
  createdAt: string
}) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const process = useProcessDocument(id)

  return (
    <li className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
      <div className="min-w-0">
        <Link
          to={`/app/documents/${id}?tab=intelligence`}
          className="block truncate text-sm text-ink hover:text-teal-700 hover:underline dark:hover:text-teal-300"
        >
          {title}
        </Link>
        <p className="text-[11px] text-ink-muted">
          <span className="font-mono">{reference}</span> · {owner} · {createdAt}
        </p>
      </div>

      <div className="flex items-center gap-2">
        {process.error && (
          <span role="alert" className="text-xs text-red-700 dark:text-red-300">
            {t(`ai.errors.${process.error.code}`, { defaultValue: process.error.message })}
          </span>
        )}
        <Button
          size="sm"
          loading={process.isPending}
          onClick={async () => {
            try {
              await process.mutateAsync()
              navigate(`/app/documents/${id}?tab=intelligence`)
            } catch {
              // Shown beside the button from process.error.
            }
          }}
        >
          <FileSearch aria-hidden className="size-4" />
          {t('ai.review.analyse')}
        </Button>
      </div>
    </li>
  )
}

function Tile({
  label,
  value,
  detail,
}: {
  label: string
  value: number | string | undefined
  detail?: string
}) {
  return (
    <div className="rounded-lg border border-subtle bg-raised p-4 shadow-card">
      <p className="text-xs text-ink-muted">{label}</p>
      <p className="mt-1 text-2xl font-semibold tabular-nums text-ink">{value ?? '-'}</p>
      {detail && <p className="mt-1 text-[11px] text-ink-muted">{detail}</p>}
    </div>
  )
}
