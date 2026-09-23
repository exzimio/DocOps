import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { ArrowLeft, Download, Send } from 'lucide-react'
import { hasPermission, useSession } from '@/features/auth/session'
import { Permission } from '@/lib/types'
import { usePortalSubmission, useProvideInformation } from '@/features/portal/api'
import { ProgressBadge } from '@/features/portal/presentation'
import { formatBytes, formatDateTime } from '@/features/documents/format'
import { Button } from '@/components/ui/Button'
import { TextField } from '@/components/ui/Field'
import { Card, CardBody, CardHeader, ErrorState, LoadingState } from '@/components/ui/Surface'

/**
 * One submission: what was sent, how far it has got, and the exchange about it.
 *
 * The exchange is the only part of the internal process a supplier ever sees, and it only contains
 * the questions addressed to it and its own answers. An approver's reasoning and the rest of the
 * timeline are never selected on the server, so there is nothing here to filter out by mistake.
 */
export function PortalSubmissionPage() {
  const { t, i18n } = useTranslation()
  const { id } = useParams<{ id: string }>()
  const { data: user } = useSession()

  const submission = usePortalSubmission(id)
  const provide = useProvideInformation(id ?? '')
  const [answer, setAnswer] = useState('')

  const canSubmit = hasPermission(user, Permission.portalSubmit)

  const send = (event: React.FormEvent) => {
    event.preventDefault()

    if (answer.trim().length === 0) {
      return
    }

    provide.mutate(answer.trim(), { onSuccess: () => setAnswer('') })
  }

  if (submission.isLoading) {
    return <LoadingState label={t('common.loading')} />
  }

  if (submission.isError) {
    return (
      <ErrorState
        title={t('errors.genericTitle')}
        body={submission.error.message}
        correlationId={submission.error.problem.correlationId}
      />
    )
  }

  if (!submission.data) {
    return null
  }

  const { summary, description, fileName, fileSizeBytes, messages } = submission.data

  return (
    <div className="flex flex-col gap-4">
      <Link
        to="/portal"
        className="inline-flex items-center gap-1.5 text-sm text-ink-secondary hover:text-ink"
      >
        <ArrowLeft aria-hidden className="size-4" />
        {t('portal.back')}
      </Link>

      <Card>
        <CardHeader
          title={summary.title}
          description={summary.reference}
          actions={<ProgressBadge progress={summary.progress} />}
        />
        <CardBody className="flex flex-col gap-3">
          {description && <p className="text-sm text-ink-secondary">{description}</p>}

          <dl className="grid grid-cols-2 gap-x-4 gap-y-1.5 text-xs">
            <div>
              <dt className="text-ink-muted">{t('portal.submittedAt')}</dt>
              <dd className="text-ink">{formatDateTime(summary.submittedAtUtc, i18n.language)}</dd>
            </div>
            <div>
              <dt className="text-ink-muted">{t('portal.file')}</dt>
              <dd className="break-words text-ink">
                {fileName} · {formatBytes(fileSizeBytes)}
              </dd>
            </div>
          </dl>

          <div>
            <a href={`/api/portal/submissions/${summary.id}/content`}>
              <Button variant="secondary" size="sm">
                <Download aria-hidden className="size-4" />
                {t('portal.download')}
              </Button>
            </a>
          </div>
        </CardBody>
      </Card>

      <Card>
        <CardHeader title={t('portal.messages.title')} description={t('portal.messages.description')} />
        <CardBody className="flex flex-col gap-3">
          {messages.length === 0 && <p className="text-sm text-ink-muted">{t('portal.messages.empty')}</p>}

          {messages.length > 0 && (
            <ul aria-label={t('portal.messages.title')} className="flex flex-col gap-2">
              {messages.map((message) => (
                <li
                  key={message.id}
                  className={
                    message.fromOrganisation
                      ? 'rounded-md border border-subtle bg-sunken p-3'
                      : 'rounded-md border border-teal-200 bg-teal-50 p-3 dark:border-teal-900 dark:bg-teal-950'
                  }
                >
                  <p className="text-[11px] font-medium text-ink-muted">
                    {message.fromOrganisation
                      ? t('portal.messages.fromOrganisation')
                      : t('portal.messages.fromYou')}
                    {' · '}
                    {formatDateTime(message.occurredAtUtc, i18n.language)}
                  </p>
                  <p className="mt-1 break-words text-sm text-ink">{message.body}</p>
                </li>
              ))}
            </ul>
          )}

          {canSubmit && summary.awaitingResponse && (
            <form onSubmit={send} className="flex flex-col gap-2 border-t border-subtle pt-3">
              <TextField
                label={t('portal.messages.answer')}
                value={answer}
                onChange={(event) => setAnswer(event.target.value)}
                placeholder={t('portal.messages.answerPlaceholder')}
              />

              {provide.isError && (
                <p role="alert" className="text-xs font-medium text-red-600 dark:text-red-400">
                  {provide.error.message}
                </p>
              )}

              <div className="flex justify-end">
                <Button
                  type="submit"
                  size="sm"
                  loading={provide.isPending}
                  disabled={answer.trim().length === 0}
                >
                  <Send aria-hidden className="size-4" />
                  {t('portal.messages.send')}
                </Button>
              </div>
            </form>
          )}
        </CardBody>
      </Card>
    </div>
  )
}
