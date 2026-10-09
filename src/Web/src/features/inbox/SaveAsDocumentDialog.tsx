import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import { useDocumentTypes } from '@/features/documents/api'
import type { DocumentType } from '@/features/documents/types'
import { useCreateDocumentFromEmail, useSaveAttachmentAsDocument } from './api'
import { DocumentFieldsFieldset } from './DocumentFieldsFieldset'
import type { EmailAttachment, EmailDetail } from './types'
import { Button } from '@/components/ui/Button'
import { TextField } from '@/components/ui/Field'
import type { Department } from '@/lib/types'

interface SaveAsDocumentDialogProps {
  email: EmailDetail
  /** The attachment to save, or `undefined` to save the message body itself. */
  attachment: EmailAttachment | undefined
  departments: Department[]
  onClose: () => void
  onSaved: (documentId: string) => void
}

/**
 * Turns an attachment, or the message itself, into a document — with the same type-driven metadata
 * form the manual upload uses. Whichever type is chosen, the request goes through the document
 * service, so the new document gets the same reference, validation and audit entry a manual upload
 * would get; nothing about arriving by mail skips a step.
 */
export function SaveAsDocumentDialog({
  email,
  attachment,
  departments,
  onClose,
  onSaved,
}: SaveAsDocumentDialogProps) {
  const { t } = useTranslation()
  const typesQuery = useDocumentTypes()
  const saveAttachment = useSaveAttachmentAsDocument()
  const createFromEmail = useCreateDocumentFromEmail()

  const [title, setTitle] = useState(email.summary.subject)
  const [typeId, setTypeId] = useState('')
  const [departmentId, setDepartmentId] = useState('')
  const [tagsText, setTagsText] = useState('')
  const [metadata, setMetadata] = useState<Record<string, string>>({})
  const [errors, setErrors] = useState<Record<string, string>>({})

  const isPending = saveAttachment.isPending || createFromEmail.isPending
  const mutationError = saveAttachment.error ?? createFromEmail.error

  const selectedType: DocumentType | undefined = useMemo(
    () => typesQuery.data?.find((type) => type.id === typeId),
    [typesQuery.data, typeId],
  )

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }

    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  const validate = (): boolean => {
    const next: Record<string, string> = {}
    if (!title.trim()) next.title = t('documents.validation.titleRequired')
    if (!typeId) next.type = t('documents.validation.typeRequired')

    selectedType?.fields
      .filter((field) => field.isRequired && !metadata[field.code]?.trim())
      .forEach((field) => {
        next[field.code] = t('documents.errors.required_metadata_missing')
      })

    setErrors(next)
    return Object.keys(next).length === 0
  }

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()

    if (!validate()) {
      return
    }

    const tags = tagsText
      .split(',')
      .map((tag) => tag.trim())
      .filter(Boolean)

    try {
      const document = attachment
        ? await saveAttachment.mutateAsync({
            emailId: email.summary.id,
            attachmentId: attachment.id,
            documentTypeId: typeId,
            title: title.trim(),
            departmentId: departmentId || undefined,
            tags,
            metadata,
          })
        : await createFromEmail.mutateAsync({
            emailId: email.summary.id,
            documentTypeId: typeId,
            title: title.trim(),
            departmentId: departmentId || undefined,
            tags,
            metadata,
          })

      onSaved(document.summary.id)
    } catch {
      // Surfaced from mutationError below.
    }
  }

  const serverError = mutationError
    ? t([`documents.errors.${mutationError.code}`, 'errors.genericBody'] as const)
    : null

  const dialogTitleKey = attachment
    ? 'inbox.actions.saveAttachmentTitle'
    : 'inbox.actions.createDocumentTitle'

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-graphite-950/50 p-4"
      role="dialog"
      aria-modal="true"
      aria-label={t(dialogTitleKey)}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <div className="mt-8 w-full max-w-2xl rounded-lg border border-subtle bg-raised shadow-overlay">
        <div className="flex items-center justify-between border-b border-subtle px-4 py-3">
          <h2 className="text-sm font-semibold text-ink">{t(dialogTitleKey)}</h2>
          <Button variant="ghost" size="sm" onClick={onClose} aria-label={t('common.close')}>
            <X aria-hidden className="size-4" />
          </Button>
        </div>

        <form onSubmit={submit} className="flex flex-col gap-4 p-4">
          {attachment && (
            <p className="rounded-md border border-subtle bg-sunken px-3 py-2 text-xs text-ink-secondary">
              {t('inbox.actions.savingAttachment', { fileName: attachment.fileName })}
            </p>
          )}

          <TextField
            label={t('documents.documentTitle')}
            value={title}
            onChange={(event) => setTitle(event.target.value)}
            error={errors.title}
          />

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="flex flex-col gap-1.5">
              <label htmlFor="inbox-save-type" className="text-xs font-medium text-ink-secondary">
                {t('documents.type')}
              </label>
              <select
                id="inbox-save-type"
                value={typeId}
                onChange={(event) => {
                  setTypeId(event.target.value)
                  setMetadata({})
                }}
                className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
              >
                <option value="">-</option>
                {typesQuery.data?.map((type) => (
                  <option key={type.id} value={type.id}>
                    {type.name}
                  </option>
                ))}
              </select>
              {errors.type && (
                <p role="alert" className="text-xs font-medium text-red-600">
                  {errors.type}
                </p>
              )}
            </div>

            <div className="flex flex-col gap-1.5">
              <label htmlFor="inbox-save-department" className="text-xs font-medium text-ink-secondary">
                {t('documents.department')}
              </label>
              <select
                id="inbox-save-department"
                value={departmentId}
                onChange={(event) => setDepartmentId(event.target.value)}
                className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
              >
                <option value="">-</option>
                {departments.map((department) => (
                  <option key={department.id} value={department.id}>
                    {department.name}
                  </option>
                ))}
              </select>
            </div>
          </div>

          <TextField
            label={t('documents.tags')}
            value={tagsText}
            onChange={(event) => setTagsText(event.target.value)}
            hint="fatura, urgente"
          />

          <DocumentFieldsFieldset
            selectedType={selectedType}
            metadata={metadata}
            onChange={(code, value) => setMetadata((current) => ({ ...current, [code]: value }))}
            errors={errors}
          />

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
            <Button type="submit" loading={isPending}>
              {isPending ? t('documents.uploading') : t('inbox.actions.save')}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
