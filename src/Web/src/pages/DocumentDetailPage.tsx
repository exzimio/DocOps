import { useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Archive, ArrowLeft, Download, FilePlus2, Pencil, RotateCcw, Sparkles, Trash2 } from 'lucide-react'
import clsx from 'clsx'
import { api, type ApiError } from '@/lib/api'
import type { Department } from '@/lib/types'
import {
  downloadUrl,
  useAddVersion,
  useArchiveDocument,
  useChangeStatus,
  useDeleteDocument,
  useDocument,
} from '@/features/documents/api'
import { ClassificationBadge, StatusBadge } from '@/features/documents/presentation'
import {
  AuditPanel,
  EffectiveAccessPanel,
  OverviewPanel,
  PermissionsPanel,
  VersionsPanel,
} from '@/features/documents/panels'
import { EditDocumentForm } from '@/features/documents/EditDocumentForm'
import { StartWorkflowPanel } from '@/features/workflows/StartWorkflowPanel'
import { DocumentIntelligenceTab } from '@/features/ai/DocumentIntelligenceTab'
import { hasPermission, useSession } from '@/features/auth/session'
import { DocumentStatus } from '@/features/documents/types'
import { SignaturePanel } from '@/features/signatures/SignaturePanel'
import { Button } from '@/components/ui/Button'
import { Card, CardBody, CardHeader, ErrorState, LoadingState } from '@/components/ui/Surface'

type Tab = 'overview' | 'intelligence' | 'versions' | 'signatures' | 'permissions' | 'audit'

/** The intake type: uploaded, not yet read. Mirrors DocumentTypeCodes.Unclassified on the server. */
const UnclassifiedTypeCode = 'Unclassified'

export function DocumentDetailPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { id = '' } = useParams<{ id: string }>()
  const { data: user } = useSession()

  const [searchParams] = useSearchParams()
  const [tab, setTab] = useState<Tab>(() =>
    searchParams.get('tab') === 'intelligence' ? 'intelligence' : 'overview',
  )
  const [editing, setEditing] = useState(false)
  const [versionFile, setVersionFile] = useState<File | null>(null)
  const [versionComment, setVersionComment] = useState('')
  const [statusTarget, setStatusTarget] = useState('')
  const [statusComment, setStatusComment] = useState('')

  const documentQuery = useDocument(id)
  const addVersion = useAddVersion(id)
  const changeStatus = useChangeStatus(id)
  const archive = useArchiveDocument(id)
  const remove = useDeleteDocument()

  const departmentsQuery = useQuery<Department[], ApiError>({
    queryKey: ['tenant', 'departments'],
    queryFn: () => api.get<Department[]>('/api/tenants/current/departments'),
    staleTime: 5 * 60_000,
  })

  if (documentQuery.isLoading) {
    return <LoadingState label={t('common.loading')} />
  }

  if (documentQuery.isError) {
    const error = documentQuery.error

    return (
      <ErrorState
        title={error.isForbidden ? t('errors.forbiddenTitle') : t('errors.genericTitle')}
        body={error.isForbidden ? t('errors.forbiddenBody') : error.message}
        correlationId={error.problem.correlationId}
        action={
          <Link to="/app/documents">
            <Button variant="secondary" size="sm">
              {t('documents.backToList')}
            </Button>
          </Link>
        }
      />
    )
  }

  const document = documentQuery.data
  if (!document) {
    return null
  }

  const access = document.effectiveAccess.levels
  const canDownload = access.includes('download')
  const canEdit = access.includes('edit')
  const canEditMetadata = canEdit && document.canChangeMetadata
  const canAddVersion = canEdit && document.canChangeContent
  const canDelete = access.includes('delete')
  const isArchived = document.summary.status === DocumentStatus.Archived
  const canUseAi = hasPermission(user, 'ai.use')
  const isUnclassified = document.summary.typeCode === UnclassifiedTypeCode

  const submitVersion = async (event: React.FormEvent) => {
    event.preventDefault()

    if (!versionFile) {
      return
    }

    try {
      await addVersion.mutateAsync({ file: versionFile, comment: versionComment.trim() || undefined })
      setVersionFile(null)
      setVersionComment('')
    } catch {
      // Rendered from addVersion.error below.
    }
  }

  const submitStatus = async (event: React.FormEvent) => {
    event.preventDefault()

    if (!statusTarget) {
      return
    }

    try {
      await changeStatus.mutateAsync({
        status: Number(statusTarget),
        comment: statusComment.trim() || undefined,
      })
      setStatusTarget('')
      setStatusComment('')
    } catch {
      // Rendered from changeStatus.error below.
    }
  }

  const deleteDocument = async () => {
    if (!window.confirm(t('documents.deleteConfirm'))) {
      return
    }

    await remove.mutateAsync(document.summary.id)
    navigate('/app/documents', { replace: true })
  }

  const mutationError = addVersion.error ?? changeStatus.error ?? archive.error ?? remove.error
  const mutationErrorMessage = mutationError
    ? t([`documents.errors.${mutationError.code}`, 'errors.genericBody'] as const)
    : null

  const tabs: { key: Tab; label: string }[] = [
    { key: 'overview', label: t('documents.tabs.overview') },
    ...(canUseAi ? [{ key: 'intelligence' as const, label: t('documents.tabs.intelligence') }] : []),
    { key: 'versions', label: `${t('documents.tabs.versions')} (${document.versions.length})` },
    { key: 'signatures', label: t('documents.tabs.signatures') },
    { key: 'permissions', label: t('documents.tabs.permissions') },
    { key: 'audit', label: t('documents.tabs.audit') },
  ]

  return (
    <div className="mx-auto flex max-w-[95rem] flex-col gap-4">
      <div>
        <Link
          to="/app/documents"
          className="inline-flex items-center gap-1 text-xs text-ink-muted hover:text-ink"
        >
          <ArrowLeft aria-hidden className="size-3.5" />
          {t('documents.backToList')}
        </Link>
      </div>

      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <code className="font-mono text-xs text-ink-muted">{document.summary.reference}</code>
            <StatusBadge status={document.summary.status} />
            <ClassificationBadge value={document.summary.classification} />
          </div>
          <h1 className="mt-1 text-lg font-semibold text-ink">{document.summary.title}</h1>
          <p className="text-xs text-ink-muted">
            {document.summary.typeName} · {document.summary.ownerName}
            {document.summary.departmentName && ` · ${document.summary.departmentName}`} · v
            {document.summary.currentVersionNumber}
          </p>
        </div>

        <div className="flex flex-wrap gap-2">
          {canDownload && (
            <a href={downloadUrl(document.summary.id)}>
              <Button variant="secondary" size="sm">
                <Download aria-hidden className="size-4" />
                {t('documents.download')}
              </Button>
            </a>
          )}

          {canEditMetadata && !editing && (
            <Button variant="secondary" size="sm" onClick={() => setEditing(true)}>
              <Pencil aria-hidden className="size-4" />
              {t('documents.edit')}
            </Button>
          )}

          {canEdit && (
            <Button
              variant="secondary"
              size="sm"
              loading={archive.isPending}
              onClick={() => archive.mutate({ restore: isArchived })}
            >
              {isArchived ? (
                <>
                  <RotateCcw aria-hidden className="size-4" />
                  {t('documents.restore')}
                </>
              ) : (
                <>
                  <Archive aria-hidden className="size-4" />
                  {t('documents.archive')}
                </>
              )}
            </Button>
          )}

          {canDelete && (
            <Button variant="danger" size="sm" loading={remove.isPending} onClick={deleteDocument}>
              <Trash2 aria-hidden className="size-4" />
              {t('documents.delete')}
            </Button>
          )}
        </div>
      </header>

      {mutationErrorMessage && (
        <p
          role="alert"
          className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-xs font-medium text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200"
        >
          {mutationErrorMessage}
        </p>
      )}

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_20rem]">
        <div className="flex min-w-0 flex-col gap-4">
          {editing ? (
            <EditDocumentForm
              document={document}
              departments={departmentsQuery.data ?? []}
              onDone={() => setEditing(false)}
            />
          ) : (
            <>
              {/* Seis separadores não cabem num telemóvel: sem deslocamento horizontal, os últimos
                  ficam fora do ecrã e inalcançáveis. `shrink-0` nos botões impede que o navegador
                  os comprima até ficarem ilegíveis em vez de deslocar. */}
              <div className="flex gap-1 overflow-x-auto border-b border-subtle" role="tablist">
                {tabs.map((entry) => (
                  <button
                    key={entry.key}
                    type="button"
                    role="tab"
                    aria-selected={tab === entry.key}
                    onClick={() => setTab(entry.key)}
                    className={clsx(
                      '-mb-px shrink-0 whitespace-nowrap border-b-2 px-3 py-2 text-sm transition-colors',
                      tab === entry.key
                        ? 'border-teal-600 font-medium text-ink'
                        : 'border-transparent text-ink-muted hover:text-ink',
                    )}
                  >
                    {entry.label}
                  </button>
                ))}
              </div>

              {tab === 'overview' && isUnclassified && canUseAi && (
                <div className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-amber-300 bg-amber-50 px-3 py-2.5 text-sm text-amber-950 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100">
                  <span>{t('documents.unclassifiedHint')}</span>
                  <Button size="sm" onClick={() => setTab('intelligence')}>
                    <Sparkles aria-hidden className="size-4" />
                    {t('documents.openIntelligence')}
                  </Button>
                </div>
              )}
              {tab === 'overview' && <OverviewPanel document={document} />}
              {tab === 'intelligence' && canUseAi && (
                <DocumentIntelligenceTab document={document} canProcess={canEdit} />
              )}
              {tab === 'versions' && <VersionsPanel document={document} />}
              {tab === 'signatures' && <SignaturePanel documentId={document.summary.id} />}
              {tab === 'permissions' && (
                <PermissionsPanel document={document} departments={departmentsQuery.data ?? []} />
              )}
              {tab === 'audit' && <AuditPanel documentId={document.summary.id} />}
            </>
          )}
        </div>

        <aside className="flex flex-col gap-4">
          <EffectiveAccessPanel access={document.effectiveAccess} />

          <StartWorkflowPanel
            documentId={document.summary.id}
            documentTypeId={document.documentTypeId}
            canStart={canEdit}
          />

          {document.allowedNextStatuses.length > 0 && (
            <Card>
              <CardHeader title={t('documents.changeStatus')} />
              <CardBody>
                <form onSubmit={submitStatus} className="flex flex-col gap-2">
                  <select
                    value={statusTarget}
                    onChange={(event) => setStatusTarget(event.target.value)}
                    aria-label={t('documents.changeStatus')}
                    className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
                  >
                    <option value="">-</option>
                    {document.allowedNextStatuses.map((status) => (
                      <option key={status} value={status}>
                        {t(`documents.statusValues.${status}`)}
                      </option>
                    ))}
                  </select>

                  <input
                    type="text"
                    value={statusComment}
                    onChange={(event) => setStatusComment(event.target.value)}
                    placeholder={t('documents.statusComment')}
                    aria-label={t('documents.statusComment')}
                    className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink placeholder:text-ink-muted"
                  />

                  <Button type="submit" size="sm" disabled={!statusTarget} loading={changeStatus.isPending}>
                    {t('documents.changeStatus')}
                  </Button>
                </form>
              </CardBody>
            </Card>
          )}

          {canAddVersion && (
            <Card>
              <CardHeader title={t('documents.newVersionTitle')} icon={<FilePlus2 className="size-4" />} />
              <CardBody>
                <form onSubmit={submitVersion} className="flex flex-col gap-2">
                  <input
                    type="file"
                    onChange={(event) => setVersionFile(event.target.files?.[0] ?? null)}
                    aria-label={t('documents.uploadFile')}
                    className="rounded-md border border-strong bg-raised p-2 text-xs text-ink file:mr-2 file:rounded-sm file:border-0 file:bg-sunken file:px-2 file:py-1 file:text-xs file:text-ink"
                  />
                  <input
                    type="text"
                    value={versionComment}
                    onChange={(event) => setVersionComment(event.target.value)}
                    placeholder={t('documents.versionComment')}
                    aria-label={t('documents.versionComment')}
                    className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink placeholder:text-ink-muted"
                  />
                  <Button type="submit" size="sm" disabled={!versionFile} loading={addVersion.isPending}>
                    {t('documents.newVersion')}
                  </Button>
                </form>
              </CardBody>
            </Card>
          )}
        </aside>
      </div>
    </div>
  )
}
