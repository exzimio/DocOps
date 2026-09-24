import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Download, Info, TrendingDown, TrendingUp } from 'lucide-react'
import { api, type ApiError } from '@/lib/api'
import { Permission } from '@/lib/types'
import { hasPermission, useSession } from '@/features/auth/session'
import { BarList, LineChart } from '@/features/reporting/charts'
import { MetricUnit, type Dashboard, type Metric } from '@/features/reporting/types'
import { Button } from '@/components/ui/Button'
import { Card, CardBody, CardHeader, ErrorState, LoadingState } from '@/components/ui/Surface'

const RANGES = [7, 30, 90] as const

function formatMetric(metric: Metric, locale: string): string {
  if (metric.value === null) {
    return '—'
  }

  switch (metric.unit) {
    case MetricUnit.Percent:
      return `${metric.value.toLocaleString(locale, { maximumFractionDigits: 1 })}%`
    case MetricUnit.Hours:
      return `${metric.value.toLocaleString(locale, { maximumFractionDigits: 1 })} h`
    default:
      return metric.value.toLocaleString(locale)
  }
}

/**
 * Change against the preceding window of the same length. Null when either side is missing, because
 * "+100%" against a period that had nothing is a number that means nothing.
 */
function change(metric: Metric): number | null {
  if (metric.value === null || metric.previous === null || metric.previous === 0) {
    return null
  }

  return ((metric.value - metric.previous) / metric.previous) * 100
}

/**
 * The operations dashboard.
 *
 * Every figure comes from `/api/reports/dashboard`, which computes it from the same rows the rest
 * of the application works on — and from the subset this person is allowed to see. Nothing on this
 * screen is stored as a dashboard number, which is the brief's own rule and the easiest one to
 * break unnoticed: a wrong number on a panel looks exactly like a right one.
 */
export function ReportsPage() {
  const { t, i18n } = useTranslation()
  const { data: user } = useSession()
  const [days, setDays] = useState<number>(30)

  const dashboard = useQuery<Dashboard, ApiError>({
    queryKey: ['reports', 'dashboard', days],
    queryFn: () => api.get<Dashboard>(`/api/reports/dashboard?days=${days}`),
    staleTime: 30_000,
  })

  const canExport = hasPermission(user, Permission.reportsExport)

  return (
    <div className="mx-auto flex max-w-[95rem] flex-col gap-4">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-lg font-semibold text-ink">{t('reports.title')}</h1>
          <p className="text-sm text-ink-muted">{t('reports.subtitle')}</p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {/* Filtros numa linha acima dos gráficos, como a leitura espera. */}
          <div role="group" aria-label={t('reports.range')} className="flex gap-1">
            {RANGES.map((range) => (
              <button
                key={range}
                type="button"
                aria-pressed={days === range}
                onClick={() => setDays(range)}
                className={
                  days === range
                    ? 'rounded-md border border-teal-600 bg-teal-50 px-2.5 py-1 text-xs font-medium text-teal-800 dark:bg-teal-950 dark:text-teal-200'
                    : 'rounded-md border border-subtle px-2.5 py-1 text-xs text-ink-secondary hover:bg-sunken'
                }
              >
                {t('reports.lastDays', { count: range })}
              </button>
            ))}
          </div>

          {canExport && (
            <a href={`/api/reports/dashboard.csv?days=${days}`}>
              <Button variant="secondary" size="sm">
                <Download aria-hidden className="size-4" />
                {t('reports.export')}
              </Button>
            </a>
          )}
        </div>
      </header>

      {dashboard.isLoading && <LoadingState label={t('common.loading')} />}

      {dashboard.isError && (
        <ErrorState
          title={t('errors.genericTitle')}
          body={dashboard.error.message}
          correlationId={dashboard.error.problem.correlationId}
        />
      )}

      {dashboard.data && (
        <>
          {dashboard.data.scopedToCaller && (
            <p
              role="note"
              className="flex items-start gap-2 rounded-md border border-subtle bg-sunken px-3 py-2 text-xs text-ink-secondary"
            >
              <Info aria-hidden className="mt-0.5 size-4 shrink-0" />
              {t('reports.scopedNotice')}
            </p>
          )}

          <section aria-label={t('reports.indicators')} className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {dashboard.data.metrics.map((metric) => {
              const delta = change(metric)

              return (
                <Card key={metric.key} role="region" aria-label={t(`reports.metrics.${metric.key}`)}>
                  <CardBody className="flex flex-col gap-1">
                    <p className="text-xs text-ink-muted">{t(`reports.metrics.${metric.key}`)}</p>
                    <p className="text-2xl font-semibold tabular-nums text-ink">
                      {formatMetric(metric, i18n.language)}
                    </p>

                    {/* A direção não pinta a cor de propósito: menos horas de processo é bom e
                        menos documentos é mau, e o painel não sabe qual é qual. */}
                    {delta !== null && (
                      <p className="flex items-center gap-1 text-[11px] text-ink-secondary">
                        {delta >= 0 ? (
                          <TrendingUp aria-hidden className="size-3.5" />
                        ) : (
                          <TrendingDown aria-hidden className="size-3.5" />
                        )}
                        {t('reports.versusPrevious', {
                          value: `${delta >= 0 ? '+' : ''}${delta.toLocaleString(i18n.language, { maximumFractionDigits: 0 })}%`,
                        })}
                      </p>
                    )}

                    {metric.value === null && (
                      <p className="text-[11px] text-ink-muted">{t('reports.noBasis')}</p>
                    )}
                  </CardBody>
                </Card>
              )
            })}
          </section>

          <section aria-label={t('reports.trends')} className="grid gap-3 lg:grid-cols-3">
            {dashboard.data.series.map((series) => (
              <Card key={series.key} role="region" aria-label={t(`reports.series.${series.key}`)}>
                <CardHeader title={t(`reports.series.${series.key}`)} />
                <CardBody>
                  <LineChart
                    points={series.points}
                    label={t(`reports.series.${series.key}`)}
                    format={(value) => value.toLocaleString(i18n.language)}
                  />
                </CardBody>
              </Card>
            ))}
          </section>

          <section aria-label={t('reports.distribution')} className="grid gap-3 lg:grid-cols-3">
            {(
              [
                ['reports.byType', dashboard.data.documentsByType],
                ['reports.byDepartment', dashboard.data.documentsByDepartment],
                [
                  'reports.byStatus',
                  // O servidor devolve o valor do enum; o rótulo legível é deste lado.
                  dashboard.data.documentsByStatus.map((row) => ({
                    ...row,
                    label: t(`documents.statusValues.${row.label}`),
                  })),
                ],
              ] as const
            ).map(([key, rows]) => (
              <Card key={key} role="region" aria-label={t(key)}>
                <CardHeader title={t(key)} />
                <CardBody>
                  <BarList rows={rows} emptyLabel={t('reports.empty')} />
                </CardBody>
              </Card>
            ))}
          </section>

          <Card>
            <CardHeader
              title={t('reports.bottlenecks.title')}
              description={t('reports.bottlenecks.description')}
            />
            <CardBody className="p-0">
              {dashboard.data.bottlenecks.length === 0 ? (
                <p className="p-4 text-sm text-ink-muted">{t('reports.bottlenecks.empty')}</p>
              ) : (
                <table className="w-full text-sm">
                  <thead className="border-b border-subtle text-left text-xs text-ink-muted">
                    <tr>
                      <th scope="col" className="px-4 py-2 font-medium">
                        {t('reports.bottlenecks.step')}
                      </th>
                      <th scope="col" className="px-4 py-2 font-medium">
                        {t('reports.bottlenecks.open')}
                      </th>
                      <th scope="col" className="px-4 py-2 font-medium">
                        {t('reports.bottlenecks.overdue')}
                      </th>
                      <th scope="col" className="px-4 py-2 font-medium">
                        {t('reports.bottlenecks.longest')}
                      </th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-subtle">
                    {dashboard.data.bottlenecks.map((row) => (
                      <tr key={`${row.workflowName}-${row.stepName}`}>
                        <td className="px-4 py-2">
                          <span className="block text-ink">{row.stepName}</span>
                          <span className="block text-xs text-ink-muted">{row.workflowName}</span>
                        </td>
                        <td className="px-4 py-2 tabular-nums text-ink">{row.openTasks}</td>
                        <td className="px-4 py-2 tabular-nums text-ink">{row.overdue}</td>
                        <td className="px-4 py-2 tabular-nums text-ink">
                          {row.longestWaitHours.toLocaleString(i18n.language, { maximumFractionDigits: 0 })} h
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </CardBody>
          </Card>
        </>
      )}
    </div>
  )
}
