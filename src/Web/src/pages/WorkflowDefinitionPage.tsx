import { Link, useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { ArrowLeft, ArrowRight, TriangleAlert } from 'lucide-react'
import { useWorkflowDefinition } from '@/features/workflows/api'
import type { WorkflowNode } from '@/features/workflows/types'
import { Button } from '@/components/ui/Button'
import { Badge, Card, CardBody, CardHeader, ErrorState, LoadingState } from '@/components/ui/Surface'

export function WorkflowDefinitionPage() {
  const { t } = useTranslation()
  const { id = '' } = useParams<{ id: string }>()
  const definitionQuery = useWorkflowDefinition(id)

  if (definitionQuery.isLoading) {
    return <LoadingState label={t('common.loading')} />
  }

  if (definitionQuery.isError) {
    return (
      <ErrorState
        title={t('errors.genericTitle')}
        body={definitionQuery.error.message}
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

  const definition = definitionQuery.data
  if (!definition) {
    return null
  }

  const errors = definition.validationIssues.filter((issue) => issue.severity === 'Error')
  const warnings = definition.validationIssues.filter((issue) => issue.severity === 'Warning')

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
        <div>
          <div className="flex items-center gap-2">
            <code className="font-mono text-xs text-ink-muted">{definition.summary.code}</code>
            <Badge tone={definition.summary.isActive ? 'green' : 'neutral'}>
              {definition.summary.isActive ? t('workflows.active') : t('workflows.inactive')}
            </Badge>
            <Badge>v{definition.summary.version}</Badge>
          </div>
          <h1 className="mt-1 text-lg font-semibold text-ink">{definition.summary.name}</h1>
          <p className="text-sm text-ink-muted">{definition.summary.description}</p>
        </div>
      </header>

      <Card>
        <CardHeader
          title={t('workflows.validationIssues')}
          actions={
            definition.validationIssues.length === 0 ? (
              <Badge tone="green">{t('workflows.validationOk')}</Badge>
            ) : (
              <Badge tone={errors.length > 0 ? 'red' : 'amber'}>{definition.validationIssues.length}</Badge>
            )
          }
        />
        <CardBody>
          {definition.validationIssues.length === 0 ? (
            <p className="text-xs text-ink-muted">{t('workflows.validationOk')}</p>
          ) : (
            <ul className="flex flex-col gap-1.5">
              {[...errors, ...warnings].map((issue, index) => (
                <li key={`${issue.code}-${index}`} className="flex items-start gap-2 text-xs">
                  <TriangleAlert
                    aria-hidden
                    className={
                      issue.severity === 'Error'
                        ? 'mt-0.5 size-3.5 text-red-600'
                        : 'mt-0.5 size-3.5 text-amber-600'
                    }
                  />
                  <span className="text-ink-secondary">
                    {issue.message}
                    {issue.nodeKey && (
                      <code className="ml-1 font-mono text-[11px] text-ink-muted">{issue.nodeKey}</code>
                    )}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </CardBody>
      </Card>

      <Card>
        <CardHeader title={t('workflows.steps')} actions={<Badge>{definition.nodes.length}</Badge>} />
        <CardBody className="p-0">
          <ol className="divide-y divide-subtle">
            {definition.nodes.map((node) => (
              <li key={node.id} className="px-4 py-3">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge tone={nodeTone(node)}>{t(`workflows.nodeTypes.${node.type}`)}</Badge>
                  <span className="text-sm font-medium text-ink">{node.name}</span>
                  <code className="font-mono text-[11px] text-ink-muted">{node.key}</code>
                </div>

                {node.description && <p className="mt-1 text-xs text-ink-muted">{node.description}</p>}

                <ul className="mt-1.5 flex flex-wrap gap-2">
                  {definition.edges
                    .filter((edge) => edge.fromNodeKey === node.key)
                    .map((edge) => (
                      <li
                        key={edge.id}
                        className="inline-flex items-center gap-1 rounded-xs border border-subtle bg-sunken px-1.5 py-0.5 text-[11px] text-ink-secondary"
                      >
                        {edge.branch && <Badge tone="teal">{edge.label ?? edge.branch}</Badge>}
                        <ArrowRight aria-hidden className="size-3" />
                        <span className="font-mono">{edge.toNodeKey}</span>
                      </li>
                    ))}
                </ul>
              </li>
            ))}
          </ol>
        </CardBody>
      </Card>
    </div>
  )
}

function nodeTone(node: WorkflowNode): 'neutral' | 'teal' | 'amber' | 'red' | 'green' {
  switch (node.type) {
    case 0:
      return 'green'
    case 8:
      return 'neutral'
    case 2:
      return 'amber'
    case 3:
      return 'teal'
    case 9:
    case 10:
    case 11:
      return 'red'
    default:
      return 'neutral'
  }
}
