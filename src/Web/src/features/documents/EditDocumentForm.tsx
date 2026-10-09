import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useUpdateDocument } from './api'
import { MetadataFieldType, SecurityClassification, type DocumentDetail } from './types'
import { Button } from '@/components/ui/Button'
import { TextField } from '@/components/ui/Field'
import { Card, CardBody, CardHeader } from '@/components/ui/Surface'
import type { Department } from '@/lib/types'

interface EditDocumentFormProps {
  document: DocumentDetail
  departments: Department[]
  onDone: () => void
}

/**
 * Edits metadata in place. The concurrency token received with the document travels back with the
 * save, so an edit based on a stale view is rejected instead of overwriting someone else's change.
 */
export function EditDocumentForm({ document, departments, onDone }: EditDocumentFormProps) {
  const { t } = useTranslation()
  const update = useUpdateDocument(document.summary.id)

  const [title, setTitle] = useState(document.summary.title)
  const [description, setDescription] = useState(document.description ?? '')
  const [departmentId, setDepartmentId] = useState(document.departmentId ?? '')
  const [businessEntity, setBusinessEntity] = useState(document.businessEntity ?? '')
  const [classification, setClassification] = useState<number>(document.summary.classification)
  const [tagsText, setTagsText] = useState(document.summary.tags.map((tag) => tag.name).join(', '))
  const [metadata, setMetadata] = useState<Record<string, string>>(
    Object.fromEntries(
      document.metadata.map((field) => [
        field.code,
        field.fieldType === MetadataFieldType.Date && field.value
          ? field.value.slice(0, 10)
          : (field.value ?? ''),
      ]),
    ),
  )

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()

    try {
      await update.mutateAsync({
        title: title.trim(),
        description: description.trim() || undefined,
        departmentId: departmentId || null,
        businessEntity: businessEntity.trim() || null,
        classification,
        tags: tagsText
          .split(',')
          .map((tag) => tag.trim())
          .filter(Boolean),
        metadata,
        concurrencyToken: document.concurrencyToken,
      })

      onDone()
    } catch {
      // Rendered from update.error below.
    }
  }

  const serverError = update.error
    ? t([`documents.errors.${update.error.code}`, 'errors.genericBody'] as const)
    : null

  return (
    <Card>
      <CardHeader title={t('documents.edit')} />
      <CardBody>
        <form onSubmit={submit} className="flex flex-col gap-4">
          <TextField
            label={t('documents.documentTitle')}
            value={title}
            onChange={(event) => setTitle(event.target.value)}
          />

          <div className="flex flex-col gap-1.5">
            <label htmlFor="edit-description" className="text-xs font-medium text-ink-secondary">
              {t('documents.overview.description')}
            </label>
            <textarea
              id="edit-description"
              rows={3}
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              className="rounded-md border border-strong bg-raised p-2 text-sm text-ink"
            />
          </div>

          <div className="grid gap-4 sm:grid-cols-3">
            <div className="flex flex-col gap-1.5">
              <label htmlFor="edit-department" className="text-xs font-medium text-ink-secondary">
                {t('documents.department')}
              </label>
              <select
                id="edit-department"
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

            <div className="flex flex-col gap-1.5">
              <label htmlFor="edit-classification" className="text-xs font-medium text-ink-secondary">
                {t('documents.classification')}
              </label>
              <select
                id="edit-classification"
                value={classification}
                onChange={(event) => setClassification(Number(event.target.value))}
                className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
              >
                {Object.values(SecurityClassification).map((value) => (
                  <option key={value} value={value}>
                    {t(`documents.classificationValues.${value}`)}
                  </option>
                ))}
              </select>
            </div>

            <TextField
              label={t('documents.overview.businessEntity')}
              value={businessEntity}
              onChange={(event) => setBusinessEntity(event.target.value)}
            />
          </div>

          <TextField
            label={t('documents.tags')}
            value={tagsText}
            onChange={(event) => setTagsText(event.target.value)}
          />

          {document.metadata.length > 0 && (
            <fieldset className="rounded-md border border-subtle p-3">
              <legend className="px-1 text-xs font-semibold text-ink-secondary">
                {t('documents.overview.metadata')}
              </legend>
              <div className="grid gap-3 sm:grid-cols-2">
                {document.metadata.map((field) => (
                  <div key={field.fieldId} className="flex flex-col gap-1.5">
                    <label
                      htmlFor={`edit-meta-${field.code}`}
                      className="text-xs font-medium text-ink-secondary"
                    >
                      {field.label}
                      {field.isRequired && <span className="text-red-600"> *</span>}
                    </label>
                    <input
                      id={`edit-meta-${field.code}`}
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
            <Button type="button" variant="secondary" onClick={onDone}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" loading={update.isPending}>
              {update.isPending ? t('documents.saving') : t('documents.save')}
            </Button>
          </div>
        </form>
      </CardBody>
    </Card>
  )
}
