import { useTranslation } from 'react-i18next'
import { MetadataFieldType, type DocumentType } from '@/features/documents/types'

interface DocumentFieldsFieldsetProps {
  selectedType: DocumentType | undefined
  metadata: Record<string, string>
  onChange: (code: string, value: string) => void
  errors: Record<string, string>
}

/**
 * The metadata fields a document type requires, read from the type itself rather than hard-coded —
 * adding a field on the server changes this form. Shared between every place in the inbox that turns
 * something into a document, so the fields behave identically wherever they appear.
 */
export function DocumentFieldsFieldset({
  selectedType,
  metadata,
  onChange,
  errors,
}: DocumentFieldsFieldsetProps) {
  const { t } = useTranslation()

  if (!selectedType || selectedType.fields.length === 0) {
    return null
  }

  return (
    <fieldset className="rounded-md border border-subtle p-3">
      <legend className="px-1 text-xs font-semibold text-ink-secondary">
        {t('documents.overview.metadata')}
      </legend>
      <div className="grid gap-3 sm:grid-cols-2">
        {selectedType.fields.map((field) => (
          <div key={field.id} className="flex flex-col gap-1.5">
            <label htmlFor={`inbox-meta-${field.code}`} className="text-xs font-medium text-ink-secondary">
              {field.label}
              {field.isRequired && <span className="text-red-600"> *</span>}
            </label>

            {field.fieldType === MetadataFieldType.Select ? (
              <select
                id={`inbox-meta-${field.code}`}
                value={metadata[field.code] ?? ''}
                onChange={(event) => onChange(field.code, event.target.value)}
                className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
              >
                <option value="">—</option>
                {field.options.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            ) : (
              <input
                id={`inbox-meta-${field.code}`}
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
                onChange={(event) => onChange(field.code, event.target.value)}
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
  )
}
