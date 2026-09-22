import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import { useDocuments } from '@/features/documents/api'
import { defaultFilters } from '@/features/documents/types'
import { useLinkEmailToDocument } from './api'
import type { EmailDetail } from './types'
import { Button } from '@/components/ui/Button'
import { LoadingState } from '@/components/ui/Surface'

interface LinkDocumentDialogProps {
  email: EmailDetail
  onClose: () => void
  onLinked: () => void
}

/**
 * Points a message at a document that already exists, instead of a new one it produced. The list is
 * the same paged, authorization-filtered search the documents screen uses — nothing here reaches a
 * document the caller could not otherwise open.
 */
export function LinkDocumentDialog({ email, onClose, onLinked }: LinkDocumentDialogProps) {
  const { t } = useTranslation()
  const [query, setQuery] = useState('')
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const documentsQuery = useDocuments({ ...defaultFilters, query, pageSize: 10 })
  const linkEmail = useLinkEmailToDocument()

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }

    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()

    if (!selectedId) {
      return
    }

    try {
      await linkEmail.mutateAsync({ emailId: email.summary.id, documentId: selectedId })
      onLinked()
    } catch {
      // Surfaced from linkEmail.error below.
    }
  }

  const serverError = linkEmail.error
    ? t([`documents.errors.${linkEmail.error.code}`, 'errors.genericBody'] as const)
    : null

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-graphite-950/50 p-4"
      role="dialog"
      aria-modal="true"
      aria-label={t('inbox.actions.linkTitle')}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <div className="mt-8 w-full max-w-2xl rounded-lg border border-subtle bg-raised shadow-overlay">
        <div className="flex items-center justify-between border-b border-subtle px-4 py-3">
          <h2 className="text-sm font-semibold text-ink">{t('inbox.actions.linkTitle')}</h2>
          <Button variant="ghost" size="sm" onClick={onClose} aria-label={t('common.close')}>
            <X aria-hidden className="size-4" />
          </Button>
        </div>

        <form onSubmit={submit} className="flex flex-col gap-4 p-4">
          <input
            type="search"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder={t('documents.searchPlaceholder')}
            aria-label={t('documents.searchPlaceholder')}
            className="h-9 w-full rounded-md border border-strong bg-raised px-3 text-sm text-ink placeholder:text-ink-muted"
          />

          <div className="max-h-80 overflow-y-auto rounded-md border border-subtle">
            {documentsQuery.isLoading && <LoadingState label={t('common.loading')} />}

            {documentsQuery.data && documentsQuery.data.items.length === 0 && (
              <p className="p-4 text-center text-xs text-ink-muted">{t('documents.empty')}</p>
            )}

            {documentsQuery.data && documentsQuery.data.items.length > 0 && (
              <ul aria-label={t('inbox.actions.linkTitle')} className="divide-y divide-subtle">
                {documentsQuery.data.items.map((document) => (
                  <li key={document.id}>
                    <label className="flex cursor-pointer items-center gap-2 px-3 py-2 hover:bg-sunken">
                      <input
                        type="radio"
                        name="link-document"
                        checked={selectedId === document.id}
                        onChange={() => setSelectedId(document.id)}
                        className="size-4 accent-teal-700"
                      />
                      <span className="font-mono text-[11px] text-ink-muted">{document.reference}</span>
                      <span className="truncate text-sm text-ink">{document.title}</span>
                    </label>
                  </li>
                ))}
              </ul>
            )}
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
            <Button type="submit" disabled={!selectedId} loading={linkEmail.isPending}>
              {t('inbox.actions.link')}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
