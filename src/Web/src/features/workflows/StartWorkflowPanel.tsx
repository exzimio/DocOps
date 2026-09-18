import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { GitBranch, Play } from 'lucide-react'
import { useStartWorkflow, useWorkflowDefinitions, useWorkflowInstances } from './api'
import { instanceTone } from './presentation'
import { formatDateTime } from '@/features/documents/format'
import { Button } from '@/components/ui/Button'
import { Badge, Card, CardBody, CardHeader } from '@/components/ui/Surface'

interface StartWorkflowPanelProps {
  documentId: string
  documentTypeId: string
  canStart: boolean
}

/**
 * Runs beside a document: the processes already running on it, and — when the caller may edit the
 * document — the option to start another. Only definitions that are active and match the document's
 * type are offered, which is the same rule the server applies when the request arrives.
 */
export function StartWorkflowPanel({ documentId, documentTypeId, canStart }: StartWorkflowPanelProps) {
  const { t, i18n } = useTranslation()
  const [selected, setSelected] = useState('')

  const definitionsQuery = useWorkflowDefinitions()
  const instancesQuery = useWorkflowInstances({ documentId, page: 1 })
  const startWorkflow = useStartWorkflow()

  // Same rule the server applies: an active definition either targets this document's type or
  // targets none at all.
  const available = (definitionsQuery.data ?? []).filter(
    (definition) =>
      definition.isActive &&
      (definition.documentTypeId === null || definition.documentTypeId === documentTypeId),
  )

  const instances = instancesQuery.data?.items ?? []

  const startError = startWorkflow.error
    ? t([`workflows.errors.${startWorkflow.error.code}`, 'errors.genericBody'] as const)
    : null

  return (
    <Card>
      <CardHeader title={t('workflows.title')} icon={<GitBranch className="size-4" />} />
      <CardBody className="flex flex-col gap-3">
        {instances.length > 0 && (
          <ul className="flex flex-col gap-2">
            {instances.map((instance) => (
              <li key={instance.id} className="rounded-md border border-subtle p-2">
                <div className="flex items-center justify-between gap-2">
                  <Link
                    to={`/app/workflows/instances/${instance.id}`}
                    className="font-mono text-xs text-ink-secondary hover:text-teal-700 hover:underline dark:hover:text-teal-300"
                  >
                    {instance.reference}
                  </Link>
                  <Badge tone={instanceTone(instance.status)}>
                    {t(`workflows.statusValues.${instance.status}`)}
                  </Badge>
                </div>
                <p className="mt-0.5 text-[11px] text-ink-muted">
                  {instance.definitionName}
                  {instance.currentStepName && ` · ${instance.currentStepName}`}
                </p>
                <p className="text-[11px] text-ink-muted">
                  {formatDateTime(instance.startedAtUtc, i18n.language)}
                </p>
              </li>
            ))}
          </ul>
        )}

        {canStart && (
          <div className="flex flex-col gap-2">
            {available.length === 0 ? (
              <p className="text-xs text-ink-muted">{t('workflows.noActiveDefinitions')}</p>
            ) : (
              <>
                <select
                  value={selected}
                  onChange={(event) => setSelected(event.target.value)}
                  aria-label={t('workflows.startTitle')}
                  className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
                >
                  <option value="">—</option>
                  {available.map((definition) => (
                    <option key={definition.id} value={definition.id}>
                      {definition.name}
                    </option>
                  ))}
                </select>

                <Button
                  size="sm"
                  disabled={!selected}
                  loading={startWorkflow.isPending}
                  onClick={() => startWorkflow.mutate({ definitionId: selected, documentId })}
                >
                  <Play aria-hidden className="size-4" />
                  {t('workflows.start')}
                </Button>
              </>
            )}
          </div>
        )}

        {startError && (
          <p role="alert" className="text-xs font-medium text-red-600 dark:text-red-400">
            {startError}
          </p>
        )}
      </CardBody>
    </Card>
  )
}
