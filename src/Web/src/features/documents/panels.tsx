import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { CheckCircle2, Download, ShieldAlert, ShieldCheck, Trash2 } from 'lucide-react'
import {
  downloadUrl,
  useDocumentAudit,
  useEffectiveAccessFor,
  useGrantPermission,
  useRevokePermission,
  useTenantMembers,
  useTenantRoles,
} from './api'
import { formatBytes, formatDateTime, formatMetadataValue } from './format'
import { PermissionSubjectType, type DocumentDetail, type EffectiveAccess } from './types'
import { Button } from '@/components/ui/Button'
import {
  Badge,
  Card,
  CardBody,
  CardHeader,
  DataRow,
  EmptyState,
  LoadingState,
  TableScroller,
} from '@/components/ui/Surface'
import type { Department } from '@/lib/types'

export function OverviewPanel({ document }: { document: DocumentDetail }) {
  const { t, i18n } = useTranslation()

  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <Card>
        <CardHeader title={t('documents.overview.properties')} />
        <CardBody>
          <dl className="divide-y divide-subtle">
            <DataRow label={t('documents.reference')}>
              <span className="font-mono text-xs">{document.summary.reference}</span>
            </DataRow>
            <DataRow label={t('documents.type')}>{document.summary.typeName}</DataRow>
            <DataRow label={t('documents.owner')}>{document.summary.ownerName}</DataRow>
            <DataRow label={t('documents.department')}>{document.summary.departmentName ?? '-'}</DataRow>
            <DataRow label={t('documents.overview.businessEntity')}>{document.businessEntity ?? '-'}</DataRow>
            <DataRow label={t('documents.created')}>
              {formatDateTime(document.summary.createdAtUtc, i18n.language)}
            </DataRow>
            <DataRow label={t('documents.modified')}>
              {formatDateTime(document.summary.modifiedAtUtc, i18n.language)}
            </DataRow>
            <DataRow label={t('documents.overview.retentionPolicy')}>
              {document.retentionPolicyName ?? '-'}
            </DataRow>
            {document.summary.retentionDueAtUtc && (
              <DataRow label={t('documents.retentionDue')}>
                {formatDateTime(document.summary.retentionDueAtUtc, i18n.language)}
              </DataRow>
            )}
            {document.archivedAtUtc && (
              <DataRow label={t('documents.overview.archivedAt')}>
                {formatDateTime(document.archivedAtUtc, i18n.language)}
              </DataRow>
            )}
          </dl>

          <div className="mt-4 border-t border-subtle pt-3">
            <p className="text-xs font-medium text-ink-secondary">{t('documents.overview.description')}</p>
            <p className="mt-1 text-sm text-ink">
              {document.description ?? (
                <span className="text-ink-muted">{t('documents.overview.noDescription')}</span>
              )}
            </p>
          </div>

          {document.summary.tags.length > 0 && (
            <div className="mt-4 border-t border-subtle pt-3">
              <p className="text-xs font-medium text-ink-secondary">{t('documents.tags')}</p>
              <div className="mt-1.5 flex flex-wrap gap-1.5">
                {document.summary.tags.map((tag) => (
                  <Badge key={tag.id} tone="teal">
                    {tag.name}
                  </Badge>
                ))}
              </div>
            </div>
          )}
        </CardBody>
      </Card>

      <Card>
        <CardHeader title={t('documents.overview.metadata')} />
        <CardBody>
          {document.metadata.length === 0 ? (
            <p className="text-xs text-ink-muted">{t('documents.overview.noMetadata')}</p>
          ) : (
            <dl className="divide-y divide-subtle">
              {document.metadata.map((field) => (
                <DataRow key={field.fieldId} label={field.label}>
                  {formatMetadataValue(
                    field.value,
                    field.fieldType,
                    i18n.language,
                    documentCurrency(document),
                  )}
                  {field.aiConfidence !== null && (
                    <Badge tone="amber" className="ml-2">
                      IA {Math.round(field.aiConfidence * 100)}%
                    </Badge>
                  )}
                </DataRow>
              ))}
            </dl>
          )}
        </CardBody>
      </Card>
    </div>
  )
}

/** The currency a document states for its own amounts, when its type has a currency field. */
function documentCurrency(document: DocumentDetail): string | null {
  return document.metadata.find((field) => field.code === 'currency')?.value ?? null
}

export function VersionsPanel({ document }: { document: DocumentDetail }) {
  const { t, i18n } = useTranslation()
  const canDownload = document.effectiveAccess.levels.includes('download')

  if (document.versions.length === 0) {
    return <EmptyState title={t('documents.versionsPanel.empty')} />
  }

  return (
    <Card>
      <CardBody className="p-0">
        <TableScroller label={t('documents.versionsPanel.title')}>
          <table className="w-full min-w-[48rem] text-left text-sm">
            <thead className="bg-sunken text-[11px] uppercase tracking-wide text-ink-muted">
              <tr>
                <th scope="col" className="px-3 py-2 font-medium">
                  {t('documents.version')}
                </th>
                <th scope="col" className="px-3 py-2 font-medium">
                  {t('documents.versionsPanel.author')}
                </th>
                <th scope="col" className="px-3 py-2 font-medium">
                  {t('documents.created')}
                </th>
                <th scope="col" className="px-3 py-2 font-medium">
                  {t('documents.versionsPanel.comment')}
                </th>
                <th scope="col" className="px-3 py-2 text-right font-medium">
                  {t('documents.size')}
                </th>
                <th scope="col" className="px-3 py-2 text-right font-medium">
                  {t('documents.download')}
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-subtle">
              {document.versions.map((version) => (
                <tr key={version.id} className="hover:bg-sunken">
                  <td className="px-3 py-2">
                    <span className="font-medium text-ink">v{version.versionNumber}</span>
                    {version.isCurrent && (
                      <Badge tone="green" className="ml-2">
                        {t('documents.versionsPanel.current')}
                      </Badge>
                    )}
                    <p
                      className="mt-0.5 truncate font-mono text-[11px] text-ink-muted"
                      title={`${t('documents.versionsPanel.hash')}: ${version.contentHash}`}
                    >
                      {version.contentHash.slice(0, 16)}…
                    </p>
                  </td>
                  <td className="px-3 py-2 text-xs text-ink-secondary">{version.createdByName}</td>
                  <td className="px-3 py-2 text-xs text-ink-secondary">
                    {formatDateTime(version.createdAtUtc, i18n.language)}
                  </td>
                  <td className="px-3 py-2 text-xs text-ink-secondary">{version.comment ?? '-'}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-xs text-ink-secondary">
                    {formatBytes(version.fileSizeBytes)}
                  </td>
                  <td className="px-3 py-2 text-right">
                    {canDownload && (
                      <a
                        href={downloadUrl(document.summary.id, version.versionNumber)}
                        className="inline-flex items-center gap-1 text-xs text-teal-700 hover:underline dark:text-teal-300"
                      >
                        <Download aria-hidden className="size-3.5" />v{version.versionNumber}
                      </a>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </TableScroller>
      </CardBody>
    </Card>
  )
}

export function PermissionsPanel({
  document,
  departments,
}: {
  document: DocumentDetail
  departments: Department[]
}) {
  const { t, i18n } = useTranslation()
  const grant = useGrantPermission(document.summary.id)
  const revoke = useRevokePermission(document.summary.id)

  const canShare = document.effectiveAccess.levels.includes('share')
  const [subjectType, setSubjectType] = useState<number>(PermissionSubjectType.Department)
  const [subjectId, setSubjectId] = useState('')
  const [levels, setLevels] = useState<string[]>(['view', 'download'])
  const [expiresAt, setExpiresAt] = useState('')

  // O briefing pede permissões por pessoa, departamento, perfil e documento. Os perfis vêm do
  // servidor, não de uma lista escrita aqui: duas listas divergem, e a que a interface mostra é a
  // que ninguém repara que está errada.
  const roles = useTenantRoles()
  const members = useTenantMembers()

  const [explainFor, setExplainFor] = useState<string | null>(null)
  const explained = useEffectiveAccessFor(document.summary.id, explainFor)

  if (!canShare) {
    return <EmptyState title={t('documents.permissionsPanel.hidden')} />
  }

  const toggleLevel = (level: string) =>
    setLevels((current) =>
      current.includes(level) ? current.filter((value) => value !== level) : [...current, level],
    )

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()

    if (!subjectId) {
      return
    }

    try {
      // Uma data sem hora é o fim desse dia: quem escreve «31 de dezembro» quer o dia inteiro.
      const expiresAtUtc = expiresAt ? new Date(`${expiresAt}T23:59:59`).toISOString() : null

      await grant.mutateAsync(
        subjectType === PermissionSubjectType.Role
          ? { subjectType, roleName: subjectId, accessLevels: levels, expiresAtUtc }
          : { subjectType, subjectId, accessLevels: levels, expiresAtUtc },
      )
      setSubjectId('')
      setExpiresAt('')
    } catch {
      // Rendered from grant.error below.
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardHeader title={t('documents.permissionsPanel.title')} icon={<ShieldCheck className="size-4" />} />
        <CardBody className="p-0">
          {document.permissions.length === 0 ? (
            <p className="p-4 text-xs text-ink-muted">{t('documents.permissionsPanel.empty')}</p>
          ) : (
            <table className="w-full text-left text-sm">
              <thead className="bg-sunken text-[11px] uppercase tracking-wide text-ink-muted">
                <tr>
                  <th scope="col" className="px-3 py-2 font-medium">
                    {t('documents.permissionsPanel.subject')}
                  </th>
                  <th scope="col" className="px-3 py-2 font-medium">
                    {t('documents.permissionsPanel.levels')}
                  </th>
                  <th scope="col" className="px-3 py-2 font-medium">
                    {t('documents.permissionsPanel.grantedBy')}
                  </th>
                  <th scope="col" className="px-3 py-2" />
                </tr>
              </thead>
              <tbody className="divide-y divide-subtle">
                {document.permissions.map((permission) => (
                  <tr key={permission.id} className="hover:bg-sunken">
                    <td className="px-3 py-2">
                      <span className="font-medium text-ink">{permission.subjectDisplayName}</span>
                      <Badge className="ml-2">
                        {permission.subjectType === PermissionSubjectType.User
                          ? t('documents.permissionsPanel.subjectTypeUser')
                          : permission.subjectType === PermissionSubjectType.Department
                            ? t('documents.permissionsPanel.subjectTypeDepartment')
                            : t('documents.permissionsPanel.subjectTypeRole')}
                      </Badge>
                    </td>
                    <td className="px-3 py-2">
                      <span className="flex flex-wrap gap-1">
                        {permission.accessLevels.map((level) => (
                          <Badge key={level} tone="teal">
                            {t(`documents.access.${level}`)}
                          </Badge>
                        ))}
                      </span>
                    </td>
                    <td className="px-3 py-2 text-xs text-ink-secondary">
                      {permission.grantedByName ?? '-'}
                      <span className="ml-1 text-ink-muted">
                        · {formatDateTime(permission.grantedAtUtc, i18n.language)}
                      </span>
                    </td>
                    <td className="px-3 py-2 text-right">
                      <Button
                        variant="ghost"
                        size="sm"
                        loading={revoke.isPending}
                        onClick={() => revoke.mutate(permission.id)}
                        aria-label={`${t('documents.permissionsPanel.revoke')}: ${permission.subjectDisplayName}`}
                      >
                        <Trash2 aria-hidden className="size-4" />
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </CardBody>
      </Card>

      <Card>
        <CardHeader title={t('documents.permissionsPanel.grant')} />
        <CardBody>
          <form onSubmit={submit} className="flex flex-wrap items-end gap-3">
            <div className="flex flex-col gap-1.5">
              <label htmlFor="grant-subject-type" className="text-xs font-medium text-ink-secondary">
                {t('documents.permissionsPanel.subjectType')}
              </label>
              <select
                id="grant-subject-type"
                value={subjectType}
                onChange={(event) => {
                  setSubjectType(Number(event.target.value))
                  setSubjectId('')
                }}
                className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
              >
                <option value={PermissionSubjectType.Department}>
                  {t('documents.permissionsPanel.subjectTypeDepartment')}
                </option>
                <option value={PermissionSubjectType.Role}>
                  {t('documents.permissionsPanel.subjectTypeRole')}
                </option>
                <option value={PermissionSubjectType.User}>
                  {t('documents.permissionsPanel.subjectTypeUser')}
                </option>
              </select>
            </div>

            <div className="flex flex-col gap-1.5">
              <label htmlFor="grant-subject" className="text-xs font-medium text-ink-secondary">
                {t('documents.permissionsPanel.subject')}
              </label>
              <select
                id="grant-subject"
                value={subjectId}
                onChange={(event) => setSubjectId(event.target.value)}
                className="h-9 min-w-[12rem] rounded-md border border-strong bg-raised px-2 text-sm text-ink"
              >
                <option value="">-</option>
                {subjectType === PermissionSubjectType.Department &&
                  departments.map((department) => (
                    <option key={department.id} value={department.id}>
                      {department.name}
                    </option>
                  ))}
                {subjectType === PermissionSubjectType.Role &&
                  roles.data?.map((role) => (
                    <option key={role.name} value={role.name}>
                      {role.name}
                    </option>
                  ))}
                {subjectType === PermissionSubjectType.User &&
                  members.data?.map((member) => (
                    <option key={member.id} value={member.id}>
                      {member.displayName}
                      {member.departmentCode ? ` · ${member.departmentCode}` : ''}
                    </option>
                  ))}
              </select>
            </div>

            <div className="flex flex-col gap-1.5">
              <span className="text-xs font-medium text-ink-secondary">
                {t('documents.permissionsPanel.levels')}
              </span>
              <div className="flex flex-wrap gap-2">
                {(['download', 'edit', 'delete', 'approve', 'share'] as const).map((level) => (
                  <label key={level} className="flex items-center gap-1 text-xs text-ink-secondary">
                    <input
                      type="checkbox"
                      checked={levels.includes(level)}
                      onChange={() => toggleLevel(level)}
                      className="size-4 accent-teal-700"
                    />
                    {t(`documents.access.${level}`)}
                  </label>
                ))}
              </div>
            </div>

            <div className="flex flex-col gap-1.5">
              <label htmlFor="grant-expires" className="text-xs font-medium text-ink-secondary">
                {t('documents.permissionsPanel.expires')}
              </label>
              <input
                id="grant-expires"
                type="date"
                value={expiresAt}
                onChange={(event) => setExpiresAt(event.target.value)}
                className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
              />
            </div>

            <Button type="submit" loading={grant.isPending} disabled={!subjectId}>
              {t('documents.permissionsPanel.grant')}
            </Button>
          </form>

          {grant.error && (
            <p role="alert" className="mt-3 text-xs font-medium text-red-600">
              {grant.error.message}
            </p>
          )}
        </CardBody>
      </Card>

      {/* O briefing pede um painel que mostre exatamente porque é que alguém alcança um documento.
          Mostrá-lo apenas para quem está a olhar responde a metade da pergunta: quem concede
          precisa de saber porque é que **outra** pessoa alcança, ou não alcança. O servidor só
          responde a quem pode partilhar — perguntar «esta pessoa conseguiria?» é legítimo para
          quem decide conceder, e mais ninguém. */}
      <Card>
        <CardHeader
          title={t('documents.permissionsPanel.whyTitle')}
          description={t('documents.permissionsPanel.whySubtitle')}
          icon={<ShieldCheck className="size-4" />}
        />
        <CardBody className="flex flex-col gap-3">
          <div className="flex flex-col gap-1.5">
            <label htmlFor="explain-for" className="text-xs font-medium text-ink-secondary">
              {t('documents.permissionsPanel.whoseAccess')}
            </label>
            <select
              id="explain-for"
              value={explainFor ?? ''}
              onChange={(event) => setExplainFor(event.target.value || null)}
              className="h-9 max-w-sm rounded-md border border-strong bg-raised px-2 text-sm text-ink"
            >
              <option value="">-</option>
              {members.data?.map((member) => (
                <option key={member.id} value={member.id}>
                  {member.displayName}
                  {member.departmentCode ? ` · ${member.departmentCode}` : ''}
                </option>
              ))}
            </select>
          </div>

          {explained.isLoading && <LoadingState label={t('common.loading')} />}

          {explained.data && explained.data.levels.length === 0 && (
            <EmptyState
              title={t('documents.permissionsPanel.noAccessTitle')}
              body={t('documents.permissionsPanel.noAccessBody')}
            />
          )}

          {explained.data && explained.data.levels.length > 0 && (
            <>
              <div className="flex flex-wrap gap-1.5">
                {explained.data.levels.map((level) => (
                  <Badge key={level} tone="teal">
                    {t(`documents.access.${level}`)}
                  </Badge>
                ))}
              </div>

              <ul className="flex flex-col gap-2">
                {explained.data.reasons.map((reason, index) => (
                  <li key={`why-${index}`} className="border-l-2 border-teal-600 pl-2.5">
                    <p className="text-xs text-ink">{reason.description}</p>
                    <p className="text-[11px] text-ink-muted">
                      {t('documents.accessPanel.granted')}:{' '}
                      {reason.granted.map((level) => t(`documents.access.${level}`)).join(', ')}
                    </p>
                  </li>
                ))}

                {explained.data.restrictions.map((restriction, index) => (
                  <li key={`why-restriction-${index}`} className="border-l-2 border-amber-500 pl-2.5">
                    <p className="text-xs text-ink">{restriction.description}</p>
                    <p className="text-[11px] text-ink-muted">
                      {t('documents.accessPanel.removed')}:{' '}
                      {restriction.removed.map((level) => t(`documents.access.${level}`)).join(', ')}
                    </p>
                  </li>
                ))}
              </ul>
            </>
          )}
        </CardBody>
      </Card>
    </div>
  )
}

export function AuditPanel({ documentId }: { documentId: string }) {
  const { t, i18n } = useTranslation()
  const [page, setPage] = useState(1)
  const auditQuery = useDocumentAudit(documentId, page)

  if (auditQuery.isLoading) {
    return <LoadingState label={t('common.loading')} />
  }

  const result = auditQuery.data

  if (!result || result.items.length === 0) {
    return <EmptyState title={t('documents.auditPanel.empty')} />
  }

  return (
    <Card>
      <CardBody className="p-0">
        <ol className="divide-y divide-subtle">
          {result.items.map((entry) => (
            <li key={entry.id} className="flex gap-3 px-4 py-3">
              <div className="mt-0.5 shrink-0">
                {entry.severity === 'Security' ? (
                  <ShieldAlert aria-hidden className="size-4 text-amber-600" />
                ) : (
                  <CheckCircle2 aria-hidden className="size-4 text-teal-600 dark:text-teal-400" />
                )}
              </div>
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-baseline gap-x-2">
                  <span className="text-sm font-medium text-ink">{entry.summary ?? entry.action}</span>
                  <code className="font-mono text-[11px] text-ink-muted">{entry.action}</code>
                </div>
                <p className="text-xs text-ink-muted">
                  {entry.actorDisplayName} · {formatDateTime(entry.occurredAtUtc, i18n.language)}
                </p>
                {(entry.beforeJson ?? entry.afterJson) && (
                  <details className="mt-1">
                    <summary className="cursor-pointer text-[11px] text-ink-muted">
                      {t('documents.auditPanel.detail')}
                    </summary>
                    <pre className="mt-1 overflow-x-auto rounded-sm bg-sunken p-2 font-mono text-[11px] text-ink-secondary">
                      {[
                        entry.beforeJson && `- ${entry.beforeJson}`,
                        entry.afterJson && `+ ${entry.afterJson}`,
                      ]
                        .filter(Boolean)
                        .join('\n')}
                    </pre>
                  </details>
                )}
              </div>
            </li>
          ))}
        </ol>

        {result.totalPages > 1 && (
          <div className="flex items-center justify-between border-t border-subtle px-4 py-2">
            <p className="text-xs text-ink-muted">
              {t('documents.page', { page: result.page, total: result.totalPages })}
            </p>
            <div className="flex gap-2">
              <Button
                variant="secondary"
                size="sm"
                disabled={!result.hasPrevious}
                onClick={() => setPage((p) => p - 1)}
              >
                {t('documents.previous')}
              </Button>
              <Button
                variant="secondary"
                size="sm"
                disabled={!result.hasNext}
                onClick={() => setPage((p) => p + 1)}
              >
                {t('documents.next')}
              </Button>
            </div>
          </div>
        )}
      </CardBody>
    </Card>
  )
}

/** Explains the access decision: which rule granted what, and which rule took something away. */
export function EffectiveAccessPanel({ access }: { access: EffectiveAccess }) {
  const { t } = useTranslation()

  return (
    <Card>
      <CardHeader
        title={t('documents.accessPanel.title')}
        description={t('documents.accessPanel.subtitle')}
        icon={<ShieldCheck className="size-4" />}
      />
      <CardBody className="flex flex-col gap-3">
        <div className="flex flex-wrap gap-1.5">
          {access.levels.length === 0 ? (
            <span className="text-xs text-ink-muted">{t('documents.accessPanel.noReasons')}</span>
          ) : (
            access.levels.map((level) => (
              <Badge key={level} tone="teal">
                {t(`documents.access.${level}`)}
              </Badge>
            ))
          )}
        </div>

        <ul className="flex flex-col gap-2">
          {access.reasons.map((reason, index) => (
            <li key={`${reason.source}-${index}`} className="border-l-2 border-teal-600 pl-2.5">
              <p className="text-xs text-ink">{reason.description}</p>
              <p className="text-[11px] text-ink-muted">
                {t('documents.accessPanel.granted')}:{' '}
                {reason.granted.map((level) => t(`documents.access.${level}`)).join(', ')}
              </p>
            </li>
          ))}

          {access.restrictions.map((restriction, index) => (
            <li key={`restriction-${index}`} className="border-l-2 border-amber-500 pl-2.5">
              <p className="text-xs text-ink">{restriction.description}</p>
              <p className="text-[11px] text-ink-muted">
                {t('documents.accessPanel.removed')}:{' '}
                {restriction.removed.map((level) => t(`documents.access.${level}`)).join(', ')}
              </p>
            </li>
          ))}
        </ul>
      </CardBody>
    </Card>
  )
}
