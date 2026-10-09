import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { AlertTriangle, CheckCircle2, Clock, TriangleAlert, UserPlus } from 'lucide-react'
import clsx from 'clsx'
import { useSession } from '@/features/auth/session'
import { useTaskAction, useTasks, useTaskStatistics } from '@/features/workflows/api'
import {
  WorkflowTaskStatus,
  defaultTaskFilters,
  type TaskAction,
  type TaskFilters,
  type TaskListItem,
  type TaskScope,
} from '@/features/workflows/types'
import { taskTone } from '@/features/workflows/presentation'
import { emptyTasksBodyKey, narrowingFilterKeys, widestTaskFilters } from '@/features/workflows/emptyTasks'
import { formatDateTime } from '@/features/documents/format'
import { Button } from '@/components/ui/Button'
import {
  Badge,
  Card,
  CardBody,
  CardHeader,
  EmptyState,
  ErrorState,
  LoadingState,
} from '@/components/ui/Surface'

const scopes: TaskScope[] = ['Mine', 'All', 'Delegated']

export function TasksPage() {
  const { t, i18n } = useTranslation()
  const { data: user } = useSession()

  const [filters, setFilters] = useState<TaskFilters>(defaultTaskFilters)
  const [pending, setPending] = useState<{
    task: TaskListItem
    action: Exclude<TaskAction, 'reassign'>
  } | null>(null)
  const [comment, setComment] = useState('')
  const [commentError, setCommentError] = useState<string | null>(null)

  const tasksQuery = useTasks(filters)
  const statisticsQuery = useTaskStatistics(filters.scope)
  const taskAction = useTaskAction()

  const canViewAll = user?.permissions.includes('tasks.view.all') ?? false
  const result = tasksQuery.data

  // O que uma lista vazia pode estar a esconder. Vazio significa que não há nada para alargar, e
  // aí — e só aí — a ausência de tarefas é um facto sobre o trabalho e não sobre os filtros.
  const narrowing = narrowingFilterKeys(filters, canViewAll)

  // Enquanto a consulta nova está a caminho, a lista mostrada ainda é a anterior — é para isso que
  // serve o `placeholderData`, e com linhas é o comportamento certo. Sem linhas não é: o ecrã
  // vazio descreveria os filtros novos por cima de um resultado velho, e chegou a afirmar «nenhum
  // processo criou tarefas até agora» no instante a seguir a alargar. Aí espera-se.
  const staleAndEmpty = tasksQuery.isPlaceholderData && result?.items.length === 0

  const submitAction = async () => {
    if (!pending) {
      return
    }

    const needsComment = pending.action === 'reject' || pending.action === 'request-info'

    if (needsComment && comment.trim().length === 0) {
      setCommentError(t('tasks.commentRequired'))
      return
    }

    try {
      await taskAction.mutateAsync({
        taskId: pending.task.id,
        action: pending.action,
        comment: comment.trim() || undefined,
      })

      setPending(null)
      setComment('')
      setCommentError(null)
    } catch {
      // Rendered from taskAction.error below.
    }
  }

  const actionError = taskAction.error
    ? t([`tasks.errors.${taskAction.error.code}`, 'errors.genericBody'] as const)
    : null

  return (
    <div className="mx-auto flex max-w-[95rem] flex-col gap-4">
      <header>
        <h1 className="text-lg font-semibold text-ink">{t('tasks.title')}</h1>
        <p className="text-sm text-ink-muted">{t('tasks.subtitle')}</p>
      </header>

      {statisticsQuery.data && (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
          <StatCard
            label={t('tasks.statistics.pending')}
            value={statisticsQuery.data.pending}
            icon={<Clock className="size-4" />}
          />
          <StatCard
            label={t('tasks.statistics.inProgress')}
            value={statisticsQuery.data.inProgress}
            icon={<Clock className="size-4" />}
          />
          <StatCard
            label={t('tasks.statistics.overdue')}
            value={statisticsQuery.data.overdue}
            icon={<TriangleAlert className="size-4" />}
            tone={statisticsQuery.data.overdue > 0 ? 'amber' : 'neutral'}
          />
          <StatCard
            label={t('tasks.statistics.completed30')}
            value={statisticsQuery.data.completedLast30Days}
            icon={<CheckCircle2 className="size-4" />}
          />
          <StatCard
            label={t('tasks.statistics.unassigned')}
            value={statisticsQuery.data.unassigned}
            icon={<UserPlus className="size-4" />}
          />
        </div>
      )}

      <Card className="p-3">
        <div className="flex flex-wrap items-center gap-2">
          {scopes
            .filter((scope) => scope !== 'All' || canViewAll)
            .map((scope) => (
              <button
                key={scope}
                type="button"
                aria-pressed={filters.scope === scope}
                onClick={() => setFilters((current) => ({ ...current, scope, page: 1 }))}
                className={clsx(
                  'rounded-md border px-2.5 py-1 text-xs font-medium transition-colors',
                  filters.scope === scope
                    ? 'border-teal-600 bg-teal-50 text-teal-800 dark:bg-teal-950 dark:text-teal-200'
                    : 'border-subtle text-ink-secondary hover:bg-sunken',
                )}
              >
                {t(`tasks.scope.${scope}`)}
              </button>
            ))}

          <span className="mx-1 h-5 w-px bg-subtle" aria-hidden />

          <label className="flex items-center gap-1.5 text-xs text-ink-secondary">
            <input
              type="checkbox"
              checked={filters.onlyOverdue}
              onChange={(event) =>
                setFilters((current) => ({ ...current, onlyOverdue: event.target.checked, page: 1 }))
              }
              className="size-4 accent-teal-700"
            />
            {t('tasks.onlyOverdue')}
          </label>

          <label className="flex items-center gap-1.5 text-xs text-ink-secondary">
            <input
              type="checkbox"
              checked={filters.statuses.length > 0}
              onChange={(event) =>
                setFilters((current) => ({
                  ...current,
                  page: 1,
                  statuses: event.target.checked
                    ? [WorkflowTaskStatus.Pending, WorkflowTaskStatus.InProgress]
                    : [],
                }))
              }
              className="size-4 accent-teal-700"
            />
            {t('tasks.openOnly')}
          </label>
        </div>
      </Card>

      {actionError && (
        <p
          role="alert"
          className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-xs font-medium text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200"
        >
          {actionError}
        </p>
      )}

      <Card>
        {(tasksQuery.isLoading || staleAndEmpty) && <LoadingState label={t('common.loading')} />}

        {tasksQuery.isError && (
          <ErrorState
            title={t('errors.genericTitle')}
            body={tasksQuery.error.message}
            correlationId={tasksQuery.error.problem.correlationId}
          />
        )}

        {result &&
          !staleAndEmpty &&
          result.items.length === 0 &&
          (narrowing.length > 0 ? (
            <EmptyState
              title={t('tasks.empty')}
              body={t('tasks.emptyFiltered', {
                filtros: new Intl.ListFormat(i18n.language, { type: 'conjunction' }).format(
                  narrowing.map((chave) => t(chave)),
                ),
              })}
              action={
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => setFilters((current) => widestTaskFilters(current, canViewAll))}
                >
                  {t('tasks.widenFilters')}
                </Button>
              }
            />
          ) : (
            <EmptyState title={t('tasks.empty')} body={t(emptyTasksBodyKey(filters.scope))} />
          ))}

        {result && result.items.length > 0 && (
          <>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[58rem] text-left text-sm">
                <thead className="bg-sunken text-[11px] uppercase tracking-wide text-ink-muted">
                  <tr>
                    <th scope="col" className="px-3 py-2 font-medium">
                      {t('tasks.task')}
                    </th>
                    <th scope="col" className="px-3 py-2 font-medium">
                      {t('tasks.document')}
                    </th>
                    <th scope="col" className="px-3 py-2 font-medium">
                      {t('tasks.assignee')}
                    </th>
                    <th scope="col" className="px-3 py-2 font-medium">
                      {t('tasks.priority')}
                    </th>
                    <th scope="col" className="px-3 py-2 font-medium">
                      {t('tasks.due')}
                    </th>
                    <th scope="col" className="px-3 py-2 font-medium">
                      {t('tasks.status')}
                    </th>
                    <th scope="col" className="px-3 py-2 text-right font-medium">
                      {t('tasks.actions')}
                    </th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-subtle">
                  {result.items.map((task) => (
                    <tr key={task.id} className="hover:bg-sunken">
                      <td className="px-3 py-2">
                        <p className="font-medium text-ink">{task.title}</p>
                        <p className="text-[11px] text-ink-muted">
                          <Link
                            to={`/app/workflows/instances/${task.instanceId}`}
                            className="font-mono hover:text-teal-700 hover:underline dark:hover:text-teal-300"
                          >
                            {task.instanceReference}
                          </Link>
                          {' · '}
                          {task.definitionName}
                        </p>
                      </td>
                      <td className="px-3 py-2 text-xs">
                        {task.documentId ? (
                          <Link
                            to={`/app/documents/${task.documentId}`}
                            className="text-ink-secondary hover:text-teal-700 hover:underline dark:hover:text-teal-300"
                          >
                            <span className="font-mono">{task.documentReference}</span>
                            <span className="ml-1 text-ink-muted">{task.documentTitle}</span>
                          </Link>
                        ) : (
                          <span className="text-ink-muted">-</span>
                        )}
                      </td>
                      <td className="whitespace-nowrap px-3 py-2 text-xs text-ink-secondary">
                        {task.assignedToUserId ? (
                          task.assigneeDisplayName
                        ) : (
                          <span className="flex items-center gap-1">
                            {task.assigneeDisplayName}
                            <Badge tone="amber">{t('tasks.unassigned')}</Badge>
                          </span>
                        )}
                      </td>
                      <td className="whitespace-nowrap px-3 py-2">
                        <Badge tone={task.priority >= 2 ? 'amber' : 'neutral'}>
                          {t(`tasks.priorityValues.${task.priority}`)}
                        </Badge>
                      </td>
                      <td className="whitespace-nowrap px-3 py-2 text-xs">
                        {task.dueAtUtc ? (
                          <span className="flex items-center gap-1.5">
                            <SlaIndicator fraction={task.slaRemainingFraction} overdue={task.isOverdue} />
                            <span
                              className={
                                task.isOverdue
                                  ? 'font-medium text-red-600 dark:text-red-400'
                                  : 'text-ink-secondary'
                              }
                            >
                              {formatDateTime(task.dueAtUtc, i18n.language)}
                            </span>
                          </span>
                        ) : (
                          <span className="text-ink-muted">{t('tasks.noDueDate')}</span>
                        )}
                        {task.escalatedAtUtc && (
                          <span className="mt-0.5 flex items-center gap-1 text-[11px] text-amber-700 dark:text-amber-400">
                            <AlertTriangle aria-hidden className="size-3" />
                            {t('tasks.escalated')}
                          </span>
                        )}
                      </td>
                      <td className="whitespace-nowrap px-3 py-2">
                        <Badge tone={taskTone(task.status, task.isOverdue)}>
                          {t(`tasks.statusValues.${task.status}`)}
                        </Badge>
                        {task.decision !== null && (
                          <p className="mt-0.5 text-[11px] text-ink-muted">
                            {t(`tasks.decisionValues.${task.decision}`)}
                          </p>
                        )}
                      </td>
                      <td className="px-3 py-2">
                        <div className="flex justify-end gap-1.5 whitespace-nowrap">
                          {task.availableActions
                            .filter(
                              (action): action is Exclude<TaskAction, 'reassign'> => action !== 'reassign',
                            )
                            .map((action) => (
                              <Button
                                key={action}
                                size="sm"
                                variant={
                                  action === 'reject'
                                    ? 'danger'
                                    : action === 'approve'
                                      ? 'primary'
                                      : 'secondary'
                                }
                                onClick={() => {
                                  setPending({ task, action })
                                  setComment('')
                                  setCommentError(null)
                                }}
                              >
                                {t(`tasks.actionLabelsShort.${action}`)}
                              </Button>
                            ))}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="flex flex-wrap items-center justify-between gap-2 border-t border-subtle px-3 py-2">
              <p className="text-xs text-ink-muted">
                {t('documents.page', { page: result.page, total: Math.max(result.totalPages, 1) })}
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

      {statisticsQuery.data && statisticsQuery.data.byAssignee.length > 0 && (
        <Card>
          <CardHeader title={t('tasks.statistics.byAssignee')} />
          <CardBody>
            <ul className="flex flex-col gap-2">
              {statisticsQuery.data.byAssignee.map((entry) => (
                <li key={entry.label} className="flex items-center gap-3">
                  <span className="w-44 shrink-0 truncate text-xs text-ink-secondary">{entry.label}</span>
                  <span className="h-2 flex-1 overflow-hidden rounded-full bg-sunken">
                    <span
                      className="block h-full rounded-full bg-teal-600"
                      style={{
                        width: `${Math.min(
                          100,
                          (entry.open /
                            Math.max(...statisticsQuery.data.byAssignee.map((item) => item.open), 1)) *
                            100,
                        )}%`,
                      }}
                    />
                  </span>
                  <span className="w-28 shrink-0 text-right text-xs text-ink-muted">
                    {entry.open} {t('tasks.statistics.open')}
                    {entry.overdue > 0 && (
                      <span className="ml-1 text-amber-700 dark:text-amber-400">· {entry.overdue}</span>
                    )}
                  </span>
                </li>
              ))}
            </ul>
          </CardBody>
        </Card>
      )}

      {pending && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-graphite-950/50 p-4"
          role="dialog"
          aria-modal="true"
          aria-label={t(`tasks.actionLabels.${pending.action}`)}
          onMouseDown={(event) => {
            if (event.target === event.currentTarget) setPending(null)
          }}
        >
          <div className="w-full max-w-md rounded-lg border border-subtle bg-raised p-4 shadow-overlay">
            <h2 className="text-sm font-semibold text-ink">
              {t(`tasks.actionLabels.${pending.action}`)}: {pending.task.title}
            </h2>
            <p className="mt-1 text-xs text-ink-muted">
              {pending.task.instanceReference} · {pending.task.definitionName}
            </p>

            <label htmlFor="task-comment" className="mt-4 block text-xs font-medium text-ink-secondary">
              {t('tasks.comment')}
            </label>
            <textarea
              id="task-comment"
              rows={3}
              value={comment}
              onChange={(event) => setComment(event.target.value)}
              placeholder={t('tasks.commentPlaceholder')}
              className="mt-1 w-full rounded-md border border-strong bg-raised p-2 text-sm text-ink"
            />
            {commentError && (
              <p role="alert" className="mt-1 text-xs font-medium text-red-600">
                {commentError}
              </p>
            )}

            <div className="mt-4 flex justify-end gap-2">
              <Button variant="secondary" onClick={() => setPending(null)}>
                {t('common.cancel')}
              </Button>
              <Button
                variant={pending.action === 'reject' ? 'danger' : 'primary'}
                loading={taskAction.isPending}
                onClick={submitAction}
              >
                {t('tasks.confirm')}
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

/** A dot that drains from teal to amber to red as the deadline approaches. */
function SlaIndicator({ fraction, overdue }: { fraction: number | null; overdue: boolean }) {
  const tone = overdue
    ? 'bg-red-600'
    : fraction === null
      ? 'bg-graphite-300'
      : fraction > 0.5
        ? 'bg-teal-600'
        : fraction > 0.2
          ? 'bg-amber-500'
          : 'bg-red-500'

  return <span aria-hidden className={clsx('inline-block size-2 shrink-0 rounded-full', tone)} />
}

function StatCard({
  label,
  value,
  icon,
  tone = 'neutral',
}: {
  label: string
  value: number
  icon: React.ReactNode
  tone?: 'neutral' | 'amber'
}) {
  return (
    <Card className="p-3">
      <div className="flex items-center gap-2">
        <span className={clsx('text-ink-muted', tone === 'amber' && 'text-amber-600')}>{icon}</span>
        <span className="text-xs text-ink-muted">{label}</span>
      </div>
      <p
        className={clsx(
          'mt-1 text-2xl font-semibold tabular-nums',
          tone === 'amber' ? 'text-amber-700 dark:text-amber-400' : 'text-ink',
        )}
      >
        {value}
      </p>
    </Card>
  )
}
