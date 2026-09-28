import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Pencil, Plus, Trash2 } from 'lucide-react'
import clsx from 'clsx'
import { Button } from '@/components/ui/Button'
import { Checkbox, TextField } from '@/components/ui/Field'
import {
  Badge,
  Card,
  CardBody,
  CardHeader,
  EmptyState,
  ErrorState,
  LoadingState,
} from '@/components/ui/Surface'
import {
  FieldType,
  useDeleteField,
  useDocumentTypes,
  useRetentionPolicies,
  useSaveDocumentType,
  useSaveField,
  useSetTypeActive,
  useTypeFields,
  type AdminDocumentType,
  type AdminMetadataField,
  type FieldTypeValue,
  type SaveDocumentTypeInput,
  type SaveFieldInput,
} from './catalogue'

const EMPTY_TYPE: SaveDocumentTypeInput = {
  code: '',
  name: '',
  referencePrefix: '',
  defaultClassification: 1,
  defaultRetentionPolicyId: null,
}

const EMPTY_FIELD: SaveFieldInput = {
  code: '',
  label: '',
  fieldType: FieldType.text,
  isRequired: false,
  displayOrder: 0,
  options: [],
}

const FIELD_TYPES: FieldTypeValue[] = [
  FieldType.text,
  FieldType.number,
  FieldType.date,
  FieldType.boolean,
  FieldType.currency,
  FieldType.select,
]

const CLASSIFICATIONS = [0, 1, 2, 3]

/**
 * Tipos de documento e o esquema de metadados de cada um.
 *
 * O que este ecrã tem de diferente de um editor de tabelas é que já existem documentos por baixo
 * destas definições. As recusas do servidor são mostradas com o número que as explica — «7
 * documentos têm valor neste campo» — porque «não pode» não é uma resposta.
 */
export function DocumentCataloguePanel() {
  const { t } = useTranslation()

  const [includeInactive, setIncludeInactive] = useState(false)
  const [selectedTypeId, setSelectedTypeId] = useState<string | null>(null)
  const [typeDraft, setTypeDraft] = useState<SaveDocumentTypeInput & { id?: string }>({ ...EMPTY_TYPE })
  const [fieldDraft, setFieldDraft] = useState<SaveFieldInput & { id?: string }>({ ...EMPTY_FIELD })
  const [optionsText, setOptionsText] = useState('')

  const types = useDocumentTypes(includeInactive)
  const fields = useTypeFields(selectedTypeId)
  const policies = useRetentionPolicies()

  const saveType = useSaveDocumentType()
  const setActive = useSetTypeActive()
  const saveField = useSaveField()
  const deleteField = useDeleteField()

  // Separados de propósito: uma recusa tem de aparecer onde a ação foi pedida. Um único aviso no
  // topo da página deixa quem submeteu o formulário do fundo sem ver o que o servidor respondeu.
  const typeError = saveType.error ?? setActive.error
  const fieldError = saveField.error ?? deleteField.error
  const selected = types.data?.find((type) => type.id === selectedTypeId) ?? null

  const startNewType = () => setTypeDraft({ ...EMPTY_TYPE })

  const editType = (type: AdminDocumentType) =>
    setTypeDraft({
      id: type.id,
      code: type.code,
      name: type.name,
      description: type.description ?? '',
      referencePrefix: type.referencePrefix,
      defaultClassification: type.defaultClassification,
      defaultRetentionPolicyId: type.defaultRetentionPolicyId,
    })

  const startNewField = () => {
    setFieldDraft({ ...EMPTY_FIELD })
    setOptionsText('')
  }

  const editField = (field: AdminMetadataField) => {
    setFieldDraft({
      id: field.id,
      code: field.code,
      label: field.label,
      fieldType: field.fieldType,
      isRequired: field.isRequired,
      displayOrder: field.displayOrder,
      helpText: field.helpText ?? '',
      options: field.options,
    })
    setOptionsText(field.options.join('\n'))
  }

  const submitType = (event: React.FormEvent) => {
    event.preventDefault()

    const { id, ...input } = typeDraft

    saveType.mutate(
      {
        id,
        input: {
          ...input,
          code: input.code.trim(),
          name: input.name.trim(),
          description: input.description?.trim() || undefined,
          referencePrefix: input.referencePrefix.trim().toUpperCase(),
          defaultRetentionPolicyId: input.defaultRetentionPolicyId || null,
        },
      },
      { onSuccess: startNewType },
    )
  }

  const submitField = (event: React.FormEvent) => {
    event.preventDefault()

    if (selectedTypeId === null) {
      return
    }

    const { id, ...input } = fieldDraft

    saveField.mutate(
      {
        typeId: selectedTypeId,
        fieldId: id,
        input: {
          ...input,
          code: input.code.trim(),
          label: input.label.trim(),
          helpText: input.helpText?.trim() || undefined,
          options:
            input.fieldType === FieldType.select
              ? optionsText
                  .split('\n')
                  .map((option) => option.trim())
                  .filter((option) => option.length > 0)
              : [],
        },
      },
      { onSuccess: startNewField },
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="grid gap-4 lg:grid-cols-[1fr_22rem]">
        <Card>
          <CardHeader
            title={t('admin.types.title')}
            actions={
              <Checkbox
                label={t('admin.types.includeInactive')}
                checked={includeInactive}
                onChange={(event) => setIncludeInactive(event.target.checked)}
              />
            }
          />
          <CardBody className="p-0">
            {types.isLoading && <LoadingState label={t('common.loading')} />}
            {types.isError && <ErrorState title={t('errors.genericTitle')} body={types.error.message} />}

            {types.data && (
              <table className="w-full text-sm">
                <thead className="border-b border-subtle text-left text-xs text-ink-muted">
                  <tr>
                    <th scope="col" className="px-4 py-2 font-medium">
                      {t('admin.types.type')}
                    </th>
                    <th scope="col" className="px-4 py-2 font-medium">
                      {t('admin.types.prefix')}
                    </th>
                    <th scope="col" className="px-4 py-2 font-medium">
                      {t('admin.types.fields')}
                    </th>
                    <th scope="col" className="px-4 py-2 font-medium">
                      {t('admin.types.documents')}
                    </th>
                    <th scope="col" className="px-4 py-2" />
                  </tr>
                </thead>
                <tbody className="divide-y divide-subtle">
                  {types.data.map((type) => (
                    <tr
                      key={type.id}
                      className={clsx(
                        type.isActive ? undefined : 'opacity-60',
                        type.id === selectedTypeId && 'bg-sunken',
                      )}
                    >
                      <td className="px-4 py-2">
                        <span className="block text-ink">{type.name}</span>
                        <span className="block font-mono text-xs text-ink-muted">{type.code}</span>
                        <span className="flex flex-wrap gap-1">
                          {type.isIntakeType && <Badge tone="teal">{t('admin.types.intake')}</Badge>}
                          {!type.isActive && <Badge tone="neutral">{t('admin.types.inactive')}</Badge>}
                        </span>
                      </td>
                      <td className="px-4 py-2 font-mono text-xs text-ink-secondary">
                        {type.referencePrefix}
                      </td>
                      <td className="px-4 py-2 tabular-nums text-ink">{type.fieldCount}</td>
                      <td className="px-4 py-2 tabular-nums text-ink">{type.documentCount}</td>
                      <td className="px-4 py-2">
                        <div className="flex justify-end gap-1">
                          <Button variant="ghost" size="sm" onClick={() => setSelectedTypeId(type.id)}>
                            {t('admin.types.manageFields')}
                          </Button>
                          <Button variant="ghost" size="sm" onClick={() => editType(type)}>
                            <Pencil aria-hidden className="size-4" />
                            {t('common.edit')}
                          </Button>
                          {type.isIntakeType ? (
                            // Não é uma limitação da interface: o servidor recusa na mesma. É por
                            // este tipo que entra um ficheiro antes de alguém saber o que é.
                            <span className="self-center px-2 text-[11px] text-ink-muted">
                              {t('admin.types.alwaysActive')}
                            </span>
                          ) : (
                            <Button
                              variant="ghost"
                              size="sm"
                              loading={setActive.isPending}
                              onClick={() => setActive.mutate({ id: type.id, isActive: !type.isActive })}
                            >
                              {type.isActive ? t('admin.types.deactivate') : t('admin.types.activate')}
                            </Button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </CardBody>
        </Card>

        <Card>
          <CardHeader
            title={typeDraft.id ? t('admin.types.editTitle') : t('admin.types.createTitle')}
            actions={
              typeDraft.id && (
                <Button variant="ghost" size="sm" onClick={startNewType}>
                  <Plus aria-hidden className="size-4" />
                  {t('admin.types.createTitle')}
                </Button>
              )
            }
          />
          <CardBody>
            <form
              onSubmit={submitType}
              aria-label={t('admin.types.formLabel')}
              className="flex flex-col gap-3"
            >
              {typeError && <Refusal message={typeError.message} />}
              <TextField
                label={t('admin.types.name')}
                value={typeDraft.name}
                onChange={(event) => setTypeDraft({ ...typeDraft, name: event.target.value })}
                required
              />
              <TextField
                label={t('admin.types.code')}
                value={typeDraft.code}
                onChange={(event) => setTypeDraft({ ...typeDraft, code: event.target.value })}
                hint={t('admin.types.codeHint')}
                required
              />
              <TextField
                label={t('admin.types.prefix')}
                value={typeDraft.referencePrefix}
                onChange={(event) => setTypeDraft({ ...typeDraft, referencePrefix: event.target.value })}
                hint={t('admin.types.prefixHint')}
                required
              />
              <TextField
                label={t('admin.types.description')}
                value={typeDraft.description ?? ''}
                onChange={(event) => setTypeDraft({ ...typeDraft, description: event.target.value })}
              />

              <div className="flex flex-col gap-1.5">
                <label htmlFor="type-classification" className="text-xs font-medium text-ink-secondary">
                  {t('admin.types.classification')}
                </label>
                <select
                  id="type-classification"
                  value={typeDraft.defaultClassification}
                  onChange={(event) =>
                    setTypeDraft({ ...typeDraft, defaultClassification: Number(event.target.value) })
                  }
                  className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
                >
                  {CLASSIFICATIONS.map((value) => (
                    <option key={value} value={value}>
                      {t(`documents.classificationValues.${value}`)}
                    </option>
                  ))}
                </select>
              </div>

              <div className="flex flex-col gap-1.5">
                <label htmlFor="type-retention" className="text-xs font-medium text-ink-secondary">
                  {t('admin.types.retention')}
                </label>
                <select
                  id="type-retention"
                  value={typeDraft.defaultRetentionPolicyId ?? ''}
                  onChange={(event) =>
                    setTypeDraft({ ...typeDraft, defaultRetentionPolicyId: event.target.value || null })
                  }
                  className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
                >
                  <option value="">—</option>
                  {policies.data?.map((policy) => (
                    <option key={policy.id} value={policy.id}>
                      {policy.name}
                    </option>
                  ))}
                </select>
              </div>

              <div className="flex justify-end gap-2">
                {typeDraft.id && (
                  <Button type="button" variant="secondary" size="sm" onClick={startNewType}>
                    {t('common.cancel')}
                  </Button>
                )}
                <Button type="submit" size="sm" loading={saveType.isPending}>
                  {typeDraft.id ? t('common.save') : t('admin.types.create')}
                </Button>
              </div>
            </form>
          </CardBody>
        </Card>
      </div>

      {selected && (
        <div className="grid gap-4 lg:grid-cols-[1fr_22rem]">
          <Card>
            <CardHeader
              title={t('admin.fields.title', { type: selected.name })}
              description={t('admin.fields.subtitle')}
              actions={
                <Button variant="ghost" size="sm" onClick={() => setSelectedTypeId(null)}>
                  {t('common.close')}
                </Button>
              }
            />
            <CardBody className="p-0">
              {fields.isLoading && <LoadingState label={t('common.loading')} />}
              {fields.data?.length === 0 && (
                <EmptyState title={t('admin.fields.emptyTitle')} body={t('admin.fields.emptyBody')} />
              )}

              {fields.data && fields.data.length > 0 && (
                <table className="w-full text-sm">
                  <thead className="border-b border-subtle text-left text-xs text-ink-muted">
                    <tr>
                      <th scope="col" className="px-4 py-2 font-medium">
                        {t('admin.fields.field')}
                      </th>
                      <th scope="col" className="px-4 py-2 font-medium">
                        {t('admin.fields.kind')}
                      </th>
                      <th scope="col" className="px-4 py-2 font-medium">
                        {t('admin.fields.filled')}
                      </th>
                      <th scope="col" className="px-4 py-2" />
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-subtle">
                    {fields.data.map((field) => (
                      <tr key={field.id}>
                        <td className="px-4 py-2">
                          <span className="block text-ink">{field.label}</span>
                          <span className="block font-mono text-xs text-ink-muted">{field.code}</span>
                          {field.isRequired && <Badge tone="amber">{t('admin.fields.required')}</Badge>}
                        </td>
                        <td className="px-4 py-2 text-xs text-ink-secondary">
                          {t(`admin.fields.kinds.${field.fieldType}`)}
                          {field.options.length > 0 && (
                            <span className="block text-ink-muted">{field.options.join(' · ')}</span>
                          )}
                        </td>
                        <td className="px-4 py-2 text-xs tabular-nums text-ink-secondary">
                          {field.valueCount}
                        </td>
                        <td className="px-4 py-2">
                          <div className="flex justify-end gap-1">
                            <Button variant="ghost" size="sm" onClick={() => editField(field)}>
                              <Pencil aria-hidden className="size-4" />
                              {t('common.edit')}
                            </Button>
                            {field.canDelete ? (
                              <Button
                                variant="ghost"
                                size="sm"
                                loading={deleteField.isPending}
                                onClick={() => deleteField.mutate({ typeId: selected.id, fieldId: field.id })}
                              >
                                <Trash2 aria-hidden className="size-4" />
                                {t('common.delete')}
                              </Button>
                            ) : (
                              // O número é a explicação. Sem ele, o botão ausente seria um mistério.
                              <span className="self-center px-2 text-right text-[11px] text-ink-muted">
                                {t('admin.fields.inUse', { count: field.valueCount })}
                              </span>
                            )}
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </CardBody>
          </Card>

          <Card>
            <CardHeader title={fieldDraft.id ? t('admin.fields.editTitle') : t('admin.fields.createTitle')} />
            <CardBody>
              <form
                onSubmit={submitField}
                aria-label={t('admin.fields.formLabel')}
                className="flex flex-col gap-3"
              >
                {fieldError && <Refusal message={fieldError.message} />}
                <TextField
                  label={t('admin.fields.label')}
                  value={fieldDraft.label}
                  onChange={(event) => setFieldDraft({ ...fieldDraft, label: event.target.value })}
                  required
                />
                <TextField
                  label={t('admin.fields.code')}
                  value={fieldDraft.code}
                  onChange={(event) => setFieldDraft({ ...fieldDraft, code: event.target.value })}
                  required
                />

                <div className="flex flex-col gap-1.5">
                  <label htmlFor="field-kind" className="text-xs font-medium text-ink-secondary">
                    {t('admin.fields.kind')}
                  </label>
                  <select
                    id="field-kind"
                    value={fieldDraft.fieldType}
                    onChange={(event) =>
                      setFieldDraft({
                        ...fieldDraft,
                        fieldType: Number(event.target.value) as FieldTypeValue,
                      })
                    }
                    className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
                  >
                    {FIELD_TYPES.map((value) => (
                      <option key={value} value={value}>
                        {t(`admin.fields.kinds.${value}`)}
                      </option>
                    ))}
                  </select>
                </div>

                {fieldDraft.fieldType === FieldType.select && (
                  <div className="flex flex-col gap-1.5">
                    <label htmlFor="field-options" className="text-xs font-medium text-ink-secondary">
                      {t('admin.fields.options')}
                    </label>
                    <textarea
                      id="field-options"
                      value={optionsText}
                      onChange={(event) => setOptionsText(event.target.value)}
                      rows={4}
                      className="rounded-md border border-strong bg-raised px-2 py-1.5 text-sm text-ink"
                    />
                    <p className="text-[11px] text-ink-muted">{t('admin.fields.optionsHint')}</p>
                  </div>
                )}

                <TextField
                  label={t('admin.fields.helpText')}
                  value={fieldDraft.helpText ?? ''}
                  onChange={(event) => setFieldDraft({ ...fieldDraft, helpText: event.target.value })}
                />
                <TextField
                  label={t('admin.fields.order')}
                  type="number"
                  value={String(fieldDraft.displayOrder)}
                  onChange={(event) =>
                    setFieldDraft({ ...fieldDraft, displayOrder: Number(event.target.value) })
                  }
                />

                <Checkbox
                  label={t('admin.fields.required')}
                  checked={fieldDraft.isRequired}
                  onChange={(event) => setFieldDraft({ ...fieldDraft, isRequired: event.target.checked })}
                />
                <p className="text-[11px] text-ink-muted">{t('admin.fields.requiredHint')}</p>

                <div className="flex justify-end gap-2">
                  {fieldDraft.id && (
                    <Button type="button" variant="secondary" size="sm" onClick={startNewField}>
                      {t('common.cancel')}
                    </Button>
                  )}
                  <Button type="submit" size="sm" loading={saveField.isPending}>
                    {fieldDraft.id ? t('common.save') : t('admin.fields.create')}
                  </Button>
                </div>
              </form>
            </CardBody>
          </Card>
        </div>
      )}
    </div>
  )
}

/**
 * A resposta do servidor, mostrada onde a ação foi pedida.
 *
 * O texto vem do serviço e traz o número que explica a recusa — «8 documentos deste tipo ficariam
 * inválidos» — porque uma recusa sem o número é indistinguível de uma avaria.
 */
function Refusal({ message }: { message: string }) {
  return (
    <p
      role="alert"
      className="mb-3 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-xs font-medium text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200"
    >
      {message}
    </p>
  )
}
