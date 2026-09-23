import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { MessageCircleQuestion, Upload } from 'lucide-react'
import { hasPermission, useSession } from '@/features/auth/session'
import { Permission } from '@/lib/types'
import { usePortalSubmissions, useSubmitToPortal } from '@/features/portal/api'
import { ProgressBadge } from '@/features/portal/presentation'
import { formatDateTime } from '@/features/documents/format'
import { Button } from '@/components/ui/Button'
import { TextField } from '@/components/ui/Field'
import { Card, CardBody, CardHeader, EmptyState, ErrorState, LoadingState } from '@/components/ui/Surface'

/**
 * What this supplier has sent and what became of it, plus the form to send more.
 *
 * Everything on this screen comes from `/api/portal`, which returns a deliberately narrow view: no
 * internal reference trail, no department, no classification, no approver names. The supplier sees
 * its own paperwork and how far it has got, which is what it came for.
 */
export function PortalPage() {
  const { t, i18n } = useTranslation()
  const { data: user } = useSession()
  const submissions = usePortalSubmissions()
  const submit = useSubmitToPortal()

  const [title, setTitle] = useState('')
  const [note, setNote] = useState('')
  const [file, setFile] = useState<File | null>(null)

  const canSubmit = hasPermission(user, Permission.portalSubmit)

  const send = (event: React.FormEvent) => {
    event.preventDefault()

    if (!file || title.trim().length === 0) {
      return
    }

    submit.mutate(
      { file, title: title.trim(), note: note.trim() || undefined },
      {
        onSuccess: () => {
          setTitle('')
          setNote('')
          setFile(null)
        },
      },
    )
  }

  return (
    <div className="flex flex-col gap-5">
      <header>
        <h1 className="text-lg font-semibold text-ink">{t('portal.heading')}</h1>
        <p className="text-sm text-ink-muted">{t('portal.subtitle')}</p>
      </header>

      {canSubmit && (
        <Card>
          <CardHeader title={t('portal.submit.title')} description={t('portal.submit.description')} />
          <CardBody>
            <form onSubmit={send} className="flex flex-col gap-3">
              <TextField
                label={t('portal.submit.documentTitle')}
                value={title}
                onChange={(event) => setTitle(event.target.value)}
                placeholder={t('portal.submit.titlePlaceholder')}
                required
              />

              <TextField
                label={t('portal.submit.note')}
                value={note}
                onChange={(event) => setNote(event.target.value)}
                hint={t('portal.submit.noteHint')}
              />

              <div className="flex flex-col gap-1.5">
                <label htmlFor="portal-file" className="text-sm font-medium text-ink">
                  {t('portal.submit.file')}
                </label>
                <input
                  id="portal-file"
                  type="file"
                  required
                  onChange={(event) => setFile(event.target.files?.[0] ?? null)}
                  className="text-sm text-ink-secondary file:mr-3 file:rounded-md file:border file:border-strong file:bg-raised file:px-3 file:py-1.5 file:text-sm file:text-ink hover:file:bg-sunken"
                />
              </div>

              {submit.isError && (
                <p role="alert" className="text-xs font-medium text-red-600 dark:text-red-400">
                  {submit.error.message}
                </p>
              )}

              <div className="flex justify-end">
                <Button
                  type="submit"
                  loading={submit.isPending}
                  disabled={!file || title.trim().length === 0}
                >
                  <Upload aria-hidden className="size-4" />
                  {t('portal.submit.action')}
                </Button>
              </div>
            </form>
          </CardBody>
        </Card>
      )}

      {submissions.isLoading && <LoadingState label={t('common.loading')} />}

      {submissions.isError && (
        <ErrorState
          title={t('errors.genericTitle')}
          body={submissions.error.message}
          correlationId={submissions.error.problem.correlationId}
          action={
            <Button variant="secondary" size="sm" onClick={() => void submissions.refetch()}>
              {t('common.retry')}
            </Button>
          }
        />
      )}

      {submissions.data?.length === 0 && (
        <EmptyState title={t('portal.empty.title')} body={t('portal.empty.body')} />
      )}

      {submissions.data && submissions.data.length > 0 && (
        <ul aria-label={t('portal.nav.submissions')} className="flex flex-col gap-2">
          {submissions.data.map((submission) => (
            <li key={submission.id}>
              <Link
                to={`/portal/submissions/${submission.id}`}
                className="flex flex-col gap-2 rounded-lg border border-subtle bg-raised p-3 transition-colors hover:bg-sunken sm:flex-row sm:items-center sm:justify-between"
              >
                <div className="min-w-0">
                  <p className="truncate text-sm font-medium text-ink">{submission.title}</p>
                  <p className="text-xs text-ink-muted">
                    {submission.reference} · {formatDateTime(submission.submittedAtUtc, i18n.language)}
                    {submission.documentNumber ? ` · ${submission.documentNumber}` : ''}
                  </p>
                </div>

                <div className="flex shrink-0 items-center gap-2">
                  {submission.awaitingResponse && (
                    <span className="inline-flex items-center gap-1 rounded-md border border-amber-300 bg-amber-50 px-2 py-0.5 text-[11px] font-medium text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100">
                      <MessageCircleQuestion aria-hidden className="size-3.5" />
                      {t('portal.awaitingResponse')}
                    </span>
                  )}
                  <ProgressBadge progress={submission.progress} />
                </div>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
