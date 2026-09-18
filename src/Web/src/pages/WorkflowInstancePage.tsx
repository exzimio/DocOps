import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import {
  ArrowLeft,
  CircleDot,
  CircleSlash,
  FileText,
  Flag,
  GitBranch,
  MailCheck,
  TimerReset,
  TriangleAlert,
  UserCheck,
} from 'lucide-react'
import clsx from 'clsx'
import { useCancelWorkflow, useWorkflowInstance } from '@/features/workflows/api'
import { WorkflowInstanceStatus } from '@/features/workflows/types'
import { instanceTone, taskTone } from '@/features/workflows/presentation'
import { formatDateTime } from '@/features/documents/format'
import { Button } from '@/components/ui/Button'
import { Badge, Card, CardBody, CardHeader, DataRow, ErrorState, LoadingState } from '@/components/ui/Surface'

const eventIcons: Record<string, React.ComponentType<{ className?: string }>> = {
  'instance.started': Flag,
  'instance.completed': Flag,
  'instance.rejected': CircleSlash,
  'instance.cancelled': CircleSlash,
  'instance.failed': TriangleAlert,
  'task.created': UserCheck,
  'task.completed': UserCheck,
  'task.claimed': UserCheck,
  'task.reassigned': UserCheck,
  'task.escalated': TriangleAlert,
  'condition.evaluated': GitBranch,
  'notification.sent': MailCheck,
  'timer.scheduled': TimerReset,
  'timer.elapsed': TimerReset,
  'document.status.applied': FileText,
}

export function WorkflowInstancePage() {
  const { t, i18n } = useTranslation()
  const { id = '' } = useParams<{ id: string }>()

  const instanceQuery = useWorkflowInstance(id)
  const cancelWorkflow = useCancelWorkflow(id)
  const [cancelReason, setCancelReason] = useState('')
  const [cancelOpen, setCancelOpen] = useState(false)

  if (instanceQuery.isLoading) {
    return <LoadingState label={t('common.loading')} />
  }

  if (instanceQuery.isError) {
    return (
      <ErrorState
        title={t('errors.genericTitle')}
        body={instanceQuery.error.message}
        correlationId={instanceQuery.error.problem.correlationId}
        action={
          <Link to="/app/workflows">
            <Button variant="secondary" size="sm">
              {t('workflows.backToList')}
            </Button>
          </Link>
        }
      />
    )
  }

  const instance = instanceQuery.data
  if (!instance) {
    return null
  }

  const { summary } = instance

  return (
    <div className="mx-auto flex max-w-[95rem] flex-col gap-4">
      <div>
        <Link
          to="/app/workflows"
          className="inline-flex items-center gap-1 text-xs text-ink-muted hover:text-ink"
        >
          <ArrowLeft aria-hidden className="size-3.5" />
          {t('workflows.backToList')}
        </Link>
      </div>

      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <code className="font-mono text-xs text-ink-muted">{summary.reference}</code>
            <Badge tone={instanceTone(summary.status)}>{t(`workflows.statusValues.${summary.status}`)}</Badge>
            {summary.overdueTaskCount > 0 && (
              <Badge tone="amber">
                {summary.overdueTaskCount} {t('tasks.overdue').toLowerCase()}
              </Badge>
            )}
          </div>
          <h1 className="mt-1 text-lg font-semibold text-ink">{summary.definitionName}</h1>
          {summary.documentId && (
            <Link
              to={`/app/documents/${summary.documentId}`}
              className="text-xs text-ink-secondary hover:text-teal-700 hover:underline dark:hover:text-teal-300"
            >
              <span className="font-mono">{summary.documentReference}</span> · {summary.documentTitle}
            </Link>
          )}
        </div>

        {instance.canCancel && (
          <Button variant="danger" size="sm" onClick={() => setCancelOpen(true)}>
            <CircleSlash aria-hidden className="size-4" />
            {t('workflows.cancel')}
          </Button>
        )}
      </header>

      {instance.statusReason && (
        <p
          role="alert"
          className="rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-xs font-medium text-amber-900 dark:border-amber-800 dark:bg-amber-900/30 dark:text-amber-200"
        >
          {t('workflows.statusReason')}: {instance.statusReason}
        </p>
      )}

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="flex min-w-0 flex-col gap-4">
          <Card>
            <CardHeader title={t('workflows.timeline')} />
            <CardBody className="p-0">
              <ol className="divide-y divide-subtle">
                {instance.timeline.map((event) => {
                  const Icon = eventIcons[event.eventType] ?? CircleDot

                  return (
                    <li key={event.id} className="flex gap-3 px-4 py-3">
                      <div className="mt-0.5 shrink-0">
                        <Icon
                          className={clsx(
                            'size-4',
                            event.eventType.startsWith('instance.failed') ||
                              event.eventType.endsWith('escalated')
                              ? 'text-amber-600'
                              : 'text-teal-600 dark:text-teal-400',
                          )}
                        />
                      </div>
                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-baseline justify-between gap-x-3">
                          <p className="text-sm font-medium text-ink">{event.title}</p>
                          <time className="shrink-0 font-mono text-[11px] text-ink-muted">
                            {formatDateTime(event.occurredAtUtc, i18n.language)}
                          </time>
                        </div>
                        <p className="text-[11px] text-ink-muted">
                          {t([`workflows.eventTypes.${event.eventType}`, event.eventType] as const)}
                          {' · '}
                          {event.actorDisplayName}
                        </p>
                        {event.detail && <p className="mt-1 text-xs text-ink-secondary">{event.detail}</p>}
                      </div>
                    </li>
                  )
                })}
              </ol>
            </CardBody>
          </Card>

          <Card>
            <CardHeader title={t('workflows.steps')} />
            <CardBody className="p-0">
              <table className="w-full text-left text-sm">
                <thead className="bg-sunken text-[11px] uppercase tracking-wide text-ink-muted">
                  <tr>
                    <th scope="col" className="px-4 py-2 font-medium">
                      {t('tasks.task')}
                    </th>
                    <th scope="col" className="px-4 py-2 font-medium">
                      {t('tasks.assignee')}
                    </th>
                    <th scope="col" className="px-4 py-2 font-medium">
                      {t('tasks.status')}
                    </th>
                    <th scope="col" className="px-4 py-2 font-medium">
                      {t('tasks.due')}
                    </th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-subtle">
                  {instance.tasks.map((task) => (
                    <tr key={task.id} className="hover:bg-sunken">
                      <td className="px-4 py-2">
                        <p className="font-medium text-ink">{task.title}</p>
                        {task.decisionComment && (
                          <p className="mt-0.5 text-[11px] text-ink-muted">“{task.decisionComment}”</p>
                        )}
                      </td>
                      <td className="px-4 py-2 text-xs text-ink-secondary">
                        {task.completedByName ?? task.assigneeDisplayName}
                      </td>
                      <td className="whitespace-nowrap px-4 py-2">
                        <Badge tone={taskTone(task.status, task.isOverdue)}>
                          {t(`tasks.statusValues.${task.status}`)}
                        </Badge>
                      </td>
                      <td className="whitespace-nowrap px-4 py-2 text-xs text-ink-secondary">
                        {task.dueAtUtc ? formatDateTime(task.dueAtUtc, i18n.language) : '—'}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </CardBody>
          </Card>
        </div>

        <aside className="flex flex-col gap-4">
          <Card>
            <CardHeader title={t('workflows.definition')} />
            <CardBody>
              <dl className="divide-y divide-subtle">
                <DataRow label={t('workflows.startedBy')}>{summary.startedByName ?? '—'}</DataRow>
                <DataRow label={t('workflows.startedAt')}>
                  {formatDateTime(summary.startedAtUtc, i18n.language)}
                </DataRow>
                <DataRow label={t('workflows.completedAt')}>
                  {summary.completedAtUtc ? formatDateTime(summary.completedAtUtc, i18n.language) : '—'}
                </DataRow>
                <DataRow label={t('workflows.elapsed')}>
                  {t('workflows.hours', { value: Math.round(summary.elapsedHours) })}
                </DataRow>
                <DataRow label={t('workflows.openTasks')}>{summary.openTaskCount}</DataRow>
                {summary.outcome && <DataRow label={t('workflows.outcome')}>{summary.outcome}</DataRow>}
                {instance.resumeAtUtc && (
                  <DataRow label={t('workflows.eventTypes.timer.scheduled')}>
                    {formatDateTime(instance.resumeAtUtc, i18n.language)}
                  </DataRow>
                )}
              </dl>
            </CardBody>
          </Card>

          {summary.status === WorkflowInstanceStatus.Waiting && instance.activeNodeKeys.length > 0 && (
            <Card>
              <CardHeader title={t('workflows.activeNodes')} />
              <CardBody>
                <ul className="flex flex-wrap gap-1.5">
                  {instance.activeNodeKeys.map((key) => {
                    const node = instance.nodes.find((candidate) => candidate.key === key)

                    return (
                      <li key={key}>
                        <Badge tone="teal">{node?.name ?? key}</Badge>
                      </li>
                    )
                  })}
                </ul>
              </CardBody>
            </Card>
          )}

          <Card>
            <CardHeader title={t('workflows.variables')} />
            <CardBody>
              <dl className="divide-y divide-subtle">
                {Object.entries(instance.variables).map(([key, value]) => (
                  <DataRow key={key} label={key}>
                    <span className="font-mono text-xs">{value ?? '—'}</span>
                  </DataRow>
                ))}
              </dl>
            </CardBody>
          </Card>
        </aside>
      </div>

      {cancelOpen && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-graphite-950/50 p-4"
          role="dialog"
          aria-modal="true"
          aria-label={t('workflows.cancel')}
          onMouseDown={(event) => {
            if (event.target === event.currentTarget) setCancelOpen(false)
          }}
        >
          <div className="w-full max-w-md rounded-lg border border-subtle bg-raised p-4 shadow-overlay">
            <h2 className="text-sm font-semibold text-ink">{t('workflows.cancel')}</h2>
            <label htmlFor="cancel-reason" className="mt-3 block text-xs font-medium text-ink-secondary">
              {t('workflows.cancelReason')}
            </label>
            <textarea
              id="cancel-reason"
              rows={3}
              value={cancelReason}
              onChange={(event) => setCancelReason(event.target.value)}
              className="mt-1 w-full rounded-md border border-strong bg-raised p-2 text-sm text-ink"
            />

            <div className="mt-4 flex justify-end gap-2">
              <Button variant="secondary" onClick={() => setCancelOpen(false)}>
                {t('common.cancel')}
              </Button>
              <Button
                variant="danger"
                loading={cancelWorkflow.isPending}
                disabled={cancelReason.trim().length === 0}
                onClick={async () => {
                  try {
                    await cancelWorkflow.mutateAsync(cancelReason.trim())
                    setCancelOpen(false)
                    setCancelReason('')
                  } catch {
                    // Rendered by the instance query on the next render.
                  }
                }}
              >
                {t('workflows.cancel')}
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
