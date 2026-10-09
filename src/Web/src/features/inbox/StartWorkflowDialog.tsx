import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import { useDocument } from '@/features/documents/api'
import { useWorkflowDefinitions } from '@/features/workflows/api'
import { useStartWorkflowFromEmail } from './api'
import type { EmailDetail } from './types'
import { Button } from '@/components/ui/Button'

interface StartWorkflowDialogProps {
  email: EmailDetail
  /** The document the message produced. A process always runs on a document, so this is required. */
  documentId: string
  onClose: () => void
  onStarted: () => void
}

/**
 * Starts a process on the document this message produced. Only active definitions that either match
 * that document's type or apply to any type are offered — the same rule the server applies when the
 * request arrives, so nothing offered here can be refused there.
 */
export function StartWorkflowDialog({ email, documentId, onClose, onStarted }: StartWorkflowDialogProps) {
  const { t } = useTranslation()
  const documentQuery = useDocument(documentId)
  const definitionsQuery = useWorkflowDefinitions()
  const startWorkflow = useStartWorkflowFromEmail()
  const documentTypeId = documentQuery.data?.documentTypeId ?? null

  const [selected, setSelected] = useState('')
  const [comment, setComment] = useState('')

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }

    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  const available = (definitionsQuery.data ?? []).filter(
    (definition) =>
      definition.isActive &&
      (definition.documentTypeId === null || definition.documentTypeId === documentTypeId),
  )

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()

    if (!selected) {
      return
    }

    try {
      await startWorkflow.mutateAsync({
        emailId: email.summary.id,
        workflowDefinitionId: selected,
        comment: comment.trim() || undefined,
      })
      onStarted()
    } catch {
      // Surfaced from startWorkflow.error below.
    }
  }

  const serverError = startWorkflow.error
    ? t([`workflows.errors.${startWorkflow.error.code}`, 'errors.genericBody'] as const)
    : null

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-graphite-950/50 p-4"
      role="dialog"
      aria-modal="true"
      aria-label={t('inbox.actions.workflowTitle')}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <div className="mt-8 w-full max-w-lg rounded-lg border border-subtle bg-raised shadow-overlay">
        <div className="flex items-center justify-between border-b border-subtle px-4 py-3">
          <h2 className="text-sm font-semibold text-ink">{t('inbox.actions.workflowTitle')}</h2>
          <Button variant="ghost" size="sm" onClick={onClose} aria-label={t('common.close')}>
            <X aria-hidden className="size-4" />
          </Button>
        </div>

        <form onSubmit={submit} className="flex flex-col gap-4 p-4">
          {available.length === 0 ? (
            <p className="text-xs text-ink-muted">{t('workflows.noActiveDefinitions')}</p>
          ) : (
            <div className="flex flex-col gap-1.5">
              <label htmlFor="inbox-workflow-definition" className="text-xs font-medium text-ink-secondary">
                {t('workflows.title')}
              </label>
              <select
                id="inbox-workflow-definition"
                value={selected}
                onChange={(event) => setSelected(event.target.value)}
                className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
              >
                <option value="">-</option>
                {available.map((definition) => (
                  <option key={definition.id} value={definition.id}>
                    {definition.name}
                  </option>
                ))}
              </select>
            </div>
          )}

          <div className="flex flex-col gap-1.5">
            <label htmlFor="inbox-workflow-comment" className="text-xs font-medium text-ink-secondary">
              {t('documents.statusComment')}
            </label>
            <textarea
              id="inbox-workflow-comment"
              rows={2}
              value={comment}
              onChange={(event) => setComment(event.target.value)}
              className="rounded-md border border-strong bg-raised p-2 text-sm text-ink"
            />
          </div>

          {serverError && (
            <p
              role="alert"
              className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-xs font-medium text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200"
            >
              {serverError}
            </p>
          )}

          <div className="flex justify-end gap-2">
            <Button type="button" variant="secondary" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" disabled={!selected} loading={startWorkflow.isPending}>
              {t('inbox.actions.startWorkflow')}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
