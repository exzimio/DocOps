import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { AlertTriangle, PenLine, ShieldAlert, X } from 'lucide-react'
import { api, type ApiError } from '@/lib/api'
import { Permission } from '@/lib/types'
import type { TenantMember } from '@/features/search/members'
import { hasPermission, useSession } from '@/features/auth/session'
import { formatDateTime } from '@/features/documents/format'
import {
  useCancelSignature,
  useDeclineSignature,
  useDocumentSignatures,
  useRequestSignature,
  useSignDocument,
} from './api'
import { SignatureStatus, type SignatureRequest } from './types'
import { Button } from '@/components/ui/Button'
import { TextField } from '@/components/ui/Field'
import { Badge, Card, CardBody, CardHeader, ErrorState, LoadingState } from '@/components/ui/Surface'

function statusTone(status: SignatureRequest['status']): 'teal' | 'green' | 'red' | 'neutral' {
  switch (status) {
    case SignatureStatus.Signed:
      return 'green'
    case SignatureStatus.Declined:
      return 'red'
    case SignatureStatus.Pending:
      return 'teal'
    default:
      return 'neutral'
  }
}

/**
 * Signature requests on one document.
 *
 * The disclaimer at the top is not decoration and not boilerplate: it is the single most important
 * thing this screen says. Everything below it looks exactly like a feature that produces legally
 * binding signatures, and it does not.
 */
export function SignaturePanel({ documentId }: { documentId: string }) {
  const { t, i18n } = useTranslation()
  const { data: user } = useSession()

  const signatures = useDocumentSignatures(documentId)
  const request = useRequestSignature(documentId)
  const sign = useSignDocument(documentId)
  const decline = useDeclineSignature(documentId)
  const cancel = useCancelSignature(documentId)

  const [signerId, setSignerId] = useState('')
  const [reason, setReason] = useState('')
  const [decliningId, setDecliningId] = useState<string | null>(null)
  const [declineReason, setDeclineReason] = useState('')

  const canRequest = hasPermission(user, Permission.signaturesRequest)

  const members = useQuery<TenantMember[], ApiError>({
    queryKey: ['tenant', 'members'],
    queryFn: () => api.get<TenantMember[]>('/api/tenants/current/users'),
    staleTime: 5 * 60_000,
    enabled: canRequest,
  })

  const submit = (event: React.FormEvent) => {
    event.preventDefault()

    if (!signerId) {
      return
    }

    request.mutate(
      { signerUserId: signerId, reason: reason.trim() || undefined },
      {
        onSuccess: () => {
          setSignerId('')
          setReason('')
        },
      },
    )
  }

  const mutationError = request.error ?? sign.error ?? decline.error ?? cancel.error

  return (
    <div className="flex flex-col gap-4">
      {/* A regra que este projeto não pode quebrar: nada aqui tem validade legal. */}
      <div
        role="note"
        className="flex items-start gap-2 rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-950 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100"
      >
        <ShieldAlert aria-hidden className="mt-0.5 size-4 shrink-0" />
        <p>
          <span className="font-semibold">{t('signatures.disclaimerTitle')}</span>{' '}
          {t('signatures.disclaimerBody')}
        </p>
      </div>

      {signatures.isLoading && <LoadingState label={t('common.loading')} />}

      {signatures.isError && (
        <ErrorState
          title={t('errors.genericTitle')}
          body={signatures.error.message}
          correlationId={signatures.error.problem.correlationId}
        />
      )}

      {mutationError && (
        <p
          role="alert"
          className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-xs font-medium text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200"
        >
          {mutationError.message}
        </p>
      )}

      {canRequest && (
        <Card>
          <CardHeader
            title={t('signatures.request.title')}
            description={t('signatures.request.description')}
          />
          <CardBody>
            <form onSubmit={submit} className="flex flex-col gap-3">
              <div className="flex flex-col gap-1.5">
                <label htmlFor="signer" className="text-sm font-medium text-ink">
                  {t('signatures.request.signer')}
                </label>
                <select
                  id="signer"
                  value={signerId}
                  onChange={(event) => setSignerId(event.target.value)}
                  className="rounded-md border border-strong bg-raised px-3 py-2 text-sm text-ink"
                >
                  <option value="">{t('signatures.request.choose')}</option>
                  {members.data?.map((member) => (
                    <option key={member.id} value={member.id}>
                      {member.displayName}
                    </option>
                  ))}
                </select>
              </div>

              <TextField
                label={t('signatures.request.reason')}
                value={reason}
                onChange={(event) => setReason(event.target.value)}
                hint={t('signatures.request.reasonHint')}
              />

              <div className="flex justify-end">
                <Button type="submit" size="sm" loading={request.isPending} disabled={!signerId}>
                  <PenLine aria-hidden className="size-4" />
                  {t('signatures.request.action')}
                </Button>
              </div>
            </form>
          </CardBody>
        </Card>
      )}

      {signatures.data?.length === 0 && <p className="text-sm text-ink-muted">{t('signatures.empty')}</p>}

      {signatures.data && signatures.data.length > 0 && (
        <ul aria-label={t('signatures.listLabel')} className="flex flex-col gap-2">
          {signatures.data.map((entry) => (
            <li
              key={entry.id}
              role="region"
              aria-label={`${t('signatures.listLabel')} — ${entry.signerDisplayName}`}
              className="rounded-lg border border-subtle bg-raised p-3"
            >
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="min-w-0">
                  <p className="text-sm font-medium text-ink">{entry.signerDisplayName}</p>
                  <p className="text-xs text-ink-muted">{entry.signerEmail}</p>
                </div>
                <Badge tone={statusTone(entry.status)}>{t(`signatures.status.${entry.status}`)}</Badge>
              </div>

              {entry.reason && <p className="mt-2 text-sm text-ink-secondary">{entry.reason}</p>}

              <dl className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1.5 text-xs">
                <div>
                  <dt className="text-ink-muted">{t('signatures.requestedAt')}</dt>
                  <dd className="text-ink">{formatDateTime(entry.requestedAtUtc, i18n.language)}</dd>
                </div>
                <div>
                  <dt className="text-ink-muted">{t('signatures.requestedBy')}</dt>
                  <dd className="text-ink">{entry.requestedByDisplayName}</dd>
                </div>
                <div>
                  <dt className="text-ink-muted">{t('signatures.version')}</dt>
                  <dd className="text-ink tabular-nums">{entry.versionNumber}</dd>
                </div>
                <div>
                  <dt className="text-ink-muted">{t('signatures.completedAt')}</dt>
                  <dd className="text-ink">
                    {entry.completedAtUtc ? formatDateTime(entry.completedAtUtc, i18n.language) : '—'}
                  </dd>
                </div>
                <div className="col-span-2">
                  <dt className="text-ink-muted">{t('signatures.contentHash')}</dt>
                  {/* O hash é o que torna a assinatura verificável: se o conteúdo mudar, deixa de bater. */}
                  <dd className="break-all font-mono text-[11px] text-ink">
                    {entry.signedContentHash ?? entry.requestedContentHash}
                  </dd>
                </div>
              </dl>

              {entry.status === SignatureStatus.Signed && !entry.contentStillMatches && (
                <p
                  role="note"
                  className="mt-2 flex items-start gap-2 rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-950 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100"
                >
                  <AlertTriangle aria-hidden className="mt-0.5 size-4 shrink-0" />
                  {t('signatures.contentChanged')}
                </p>
              )}

              {entry.declineReason && (
                <p className="mt-2 text-xs text-red-700 dark:text-red-300">
                  {t('signatures.declinedBecause', { reason: entry.declineReason })}
                </p>
              )}

              {(entry.canSign || entry.canCancel) && (
                <div className="mt-3 flex flex-wrap gap-2 border-t border-subtle pt-3">
                  {entry.canSign && (
                    <>
                      <Button size="sm" loading={sign.isPending} onClick={() => sign.mutate(entry.id)}>
                        <PenLine aria-hidden className="size-4" />
                        {t('signatures.sign')}
                      </Button>
                      <Button
                        size="sm"
                        variant="secondary"
                        onClick={() => setDecliningId(decliningId === entry.id ? null : entry.id)}
                      >
                        {t('signatures.decline')}
                      </Button>
                    </>
                  )}

                  {entry.canCancel && (
                    <Button
                      size="sm"
                      variant="ghost"
                      loading={cancel.isPending}
                      onClick={() => cancel.mutate(entry.id)}
                    >
                      <X aria-hidden className="size-4" />
                      {t('signatures.cancel')}
                    </Button>
                  )}
                </div>
              )}

              {decliningId === entry.id && (
                <form
                  className="mt-2 flex flex-col gap-2"
                  onSubmit={(event) => {
                    event.preventDefault()

                    if (declineReason.trim().length === 0) {
                      return
                    }

                    decline.mutate(
                      { requestId: entry.id, reason: declineReason.trim() },
                      {
                        onSuccess: () => {
                          setDecliningId(null)
                          setDeclineReason('')
                        },
                      },
                    )
                  }}
                >
                  <TextField
                    label={t('signatures.declineReason')}
                    value={declineReason}
                    onChange={(event) => setDeclineReason(event.target.value)}
                    hint={t('signatures.declineReasonHint')}
                  />
                  <div className="flex justify-end">
                    <Button
                      type="submit"
                      size="sm"
                      variant="secondary"
                      loading={decline.isPending}
                      disabled={declineReason.trim().length === 0}
                    >
                      {t('signatures.confirmDecline')}
                    </Button>
                  </div>
                </form>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
