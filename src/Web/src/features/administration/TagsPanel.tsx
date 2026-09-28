import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Pencil, RotateCcw, Trash2 } from 'lucide-react'
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
  useDeleteTag,
  useRestoreTag,
  useSaveTag,
  useTags,
  type AdminTag,
  type SaveTagInput,
} from './governance'

const EMPTY: SaveTagInput = { name: '', colour: '' }

/**
 * Etiquetas da organização.
 *
 * A diferença em relação aos campos de metadados é deliberada e vale a pena vê-la no ecrã: um valor
 * de metadados é o que o documento **afirma**, e por isso não se apaga; uma etiqueta é uma anotação
 * **colocada sobre** ele, e por isso remover é permitido — mas reversível. As ligações aos
 * documentos sobrevivem, e restaurar traz tudo de volta.
 */
export function TagsPanel() {
  const { t } = useTranslation()

  const [includeDeleted, setIncludeDeleted] = useState(false)
  const [search, setSearch] = useState('')
  const [draft, setDraft] = useState<SaveTagInput & { id?: string }>({ ...EMPTY })

  const tags = useTags(includeDeleted)
  const saveTag = useSaveTag()
  const deleteTag = useDeleteTag()
  const restoreTag = useRestoreTag()

  // Remover e restaurar são ações de linha; guardar é do formulário. A recusa aparece no sítio
  // onde o botão foi carregado.
  // Quarenta e tal etiquetas numa lista corrida não se percorrem: procura-se. Filtrado no cliente
  // de propósito — a lista já veio toda, e ir ao servidor a cada tecla seria trabalho sem resposta
  // nova.
  const visible = (tags.data ?? []).filter((tag) =>
    tag.name.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase()),
  )

  const rowError = deleteTag.error ?? restoreTag.error
  const formError = saveTag.error
  const startNew = () => setDraft({ ...EMPTY })

  const edit = (tag: AdminTag) => setDraft({ id: tag.id, name: tag.name, colour: tag.colour ?? '' })

  const submit = (event: React.FormEvent) => {
    event.preventDefault()

    const { id, ...input } = draft

    saveTag.mutate(
      { id, input: { name: input.name.trim(), colour: input.colour?.trim() || null } },
      { onSuccess: startNew },
    )
  }

  return (
    <div className="grid gap-4 lg:grid-cols-[1fr_22rem]">
      <Card>
        <CardHeader
          title={t('admin.tags.title')}
          description={t('admin.tags.subtitle')}
          actions={
            <div className="flex flex-wrap items-center gap-3">
              <input
                type="search"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder={t('admin.tags.search')}
                aria-label={t('admin.tags.search')}
                className="h-8 w-44 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
              />
              <Checkbox
                label={t('admin.tags.includeDeleted')}
                checked={includeDeleted}
                onChange={(event) => setIncludeDeleted(event.target.checked)}
              />
            </div>
          }
        />
        <CardBody className="p-0">
          {rowError && <Refusal message={rowError.message} className="mx-4 mt-3" />}

          {tags.isLoading && <LoadingState label={t('common.loading')} />}
          {tags.isError && <ErrorState title={t('errors.genericTitle')} body={tags.error.message} />}

          {tags.data && visible.length === 0 && (
            <EmptyState title={t('admin.tags.noneTitle')} body={t('admin.tags.noneBody')} />
          )}

          {tags.data && visible.length > 0 && (
            <table className="w-full text-sm">
              <thead className="border-b border-subtle text-left text-xs text-ink-muted">
                <tr>
                  <th scope="col" className="px-4 py-2 font-medium">
                    {t('admin.tags.tag')}
                  </th>
                  <th scope="col" className="px-4 py-2 font-medium">
                    {t('admin.tags.documents')}
                  </th>
                  <th scope="col" className="px-4 py-2" />
                </tr>
              </thead>
              <tbody className="divide-y divide-subtle">
                {visible.map((tag) => (
                  <tr key={tag.id} className={tag.isDeleted ? 'opacity-60' : undefined}>
                    <td className="px-4 py-2">
                      <span className="flex items-center gap-2">
                        {tag.colour && (
                          <span
                            aria-hidden
                            className="size-3 shrink-0 rounded-full border border-subtle"
                            style={{ backgroundColor: tag.colour }}
                          />
                        )}
                        <span className="text-ink">{tag.name}</span>
                        {tag.isDeleted && <Badge tone="neutral">{t('admin.tags.removed')}</Badge>}
                      </span>
                    </td>
                    <td className="px-4 py-2 tabular-nums text-ink">{tag.documentCount}</td>
                    <td className="px-4 py-2">
                      <div className="flex justify-end gap-1">
                        {tag.isDeleted ? (
                          <Button
                            variant="ghost"
                            size="sm"
                            loading={restoreTag.isPending}
                            onClick={() => restoreTag.mutate(tag.id)}
                          >
                            <RotateCcw aria-hidden className="size-4" />
                            {t('admin.tags.restore')}
                          </Button>
                        ) : (
                          <>
                            <Button variant="ghost" size="sm" onClick={() => edit(tag)}>
                              <Pencil aria-hidden className="size-4" />
                              {t('common.edit')}
                            </Button>
                            <Button
                              variant="ghost"
                              size="sm"
                              loading={deleteTag.isPending}
                              onClick={() => deleteTag.mutate(tag.id)}
                            >
                              <Trash2 aria-hidden className="size-4" />
                              {t('admin.tags.remove')}
                            </Button>
                          </>
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
          title={draft.id ? t('admin.tags.editTitle') : t('admin.tags.createTitle')}
          actions={
            draft.id && (
              <Button variant="ghost" size="sm" onClick={startNew}>
                {t('admin.tags.createTitle')}
              </Button>
            )
          }
        />
        <CardBody>
          <form onSubmit={submit} aria-label={t('admin.tags.formLabel')} className="flex flex-col gap-3">
            {formError && <Refusal message={formError.message} />}

            <TextField
              label={t('admin.tags.name')}
              value={draft.name}
              onChange={(event) => setDraft({ ...draft, name: event.target.value })}
              required
            />
            <TextField
              label={t('admin.tags.colour')}
              value={draft.colour ?? ''}
              onChange={(event) => setDraft({ ...draft, colour: event.target.value })}
              hint={t('admin.tags.colourHint')}
            />

            <div className="flex justify-end gap-2">
              {draft.id && (
                <Button type="button" variant="secondary" size="sm" onClick={startNew}>
                  {t('common.cancel')}
                </Button>
              )}
              <Button type="submit" size="sm" loading={saveTag.isPending}>
                {draft.id ? t('common.save') : t('admin.tags.create')}
              </Button>
            </div>
          </form>
        </CardBody>
      </Card>
    </div>
  )
}

/**
 * A resposta do servidor, mostrada onde a ação foi pedida.
 *
 * Uma ação de linha e um formulário são dois sítios diferentes do ecrã: mostrar a recusa de uma
 * dentro do outro obriga quem carregou no botão a procurar a resposta.
 */
function Refusal({ message, className }: { message: string; className?: string }) {
  return (
    <p
      role="alert"
      className={clsx(
        'rounded-md border border-red-200 bg-red-50 px-3 py-2 text-xs font-medium text-red-800 dark:border-red-900 dark:bg-red-950 dark:text-red-200',
        className,
      )}
    >
      {message}
    </p>
  )
}
