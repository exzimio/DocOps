import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import { useCreateDocument, useDocumentTypes } from './api'
import { MetadataFieldType, type DocumentType } from './types'
import { Button } from '@/components/ui/Button'
import { TextField } from '@/components/ui/Field'
import type { Department } from '@/lib/types'

interface UploadDialogProps {
  departments: Department[]
  onClose: () => void
  onCreated: (documentId: string) => void
}

/**
 * Creates a document from a file plus the metadata its type requires. The field list is not
 * hard-coded: it is read from the document type, so adding a field on the server changes this form.
 */
export function UploadDialog({ departments, onClose, onCreated }: UploadDialogProps) {
  const { t } = useTranslation()
  const dialogRef = useRef<HTMLDivElement>(null)
  const typesQuery = useDocumentTypes()
  const createDocument = useCreateDocument()

  const [file, setFile] = useState<File | null>(null)
  const [title, setTitle] = useState('')
  const [typeId, setTypeId] = useState('')
  const [departmentId, setDepartmentId] = useState('')
  const [description, setDescription] = useState('')
  const [tagsText, setTagsText] = useState('')
  const [metadata, setMetadata] = useState<Record<string, string>>({})
  const [errors, setErrors] = useState<Record<string, string>>({})

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
    if (!file) next.file = t('documents.validation.fileRequired')
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

    if (!validate() || !file) {
      return
    }

    try {
      const created = await createDocument.mutateAsync({
        file,
        title: title.trim(),
        documentTypeId: typeId,
        departmentId: departmentId || undefined,
        description: description.trim() || undefined,
        tags: tagsText
          .split(',')
          .map((tag) => tag.trim())
          .filter(Boolean),
        metadata,
      })

      onCreated(created.summary.id)
    } catch {
      // Surfaced from createDocument.error below.
    }
  }

  const serverError = createDocument.error
    ? t([`documents.errors.${createDocument.error.code}`, 'errors.genericBody'] as const)
    : null

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-graphite-950/50 p-4"
      role="dialog"
      aria-modal="true"
      aria-label={t('documents.uploadTitle')}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <div
        ref={dialogRef}
        className="mt-8 w-full max-w-2xl rounded-lg border border-subtle bg-raised shadow-overlay"
      >
        <div className="flex items-center justify-between border-b border-subtle px-4 py-3">
          <h2 className="text-sm font-semibold text-ink">{t('documents.uploadTitle')}</h2>
          <Button variant="ghost" size="sm" onClick={onClose} aria-label={t('common.close')}>
            <X aria-hidden className="size-4" />
          </Button>
        </div>

        <form onSubmit={submit} className="flex flex-col gap-4 p-4">
          <div className="flex flex-col gap-1.5">
            <label htmlFor="upload-file" className="text-xs font-medium text-ink-secondary">
              {t('documents.uploadFile')}
            </label>
            <input
              id="upload-file"
              type="file"
              onChange={(event) => setFile(event.target.files?.[0] ?? null)}
              className="rounded-md border border-strong bg-raised p-2 text-sm text-ink file:mr-3 file:rounded-sm file:border-0 file:bg-sunken file:px-2 file:py-1 file:text-xs file:text-ink"
            />
            {errors.file && (
              <p role="alert" className="text-xs font-medium text-red-600">
                {errors.file}
              </p>
            )}
          </div>

          <TextField
            label={t('documents.documentTitle')}
            value={title}
            onChange={(event) => setTitle(event.target.value)}
            error={errors.title}
          />

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="flex flex-col gap-1.5">
              <label htmlFor="upload-type" className="text-xs font-medium text-ink-secondary">
                {t('documents.type')}
              </label>
              <select
                id="upload-type"
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
              <label htmlFor="upload-department" className="text-xs font-medium text-ink-secondary">
                {t('documents.department')}
              </label>
              <select
                id="upload-department"
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

          <div className="flex flex-col gap-1.5">
            <label htmlFor="upload-description" className="text-xs font-medium text-ink-secondary">
              {t('documents.overview.description')}
            </label>
            <textarea
              id="upload-description"
              rows={2}
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              className="rounded-md border border-strong bg-raised p-2 text-sm text-ink"
            />
          </div>

          {selectedType && selectedType.fields.length > 0 && (
            <fieldset className="rounded-md border border-subtle p-3">
              <legend className="px-1 text-xs font-semibold text-ink-secondary">
                {t('documents.overview.metadata')}
              </legend>
              <div className="grid gap-3 sm:grid-cols-2">
                {selectedType.fields.map((field) => (
                  <div key={field.id} className="flex flex-col gap-1.5">
                    <label htmlFor={`meta-${field.code}`} className="text-xs font-medium text-ink-secondary">
                      {field.label}
                      {field.isRequired && <span className="text-red-600"> *</span>}
                    </label>

                    {field.fieldType === MetadataFieldType.Select ? (
                      <select
                        id={`meta-${field.code}`}
                        value={metadata[field.code] ?? ''}
                        onChange={(event) =>
                          setMetadata((current) => ({ ...current, [field.code]: event.target.value }))
                        }
                        className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
                      >
                        <option value="">-</option>
                        {field.options.map((option) => (
                          <option key={option} value={option}>
                            {option}
                          </option>
                        ))}
                      </select>
                    ) : (
                      <input
                        id={`meta-${field.code}`}
                        type={
                          field.fieldType === MetadataFieldType.Date
                            ? 'date'
                            : field.fieldType === MetadataFieldType.Number ||
                                field.fieldType === MetadataFieldType.Currency
                              ? 'number'
                              : 'text'
                        }
                        step={field.fieldType === MetadataFieldType.Currency ? '0.01' : undefined}
                        value={metadata[field.code] ?? ''}
                        onChange={(event) =>
                          setMetadata((current) => ({ ...current, [field.code]: event.target.value }))
                        }
                        className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
                      />
                    )}

                    {errors[field.code] && (
                      <p role="alert" className="text-xs font-medium text-red-600">
                        {errors[field.code]}
                      </p>
                    )}
                  </div>
                ))}
              </div>
            </fieldset>
          )}

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
            <Button type="submit" loading={createDocument.isPending}>
              {createDocument.isPending ? t('documents.uploading') : t('documents.uploadSubmit')}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
