import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { CircleCheck, CircleSlash, GitBranch, PenLine, Plus, TriangleAlert } from 'lucide-react'
import { hasPermission, useSession } from '@/features/auth/session'
import {
  useSetDefinitionActive,
  useWorkflowDefinitions,
  useWorkflowInstances,
} from '@/features/workflows/api'
import type { WorkflowDefinitionSummary } from '@/features/workflows/types'
import { instanceTone } from '@/features/workflows/presentation'
import { NewWorkflowDialog } from '@/features/workflows/designer/NewWorkflowDialog'
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

export function WorkflowsPage() {
  const { t, i18n } = useTranslation()
  const { data: user } = useSession()
  const navigate = useNavigate()

  const [definitionFilter, setDefinitionFilter] = useState<string | undefined>(undefined)
  const [page, setPage] = useState(1)
  const [isCreating, setIsCreating] = useState(false)

  const definitionsQuery = useWorkflowDefinitions()
  const instancesQuery = useWorkflowInstances({ definitionId: definitionFilter, page })

  const canActivate = hasPermission(user, 'workflows.activate')
  const canDesign = hasPermission(user, 'workflows.design')

  return (
    <div className="mx-auto flex max-w-[95rem] flex-col gap-4">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-lg font-semibold text-ink">{t('workflows.title')}</h1>
          <p className="text-sm text-ink-muted">{t('workflows.subtitle')}</p>
        </div>

        {canDesign && (
          <Button size="sm" onClick={() => setIsCreating(true)}>
            <Plus aria-hidden className="size-4" />
            {t('workflows.designer.newWorkflow')}
          </Button>
        )}
      </header>

      {isCreating && (
        <NewWorkflowDialog
          onClose={() => setIsCreating(false)}
          onCreated={(definitionId) => {
            setIsCreating(false)
            navigate(`/app/workflows/${definitionId}/designer`)
          }}
        />
      )}

      <section className="flex flex-col gap-3">
        <h2 className="text-xs font-semibold uppercase tracking-wide text-ink-muted">
          {t('workflows.definitions')}
        </h2>

        {definitionsQuery.isLoading && <LoadingState label={t('common.loading')} />}

        {definitionsQuery.isError && (
          <ErrorState title={t('errors.genericTitle')} body={definitionsQuery.error.message} />
        )}

        <div className="grid gap-3 lg:grid-cols-3">
          {definitionsQuery.data?.map((definition) => (
            <DefinitionCard
              key={definition.id}
              definition={definition}
              canActivate={canActivate}
              canDesign={canDesign}
            />
          ))}
        </div>
      </section>

      <section className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="text-xs font-semibold uppercase tracking-wide text-ink-muted">
            {t('workflows.instances')}
          </h2>

          <select
            value={definitionFilter ?? ''}
            onChange={(event) => {
              setDefinitionFilter(event.target.value || undefined)
              setPage(1)
            }}
            aria-label={t('workflows.definition')}
            className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
          >
            <option value="">{t('workflows.definition')}: -</option>
            {definitionsQuery.data?.map((definition) => (
              <option key={definition.id} value={definition.id}>
                {definition.name}
              </option>
            ))}
          </select>
        </div>

        <Card>
          {instancesQuery.isLoading && <LoadingState label={t('common.loading')} />}

          {instancesQuery.data?.items.length === 0 && (
            <EmptyState title={t('workflows.emptyInstances')} body={t('workflows.emptyInstancesBody')} />
          )}

          {instancesQuery.data && instancesQuery.data.items.length > 0 && (
            <>
              <div className="overflow-x-auto">
                <table className="w-full min-w-[60rem] text-left text-sm">
                  <thead className="bg-sunken text-[11px] uppercase tracking-wide text-ink-muted">
                    <tr>
                      <th scope="col" className="px-3 py-2 font-medium">
                        {t('workflows.reference')}
                      </th>
                      <th scope="col" className="px-3 py-2 font-medium">
                        {t('workflows.definition')}
                      </th>
                      <th scope="col" className="px-3 py-2 font-medium">
                        {t('tasks.document')}
                      </th>
                      <th scope="col" className="px-3 py-2 font-medium">
                        {t('workflows.currentStep')}
                      </th>
                      <th scope="col" className="px-3 py-2 font-medium">
                        {t('tasks.status')}
                      </th>
                      <th scope="col" className="px-3 py-2 text-right font-medium">
                        {t('workflows.elapsed')}
                      </th>
                      <th scope="col" className="px-3 py-2 text-right font-medium">
                        {t('workflows.startedAt')}
                      </th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-subtle">
                    {instancesQuery.data.items.map((instance) => (
                      <tr key={instance.id} className="hover:bg-sunken">
                        <td className="whitespace-nowrap px-3 py-2 font-mono text-xs">
                          <Link
                            to={`/app/workflows/instances/${instance.id}`}
                            className="text-ink-secondary hover:text-teal-700 hover:underline dark:hover:text-teal-300"
                          >
                            {instance.reference}
                          </Link>
                        </td>
                        <td className="px-3 py-2 text-xs text-ink-secondary">{instance.definitionName}</td>
                        <td className="max-w-[20rem] px-3 py-2 text-xs">
                          {instance.documentId ? (
                            <Link
                              to={`/app/documents/${instance.documentId}`}
                              className="block truncate text-ink-secondary hover:text-teal-700 hover:underline dark:hover:text-teal-300"
                              title={instance.documentTitle ?? undefined}
                            >
                              {instance.documentTitle}
                            </Link>
                          ) : (
                            <span className="text-ink-muted">-</span>
                          )}
                        </td>
                        <td className="px-3 py-2 text-xs text-ink-secondary">
                          {instance.currentStepName ?? '-'}
                          {instance.overdueTaskCount > 0 && (
                            <Badge tone="amber" className="ml-1.5">
                              {instance.overdueTaskCount} {t('tasks.overdue').toLowerCase()}
                            </Badge>
                          )}
                        </td>
                        <td className="whitespace-nowrap px-3 py-2">
                          <Badge tone={instanceTone(instance.status)}>
                            {t(`workflows.statusValues.${instance.status}`)}
                          </Badge>
                        </td>
                        <td className="whitespace-nowrap px-3 py-2 text-right tabular-nums text-xs text-ink-secondary">
                          {t('workflows.hours', { value: Math.round(instance.elapsedHours) })}
                        </td>
                        <td className="whitespace-nowrap px-3 py-2 text-right text-xs text-ink-secondary">
                          {formatDateTime(instance.startedAtUtc, i18n.language)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <div className="flex items-center justify-between border-t border-subtle px-3 py-2">
                <p className="text-xs text-ink-muted">
                  {t('documents.page', {
                    page: instancesQuery.data.page,
                    total: Math.max(instancesQuery.data.totalPages, 1),
                  })}
                </p>
                <div className="flex gap-2">
                  <Button
                    variant="secondary"
                    size="sm"
                    disabled={!instancesQuery.data.hasPrevious}
                    onClick={() => setPage((current) => current - 1)}
                  >
                    {t('documents.previous')}
                  </Button>
                  <Button
                    variant="secondary"
                    size="sm"
                    disabled={!instancesQuery.data.hasNext}
                    onClick={() => setPage((current) => current + 1)}
                  >
                    {t('documents.next')}
                  </Button>
                </div>
              </div>
            </>
          )}
        </Card>
      </section>
    </div>
  )
}

function DefinitionCard({
  definition,
  canActivate,
  canDesign,
}: {
  definition: WorkflowDefinitionSummary
  canActivate: boolean
  canDesign: boolean
}) {
  const { t, i18n } = useTranslation()
  const setActive = useSetDefinitionActive(definition.id)

  return (
    <Card>
      <CardHeader
        title={definition.name}
        description={definition.description ?? undefined}
        icon={<GitBranch className="size-4" />}
        actions={
          <Badge tone={definition.isActive ? 'green' : 'neutral'}>
            {definition.isActive ? t('workflows.active') : t('workflows.inactive')}
          </Badge>
        }
      />
      <CardBody className="flex flex-col gap-3">
        <dl className="grid grid-cols-3 gap-2 text-center">
          <Metric label={t('workflows.nodes')} value={definition.nodeCount} />
          <Metric label={t('workflows.running')} value={definition.runningInstances} />
          <Metric label={t('workflows.completed')} value={definition.completedInstances} />
        </dl>

        <p className="text-[11px] text-ink-muted">
          {definition.documentTypeName ?? t('workflows.anyDocumentType')} · v{definition.version}
          {definition.lastStartedAtUtc && (
            <>
              {' '}
              · {t('workflows.lastStarted')}: {formatDateTime(definition.lastStartedAtUtc, i18n.language)}
            </>
          )}
        </p>

        <div className="flex gap-2">
          <Link to={`/app/workflows/${definition.id}`} className="flex-1">
            <Button variant="secondary" size="sm" className="w-full">
              {t('workflows.steps')}
            </Button>
          </Link>

          {canDesign && (
            <Link to={`/app/workflows/${definition.id}/designer`}>
              <Button variant="secondary" size="sm">
                <PenLine aria-hidden className="size-4" />
                {t('workflows.designer.open')}
              </Button>
            </Link>
          )}

          {canActivate && (
            <Button
              variant="secondary"
              size="sm"
              loading={setActive.isPending}
              onClick={() => setActive.mutate(!definition.isActive)}
            >
              {definition.isActive ? (
                <>
                  <CircleSlash aria-hidden className="size-4" />
                  {t('workflows.deactivate')}
                </>
              ) : (
                <>
                  <CircleCheck aria-hidden className="size-4" />
                  {t('workflows.activate')}
                </>
              )}
            </Button>
          )}
        </div>

        {setActive.error && (
          <p role="alert" className="flex items-center gap-1 text-xs font-medium text-red-600">
            <TriangleAlert aria-hidden className="size-3.5" />
            {t([`workflows.errors.${setActive.error.code}`, 'errors.genericBody'] as const)}
          </p>
        )}
      </CardBody>
    </Card>
  )
}

function Metric({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-md bg-sunken p-2">
      <dt className="text-[11px] text-ink-muted">{label}</dt>
      <dd className="text-lg font-semibold tabular-nums text-ink">{value}</dd>
    </div>
  )
}
