import { useTranslation } from 'react-i18next'
import { useQuery } from '@tanstack/react-query'
import { AlertTriangle, CheckCircle2, CircleDashed } from 'lucide-react'
import { api, type ApiError } from '@/lib/api'
import { formatDateTime } from '@/features/documents/format'
import {
  Badge,
  Card,
  CardBody,
  CardHeader,
  ErrorState,
  LoadingState,
  TableScroller,
} from '@/components/ui/Surface'

interface BackgroundJobHealth {
  job: string
  /** Null quando o trabalho nunca correu desde que há registos — não é o mesmo que ter corrido e não feito nada. */
  lastRunAtUtc: string | null
  lastRunSucceeded: boolean | null
  lastFailure: string | null
  runsLastDay: number
  failuresLastDay: number
  /** Null quando nada correu na janela: não há média de nenhuma corrida. */
  averageDurationMs: number | null
}

interface SystemHealth {
  jobs: BackgroundJobHealth[]
  queues: {
    overdueTasks: number
    queuedIntegrationRuns: number
    pendingAiAnalyses: number
    documentsAwaitingApproval: number
    documentsAwaitingSignature: number
  }
  recordsSinceUtc: string | null
}

/**
 * Saúde do sistema.
 *
 * Os trabalhos são da plataforma inteira — o worker corre acima das organizações, e um ciclo não
 * acontece a uma delas. As filas são desta organização. Nada aqui identifica ninguém: nomes de
 * trabalho, horas e desfechos.
 */
export function SystemHealthPanel() {
  const { t, i18n } = useTranslation()

  const health = useQuery<SystemHealth, ApiError>({
    queryKey: ['system', 'health'],
    queryFn: () => api.get<SystemHealth>('/api/system/health'),
    // O painel descreve trabalho que acontece a cada minuto; olhá-lo uma vez e nunca mais seria
    // mostrar uma fotografia de quando se abriu o separador.
    refetchInterval: 30_000,
  })

  if (health.isLoading) {
    return <LoadingState label={t('common.loading')} />
  }

  if (health.isError) {
    return <ErrorState title={t('errors.genericTitle')} body={health.error.message} />
  }

  const data = health.data!
  const queues = [
    ['overdueTasks', data.queues.overdueTasks],
    ['queuedIntegrationRuns', data.queues.queuedIntegrationRuns],
    ['pendingAiAnalyses', data.queues.pendingAiAnalyses],
    ['documentsAwaitingApproval', data.queues.documentsAwaitingApproval],
    ['documentsAwaitingSignature', data.queues.documentsAwaitingSignature],
  ] as const

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardHeader
          title={t('admin.health.jobsTitle')}
          description={
            data.recordsSinceUtc
              ? t('admin.health.recordsSince', {
                  since: formatDateTime(data.recordsSinceUtc, i18n.language),
                })
              : t('admin.health.noRecords')
          }
        />
        <CardBody className="p-0">
          <TableScroller label={t('admin.health.jobsTitle')}>
            <table className="w-full min-w-[34rem] text-sm">
              <thead className="border-b border-subtle text-left text-xs text-ink-muted">
                <tr>
                  <th scope="col" className="px-4 py-2 font-medium">
                    {t('admin.health.job')}
                  </th>
                  <th scope="col" className="px-4 py-2 font-medium">
                    {t('admin.health.lastRun')}
                  </th>
                  <th scope="col" className="px-4 py-2 font-medium">
                    {t('admin.health.lastDay')}
                  </th>
                  <th scope="col" className="px-4 py-2 font-medium">
                    {t('admin.health.average')}
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-subtle">
                {data.jobs.map((job) => (
                  <tr key={job.job}>
                    <td className="px-4 py-2">
                      <span className="flex items-center gap-2 text-ink">
                        {job.lastRunSucceeded === null ? (
                          <CircleDashed aria-hidden className="size-4 text-ink-muted" />
                        ) : job.lastRunSucceeded ? (
                          <CheckCircle2 aria-hidden className="size-4 text-teal-700" />
                        ) : (
                          <AlertTriangle aria-hidden className="size-4 text-amber-600" />
                        )}
                        {t(`admin.health.jobs.${job.job}`)}
                      </span>
                      {job.lastFailure && (
                        <span className="mt-1 block text-xs text-red-700 dark:text-red-300">
                          {job.lastFailure}
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-2 text-xs text-ink-secondary">
                      {/* Nunca ter corrido e ter corrido sem fazer nada são situações diferentes, e
                        o ecrã não as confunde num zero. */}
                      {job.lastRunAtUtc ? (
                        formatDateTime(job.lastRunAtUtc, i18n.language)
                      ) : (
                        <span className="text-ink-muted">{t('admin.health.never')}</span>
                      )}
                    </td>
                    <td className="px-4 py-2 text-xs">
                      <span className="tabular-nums text-ink">{job.runsLastDay}</span>
                      {job.failuresLastDay > 0 && (
                        <Badge tone="amber" className="ml-2">
                          {t('admin.health.failures', { count: job.failuresLastDay })}
                        </Badge>
                      )}
                    </td>
                    <td className="px-4 py-2 text-xs tabular-nums text-ink-secondary">
                      {job.averageDurationMs === null
                        ? '-'
                        : t('admin.health.milliseconds', { value: job.averageDurationMs })}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </TableScroller>
        </CardBody>
      </Card>

      <Card>
        <CardHeader title={t('admin.health.queuesTitle')} description={t('admin.health.queuesSubtitle')} />
        <CardBody>
          <dl className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
            {queues.map(([key, value]) => (
              <div key={key} className="rounded-md border border-subtle p-3">
                <dt className="text-xs text-ink-muted">{t(`admin.health.queues.${key}`)}</dt>
                <dd className="mt-1 text-xl font-semibold tabular-nums text-ink">{value}</dd>
              </div>
            ))}
          </dl>
        </CardBody>
      </Card>
    </div>
  )
}
