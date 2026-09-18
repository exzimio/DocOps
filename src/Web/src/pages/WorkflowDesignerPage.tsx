import { useCallback, useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import {
  ArrowLeft,
  CircleCheck,
  CircleSlash,
  Copy,
  Save,
  ShieldCheck,
  Trash2,
  TriangleAlert,
} from 'lucide-react'
import { api, type ApiError } from '@/lib/api'
import type { TenantProfile } from '@/lib/types'
import { hasPermission, useSession } from '@/features/auth/session'
import { useDocumentTypes } from '@/features/documents/api'
import {
  useDeleteWorkflow,
  useDuplicateWorkflow,
  useSaveWorkflow,
  useSetDefinitionActive,
  useValidateWorkflow,
  useWorkflowDefinition,
} from '@/features/workflows/api'
import { WorkflowNodeType, type WorkflowValidationIssue } from '@/features/workflows/types'
import { WorkflowDesigner, type DesignerSelection } from '@/features/workflows/designer/WorkflowDesigner'
import { NodePropertiesPanel, type SelectedNode } from '@/features/workflows/designer/NodePropertiesPanel'
import { EdgePropertiesPanel } from '@/features/workflows/designer/EdgePropertiesPanel'
import { buildNodeKey } from '@/features/workflows/designer/nodeCatalogue'
import {
  draftIssues,
  toDraft,
  toSaveEdges,
  toSaveNodes,
  type Draft,
  type DraftNode,
} from '@/features/workflows/designer/graph'
import { Button } from '@/components/ui/Button'
import { TextField } from '@/components/ui/Field'
import { Badge, Card, CardBody, CardHeader, ErrorState, LoadingState } from '@/components/ui/Surface'

interface Metadata {
  name: string
  description: string
  documentTypeId: string
}

export function WorkflowDesignerPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { id = '' } = useParams<{ id: string }>()
  const { data: user } = useSession()

  const definitionQuery = useWorkflowDefinition(id)
  const typesQuery = useDocumentTypes()
  const tenantQuery = useQuery<TenantProfile, ApiError>({
    queryKey: ['tenant', 'current'],
    queryFn: () => api.get<TenantProfile>('/api/tenants/current'),
  })

  const save = useSaveWorkflow(id)
  const validate = useValidateWorkflow(id)
  const setActive = useSetDefinitionActive(id)
  const duplicate = useDuplicateWorkflow()
  const remove = useDeleteWorkflow()

  // The draft is loaded once per definition; the id in `loadedFor` is what tells us a different
  // definition (or a new version after a save) needs to replace what is on the canvas.
  const [loadedFor, setLoadedFor] = useState<string | null>(null)
  const [draft, setDraft] = useState<Draft>({ nodes: [], edges: [] })
  const [metadata, setMetadata] = useState<Metadata>({ name: '', description: '', documentTypeId: '' })
  const [baseline, setBaseline] = useState('')
  const [selection, setSelection] = useState<DesignerSelection | null>(null)
  const [freshIssues, setFreshIssues] = useState<WorkflowValidationIssue[] | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)

  const definition = definitionQuery.data

  if (definition && loadedFor !== definition.summary.id) {
    const loaded = toDraft(definition.nodes, definition.edges)
    const loadedMetadata: Metadata = {
      name: definition.summary.name,
      description: definition.summary.description ?? '',
      documentTypeId: definition.summary.documentTypeId ?? '',
    }

    setLoadedFor(definition.summary.id)
    setDraft(loaded)
    setMetadata(loadedMetadata)
    setBaseline(snapshot(loaded, loadedMetadata))
    setSelection(null)
    setFreshIssues(null)
  }

  const canDesign = hasPermission(user, 'workflows.design')
  const canActivate = hasPermission(user, 'workflows.activate')
  const isLatest = definition?.summary.isLatestVersion ?? true
  const readOnly = !canDesign || !isLatest
  const willSupersede = definition ? !definition.summary.isEditableInPlace : false

  const serverIssues = freshIssues ?? definition?.validationIssues
  const issues = useMemo(() => serverIssues ?? [], [serverIssues])

  const local = useMemo(() => draftIssues(draft), [draft])

  const issuesByNode = useMemo(() => {
    const map = new Map<string, { hasError: boolean; hasWarning: boolean }>()

    issues.forEach((issue) => {
      if (!issue.nodeKey) return
      const current = map.get(issue.nodeKey) ?? { hasError: false, hasWarning: false }
      map.set(issue.nodeKey, {
        hasError: current.hasError || issue.severity === 'Error',
        hasWarning: current.hasWarning || issue.severity !== 'Error',
      })
    })

    local.forEach((issue) => {
      if (!issue.nodeKey) return
      const current = map.get(issue.nodeKey) ?? { hasError: false, hasWarning: false }
      map.set(issue.nodeKey, { ...current, hasWarning: true })
    })

    return map
  }, [issues, local])

  const isDirty = baseline !== '' && baseline !== snapshot(draft, metadata)

  const addNode = useCallback(
    (type: number) => {
      setDraft((current) => {
        const label = t(`workflows.nodeTypes.${type}`)
        const key = buildNodeKey(
          label,
          current.nodes.map((node) => node.key),
        )

        const node: DraftNode = {
          key,
          name: label,
          description: null,
          type: type as DraftNode['type'],
          configuration: type === WorkflowNodeType.End ? { outcome: label } : {},
          positionX: 80 + current.nodes.length * 40,
          positionY: 260 + (current.nodes.length % 4) * 20,
        }

        setSelection({ kind: 'node', id: key })

        return { ...current, nodes: [...current.nodes, node] }
      })
    },
    [t],
  )

  const selectedNode = useMemo<SelectedNode | null>(() => {
    if (selection?.kind !== 'node') return null
    const node = draft.nodes.find((candidate) => candidate.key === selection.id)

    return node
      ? {
          id: node.key,
          key: node.key,
          name: node.name,
          description: node.description,
          type: node.type,
          configuration: node.configuration,
        }
      : null
  }, [draft.nodes, selection])

  const selectedEdge = useMemo(
    () =>
      selection?.kind === 'edge' ? (draft.edges.find((edge) => edge.id === selection.id) ?? null) : null,
    [draft.edges, selection],
  )

  if (definitionQuery.isLoading) {
    return <LoadingState label={t('common.loading')} />
  }

  if (definitionQuery.isError || !definition) {
    return (
      <ErrorState
        title={t('errors.genericTitle')}
        body={definitionQuery.error?.message ?? t('errors.genericBody')}
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

  const submit = async () => {
    const result = await save.mutateAsync({
      name: metadata.name.trim(),
      description: metadata.description.trim() || null,
      documentTypeId: metadata.documentTypeId || null,
      trigger: definition.summary.trigger,
      nodes: toSaveNodes(draft),
      edges: toSaveEdges(draft),
      concurrencyToken: definition.concurrencyToken,
    })

    setFreshIssues(result.validationIssues)

    // A save that superseded the definition returns a different row; follow it so the canvas, the
    // concurrency token and the version badge all describe the same thing.
    if (result.summary.id !== definition.summary.id) {
      navigate(`/app/workflows/${result.summary.id}/designer`, { replace: true })
    } else {
      setLoadedFor(null)
    }
  }

  const mutationError = save.error ?? setActive.error ?? duplicate.error ?? remove.error ?? validate.error

  return (
    <div className="mx-auto flex max-w-[95rem] flex-col gap-4">
      <div>
        <Link
          to={`/app/workflows/${definition.summary.id}`}
          className="inline-flex items-center gap-1 text-xs text-ink-muted hover:text-ink"
        >
          <ArrowLeft aria-hidden className="size-3.5" />
          {t('workflows.designer.backToDefinition')}
        </Link>
      </div>

      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <code className="font-mono text-xs text-ink-muted">{definition.summary.code}</code>
            <Badge tone={definition.summary.isActive ? 'green' : 'neutral'}>
              {definition.summary.isActive ? t('workflows.active') : t('workflows.inactive')}
            </Badge>
            <Badge>v{definition.summary.version}</Badge>
            {isDirty && <Badge tone="amber">{t('workflows.designer.unsaved')}</Badge>}
          </div>
          <h1 className="mt-1 text-lg font-semibold text-ink">{t('workflows.designer.title')}</h1>
          <p className="text-sm text-ink-muted">{t('workflows.designer.subtitle')}</p>
        </div>

        <div className="flex flex-wrap gap-2">
          <Button
            variant="secondary"
            size="sm"
            loading={validate.isPending}
            onClick={() => validate.mutate(undefined, { onSuccess: setFreshIssues })}
          >
            <ShieldCheck aria-hidden className="size-4" />
            {t('workflows.validate')}
          </Button>

          {canActivate && (
            <Button
              variant="secondary"
              size="sm"
              loading={setActive.isPending}
              onClick={() => setActive.mutate(!definition.summary.isActive)}
            >
              {definition.summary.isActive ? (
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

          {canDesign && (
            <Button
              variant="secondary"
              size="sm"
              loading={duplicate.isPending}
              onClick={async () => {
                const copy = await duplicate.mutateAsync({
                  definitionId: definition.summary.id,
                  name: t('workflows.designer.copyOf', { name: definition.summary.name }),
                })

                navigate(`/app/workflows/${copy.summary.id}/designer`)
              }}
            >
              <Copy aria-hidden className="size-4" />
              {t('workflows.designer.duplicate')}
            </Button>
          )}

          {canDesign && (
            <Button
              variant={confirmDelete ? 'danger' : 'secondary'}
              size="sm"
              loading={remove.isPending}
              onClick={async () => {
                if (!confirmDelete) {
                  setConfirmDelete(true)
                  return
                }

                await remove.mutateAsync(definition.summary.id)
                navigate('/app/workflows')
              }}
            >
              <Trash2 aria-hidden className="size-4" />
              {confirmDelete ? t('workflows.designer.confirmDelete') : t('workflows.designer.delete')}
            </Button>
          )}

          {canDesign && (
            <Button size="sm" loading={save.isPending} disabled={readOnly || !isDirty} onClick={submit}>
              <Save aria-hidden className="size-4" />
              {willSupersede ? t('workflows.designer.saveAsNewVersion') : t('workflows.designer.save')}
            </Button>
          )}
        </div>
      </header>

      {readOnly && (
        <p className="rounded-md border border-subtle bg-sunken px-3 py-2 text-xs text-ink-secondary">
          {!canDesign
            ? t('workflows.designer.readOnlyPermission')
            : t('workflows.designer.readOnlySuperseded')}
        </p>
      )}

      {!readOnly && willSupersede && (
        <p className="rounded-md border border-amber-500/40 bg-amber-50 px-3 py-2 text-xs text-ink-secondary dark:bg-amber-900/20">
          {t('workflows.designer.supersedeNotice', { version: definition.summary.version + 1 })}
        </p>
      )}

      {mutationError && (
        <p
          role="alert"
          className="flex items-center gap-1.5 rounded-md border border-red-500/40 bg-red-50 px-3 py-2 text-xs font-medium text-red-700 dark:bg-red-950/30 dark:text-red-300"
        >
          <TriangleAlert aria-hidden className="size-4 shrink-0" />
          {t([`workflows.errors.${mutationError.code}`, 'errors.genericBody'] as const)}
        </p>
      )}

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_20rem]">
        <div className="flex flex-col gap-4">
          <WorkflowDesigner
            draft={draft}
            issuesByNode={issuesByNode}
            readOnly={readOnly}
            selection={selection}
            onSelectionChange={setSelection}
            onDraftChange={setDraft}
            onAddNode={addNode}
          />

          <Card>
            <CardHeader
              title={t('workflows.validationIssues')}
              actions={
                issues.length + local.length === 0 ? (
                  <Badge tone="green">{t('workflows.validationOk')}</Badge>
                ) : (
                  <Badge tone={issues.some((issue) => issue.severity === 'Error') ? 'red' : 'amber'}>
                    {issues.length + local.length}
                  </Badge>
                )
              }
            />
            <CardBody>
              {issues.length + local.length === 0 ? (
                <p className="text-xs text-ink-muted">{t('workflows.validationOk')}</p>
              ) : (
                <ul className="flex flex-col gap-1.5">
                  {issues.map((issue, index) => (
                    <li key={`server-${issue.code}-${index}`} className="flex items-start gap-2 text-xs">
                      <TriangleAlert
                        aria-hidden
                        className={
                          issue.severity === 'Error'
                            ? 'mt-0.5 size-3.5 shrink-0 text-red-600'
                            : 'mt-0.5 size-3.5 shrink-0 text-amber-600'
                        }
                      />
                      <button
                        type="button"
                        onClick={() => issue.nodeKey && setSelection({ kind: 'node', id: issue.nodeKey })}
                        className="text-left text-ink-secondary hover:text-ink"
                      >
                        {issue.message}
                        {issue.nodeKey && (
                          <code className="ml-1 font-mono text-[11px] text-ink-muted">{issue.nodeKey}</code>
                        )}
                      </button>
                    </li>
                  ))}

                  {local.map((issue, index) => (
                    <li key={`draft-${issue.code}-${index}`} className="flex items-start gap-2 text-xs">
                      <TriangleAlert aria-hidden className="mt-0.5 size-3.5 shrink-0 text-amber-600" />
                      <span className="text-ink-secondary">
                        {t(`workflows.designer.issues.${issue.code}`)}
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
        </div>

        <div className="flex flex-col gap-4">
          <Card>
            <CardHeader title={t('workflows.designer.process')} />
            <CardBody className="flex flex-col gap-3">
              <TextField
                label={t('workflows.designer.processName')}
                value={metadata.name}
                disabled={readOnly}
                onChange={(event) => setMetadata((current) => ({ ...current, name: event.target.value }))}
              />

              <label className="flex flex-col gap-1.5">
                <span className="text-xs font-medium text-ink-secondary">
                  {t('workflows.designer.processDescription')}
                </span>
                <textarea
                  rows={2}
                  value={metadata.description}
                  disabled={readOnly}
                  onChange={(event) =>
                    setMetadata((current) => ({ ...current, description: event.target.value }))
                  }
                  className="rounded-md border border-strong bg-raised p-2 text-sm text-ink disabled:opacity-60"
                />
              </label>

              <label className="flex flex-col gap-1.5">
                <span className="text-xs font-medium text-ink-secondary">{t('workflows.documentType')}</span>
                <select
                  value={metadata.documentTypeId}
                  disabled={readOnly}
                  onChange={(event) =>
                    setMetadata((current) => ({ ...current, documentTypeId: event.target.value }))
                  }
                  className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink disabled:opacity-60"
                >
                  <option value="">{t('workflows.anyDocumentType')}</option>
                  {typesQuery.data?.map((type) => (
                    <option key={type.id} value={type.id}>
                      {type.name}
                    </option>
                  ))}
                </select>
              </label>
            </CardBody>
          </Card>

          <Card>
            <CardHeader title={t('workflows.designer.properties')} />
            <CardBody>
              {selectedNode ? (
                <NodePropertiesPanel
                  node={selectedNode}
                  departments={tenantQuery.data?.departments ?? []}
                  readOnly={readOnly}
                  onChange={(updated) =>
                    setDraft((current) => ({
                      ...current,
                      nodes: current.nodes.map((node) =>
                        node.key === updated.key
                          ? {
                              ...node,
                              name: updated.name,
                              description: updated.description,
                              configuration: updated.configuration,
                            }
                          : node,
                      ),
                    }))
                  }
                  onDelete={() => {
                    setDraft((current) => ({
                      nodes: current.nodes.filter((node) => node.key !== selectedNode.key),
                      edges: current.edges.filter(
                        (edge) =>
                          edge.fromNodeKey !== selectedNode.key && edge.toNodeKey !== selectedNode.key,
                      ),
                    }))
                    setSelection(null)
                  }}
                />
              ) : selectedEdge ? (
                <EdgePropertiesPanel
                  edge={selectedEdge}
                  nodes={draft.nodes}
                  readOnly={readOnly}
                  onChange={(updated) =>
                    setDraft((current) => ({
                      ...current,
                      edges: current.edges.map((edge) => (edge.id === updated.id ? updated : edge)),
                    }))
                  }
                  onDelete={() => {
                    setDraft((current) => ({
                      ...current,
                      edges: current.edges.filter((edge) => edge.id !== selectedEdge.id),
                    }))
                    setSelection(null)
                  }}
                />
              ) : (
                <p className="text-xs text-ink-muted">{t('workflows.designer.nothingSelected')}</p>
              )}
            </CardBody>
          </Card>
        </div>
      </div>
    </div>
  )
}

/** What "unsaved" means here: anything that would go to the server on a save. */
function snapshot(draft: Draft, metadata: Metadata): string {
  return JSON.stringify({ metadata, nodes: toSaveNodes(draft), edges: toSaveEdges(draft) })
}
