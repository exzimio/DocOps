import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Archive, ArchiveRestore, CheckCircle2, FileText, GitBranch, Link2, Paperclip } from 'lucide-react'
import { hasPermission, useSession } from '@/features/auth/session'
import { Permission } from '@/lib/types'
import type { Department } from '@/lib/types'
import { formatBytes, formatDateTime } from '@/features/documents/format'
import { useArchiveEmail, useInboxMessage, useMarkProcessed, useMarkRead, useReopenEmail } from './api'
import { ImportanceBadge, StatusBadge } from './presentation'
import { SaveAsDocumentDialog } from './SaveAsDocumentDialog'
import { LinkDocumentDialog } from './LinkDocumentDialog'
import { StartWorkflowDialog } from './StartWorkflowDialog'
import { EmailStatus, type EmailAttachment } from './types'
import { Button } from '@/components/ui/Button'
import { Card, CardBody, CardHeader, EmptyState, ErrorState, LoadingState } from '@/components/ui/Surface'

interface EmailDetailPanelProps {
  emailId: string
  departments: Department[]
}

type DialogState =
  | { kind: 'save-attachment'; attachment: EmailAttachment }
  | { kind: 'save-message' }
  | { kind: 'link' }
  | { kind: 'workflow' }
  | null

/**
 * What arrived, and what can be done about it.
 *
 * Every action here mirrors a permission and a state the message's own record carries
 * (`canAct`, `canArchive`, ...); the buttons shown are exactly the ones the server would accept, so
 * pressing one never surprises with a refusal the interface could have predicted.
 */
export function EmailDetailPanel({ emailId, departments }: EmailDetailPanelProps) {
  const { t, i18n } = useTranslation()
  const { data: user } = useSession()
  const emailQuery = useInboxMessage(emailId)
  const markRead = useMarkRead()
  const archiveEmail = useArchiveEmail()
  const reopenEmail = useReopenEmail()
  const markProcessed = useMarkProcessed()

  const [dialog, setDialog] = useState<DialogState>(null)

  const canProcess = hasPermission(user, Permission.inboxProcess)
  const canCreateDocuments = hasPermission(user, Permission.documentsCreate)
  const canStartWorkflows = hasPermission(user, Permission.workflowsStart)

  // Opening a message marks it read — the same thing happens when you open an email anywhere else.
  useEffect(() => {
    if (emailQuery.data?.summary.status === EmailStatus.Unread) {
      markRead.mutate(emailId)
    }
    // Runs once per message opened; markRead itself is stable across renders.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [emailId, emailQuery.data?.summary.status])

  if (emailQuery.isLoading) {
    return (
      <Card>
        <LoadingState label={t('common.loading')} />
      </Card>
    )
  }

  if (emailQuery.isError) {
    return (
      <Card>
        <ErrorState
          title={t('errors.genericTitle')}
          body={emailQuery.error.message}
          correlationId={emailQuery.error.problem.correlationId}
        />
      </Card>
    )
  }

  const email = emailQuery.data
  if (!email) {
    return null
  }

  const canAct = email.canAct && canProcess

  return (
    // Named as a region: a master-detail layout otherwise gives a screen reader no way to tell the
    // reading pane apart from the list beside it, or to say which message is open in it.
    <Card role="region" aria-label={email.summary.subject}>
      <CardHeader
        title={email.summary.subject}
        description={`${email.summary.fromName} <${email.summary.fromAddress}> · ${formatDateTime(email.summary.receivedAtUtc, i18n.language)}`}
        actions={
          <div className="flex items-center gap-1.5">
            <StatusBadge status={email.summary.status} />
            <ImportanceBadge importance={email.summary.importance} />
          </div>
        }
      />
      <CardBody className="flex flex-col gap-4">
        {email.summary.linkedDocumentId && (
          <p className="flex items-center gap-1.5 rounded-md border border-teal-200 bg-teal-50 px-3 py-2 text-xs text-teal-900 dark:border-teal-800 dark:bg-teal-950 dark:text-teal-200">
            <FileText aria-hidden className="size-3.5 shrink-0" />
            <span>
              {t('inbox.linkedTo')}{' '}
              <Link
                to={`/app/documents/${email.summary.linkedDocumentId}`}
                className="font-medium underline hover:no-underline"
              >
                {email.summary.linkedDocumentReference} · {email.summary.linkedDocumentTitle}
              </Link>
            </span>
          </p>
        )}

        <div className="whitespace-pre-wrap rounded-md border border-subtle bg-sunken p-3 text-sm text-ink">
          {email.body}
        </div>

        {email.attachments.length > 0 && (
          <div className="flex flex-col gap-2">
            <p className="text-xs font-semibold text-ink-secondary">{t('inbox.attachments')}</p>
            <ul className="flex flex-col gap-1.5">
              {email.attachments.map((attachment) => (
                <li
                  key={attachment.id}
                  className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-subtle px-3 py-2"
                >
                  <span className="flex items-center gap-2 text-sm text-ink">
                    <Paperclip aria-hidden className="size-3.5 text-ink-muted" />
                    {attachment.fileName}
                    <span className="text-xs text-ink-muted">{formatBytes(attachment.sizeBytes)}</span>
                  </span>

                  {attachment.savedAsDocumentId ? (
                    <Link
                      to={`/app/documents/${attachment.savedAsDocumentId}`}
                      className="text-xs font-medium text-teal-700 hover:underline dark:text-teal-300"
                    >
                      {attachment.savedAsDocumentReference}
                    </Link>
                  ) : (
                    canAct &&
                    canCreateDocuments && (
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() => setDialog({ kind: 'save-attachment', attachment })}
                      >
                        {t('inbox.actions.saveAttachment')}
                      </Button>
                    )
                  )}
                </li>
              ))}
            </ul>
          </div>
        )}

        {email.processedAtUtc && (
          <p className="text-xs text-ink-muted">
            {t('inbox.processedBy', {
              name: email.processedByName ?? '-',
              date: formatDateTime(email.processedAtUtc, i18n.language),
            })}
          </p>
        )}

        {email.startedWorkflowInstanceId && (
          <p className="flex items-center gap-1.5 text-xs text-ink-muted">
            <GitBranch aria-hidden className="size-3.5" />
            <Link
              to={`/app/workflows/instances/${email.startedWorkflowInstanceId}`}
              className="font-medium text-teal-700 hover:underline dark:text-teal-300"
            >
              {email.startedWorkflowReference}
            </Link>
          </p>
        )}

        {canAct && (
          <div className="flex flex-wrap gap-2 border-t border-subtle pt-3">
            {canCreateDocuments && !email.summary.linkedDocumentId && (
              <Button variant="secondary" size="sm" onClick={() => setDialog({ kind: 'save-message' })}>
                <FileText aria-hidden className="size-4" />
                {t('inbox.actions.createDocument')}
              </Button>
            )}

            <Button variant="secondary" size="sm" onClick={() => setDialog({ kind: 'link' })}>
              <Link2 aria-hidden className="size-4" />
              {t('inbox.actions.link')}
            </Button>

            {canStartWorkflows && email.summary.linkedDocumentId && (
              <Button variant="secondary" size="sm" onClick={() => setDialog({ kind: 'workflow' })}>
                <GitBranch aria-hidden className="size-4" />
                {t('inbox.actions.startWorkflow')}
              </Button>
            )}

            {email.canMarkProcessed && (
              <Button
                variant="secondary"
                size="sm"
                onClick={() => markProcessed.mutate(emailId)}
                loading={markProcessed.isPending}
              >
                <CheckCircle2 aria-hidden className="size-4" />
                {t('inbox.actions.markProcessed')}
              </Button>
            )}

            {email.canArchive && (
              <Button
                variant="ghost"
                size="sm"
                onClick={() => archiveEmail.mutate(emailId)}
                loading={archiveEmail.isPending}
              >
                <Archive aria-hidden className="size-4" />
                {t('inbox.actions.archive')}
              </Button>
            )}
          </div>
        )}

        {/* Reopening is an action like any other, so it needs the permission to act. No seeded role
            holds inbox.view without inbox.process today, but role permissions are editable by an
            administrator — and a button that the server answers with 403 is a button that does
            nothing. */}
        {email.canReopen && canProcess && (
          <div className="border-t border-subtle pt-3">
            <Button
              variant="secondary"
              size="sm"
              onClick={() => reopenEmail.mutate(emailId)}
              loading={reopenEmail.isPending}
            >
              <ArchiveRestore aria-hidden className="size-4" />
              {t('inbox.actions.reopen')}
            </Button>
          </div>
        )}

        {/* Said whatever the message's own state is: someone who may only read needs to be told
            why there is nothing to press, on an archived message as much as on an open one. */}
        {!canProcess && <EmptyState title={t('inbox.readOnlyTitle')} body={t('inbox.readOnlyBody')} />}
      </CardBody>

      {dialog?.kind === 'save-attachment' && (
        <SaveAsDocumentDialog
          email={email}
          attachment={dialog.attachment}
          departments={departments}
          onClose={() => setDialog(null)}
          onSaved={() => setDialog(null)}
        />
      )}

      {dialog?.kind === 'save-message' && (
        <SaveAsDocumentDialog
          email={email}
          attachment={undefined}
          departments={departments}
          onClose={() => setDialog(null)}
          onSaved={() => setDialog(null)}
        />
      )}

      {dialog?.kind === 'link' && (
        <LinkDocumentDialog email={email} onClose={() => setDialog(null)} onLinked={() => setDialog(null)} />
      )}

      {dialog?.kind === 'workflow' && email.summary.linkedDocumentId && (
        <StartWorkflowDialog
          email={email}
          documentId={email.summary.linkedDocumentId}
          onClose={() => setDialog(null)}
          onStarted={() => setDialog(null)}
        />
      )}
    </Card>
  )
}
