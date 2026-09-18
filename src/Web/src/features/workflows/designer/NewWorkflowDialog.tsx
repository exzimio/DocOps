import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import { useCreateWorkflow } from '../api'
import { useDocumentTypes } from '@/features/documents/api'
import { Button } from '@/components/ui/Button'
import { TextField } from '@/components/ui/Field'

interface NewWorkflowDialogProps {
  onClose: () => void
  onCreated: (definitionId: string) => void
}

/**
 * Creates the definition on the server before opening the designer, so the canvas always edits a
 * real row with a real concurrency token rather than something that only exists in the browser.
 */
export function NewWorkflowDialog({ onClose, onCreated }: NewWorkflowDialogProps) {
  const { t } = useTranslation()
  const create = useCreateWorkflow()
  const typesQuery = useDocumentTypes()

  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [documentTypeId, setDocumentTypeId] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }

    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()

    if (!name.trim()) {
      setError(t('workflows.designer.nameRequired'))
      return
    }

    setError(null)

    const definition = await create.mutateAsync({
      name: name.trim(),
      description: description.trim() || null,
      documentTypeId: documentTypeId || null,
    })

    onCreated(definition.summary.id)
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-graphite-950/50 p-4"
      role="dialog"
      aria-modal="true"
      aria-label={t('workflows.designer.newTitle')}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <div className="mt-16 w-full max-w-lg rounded-lg border border-subtle bg-raised shadow-overlay">
        <div className="flex items-center justify-between border-b border-subtle px-4 py-3">
          <h2 className="text-sm font-semibold text-ink">{t('workflows.designer.newTitle')}</h2>
          <Button variant="ghost" size="sm" onClick={onClose} aria-label={t('common.close')}>
            <X aria-hidden className="size-4" />
          </Button>
        </div>

        <form onSubmit={submit} className="flex flex-col gap-4 p-4">
          <TextField
            label={t('workflows.designer.processName')}
            value={name}
            onChange={(event) => setName(event.target.value)}
            error={error ?? undefined}
            autoFocus
          />

          <label className="flex flex-col gap-1.5">
            <span className="text-xs font-medium text-ink-secondary">
              {t('workflows.designer.processDescription')}
            </span>
            <textarea
              rows={2}
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              className="rounded-md border border-strong bg-raised p-2 text-sm text-ink"
            />
          </label>

          <label className="flex flex-col gap-1.5">
            <span className="text-xs font-medium text-ink-secondary">{t('workflows.documentType')}</span>
            <select
              value={documentTypeId}
              onChange={(event) => setDocumentTypeId(event.target.value)}
              className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
            >
              <option value="">{t('workflows.anyDocumentType')}</option>
              {typesQuery.data?.map((type) => (
                <option key={type.id} value={type.id}>
                  {type.name}
                </option>
              ))}
            </select>
          </label>

          {create.error && (
            <p role="alert" className="text-xs font-medium text-red-600">
              {t([`workflows.errors.${create.error.code}`, 'errors.genericBody'] as const)}
            </p>
          )}

          <div className="flex justify-end gap-2">
            <Button type="button" variant="secondary" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" loading={create.isPending}>
              {t('workflows.designer.create')}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
