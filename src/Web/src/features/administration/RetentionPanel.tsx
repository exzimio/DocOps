import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Pencil } from 'lucide-react'
import clsx from 'clsx'
import { Button } from '@/components/ui/Button'
import { TextField } from '@/components/ui/Field'
import { Badge, Card, CardBody, CardHeader, ErrorState, LoadingState } from '@/components/ui/Surface'
import { useRetentionPolicies, type AdminRetentionPolicy } from './catalogue'
import {
  RetentionTrigger,
  useSaveRetentionPolicy,
  useSetPolicyActive,
  type SaveRetentionPolicyInput,
} from './governance'

const EMPTY: SaveRetentionPolicyInput = {
  code: '',
  name: '',
  retentionMonths: 36,
  trigger: RetentionTrigger.onArchive,
}

const TRIGGERS = [RetentionTrigger.onCreation, RetentionTrigger.onApproval, RetentionTrigger.onArchive]

/**
 * Políticas de retenção.
 *
 * Aqui a regra é o contrário da do catálogo, e de propósito: **mudar a duração é permitido**. As
 * obrigações de retenção mudam de facto — muda a lei, muda o contrato — e um catálogo incapaz de as
 * acompanhar é que seria a mentira. O que a mudança faz é mexer na data de eliminação de todos os
 * documentos que a política governa, e por isso fica na auditoria com o valor antigo e o novo.
 *
 * O que é recusado é retirar de circulação uma política que tipos de documento ainda adotam: esses
 * tipos continuariam a aplicar uma regra que a organização decidiu deixar de aplicar.
 */
export function RetentionPanel() {
  const { t } = useTranslation()

  const [draft, setDraft] = useState<SaveRetentionPolicyInput & { id?: string }>({ ...EMPTY })

  const policies = useRetentionPolicies()
  const savePolicy = useSaveRetentionPolicy()
  const setActive = useSetPolicyActive()

  // Separados pela origem: retirar uma política é uma ação de linha, guardar é do formulário.
  const rowError = setActive.error
  const formError = savePolicy.error
  const startNew = () => setDraft({ ...EMPTY })

  const edit = (policy: AdminRetentionPolicy) =>
    setDraft({
      id: policy.id,
      code: policy.code,
      name: policy.name,
      description: policy.description ?? '',
      legalBasis: policy.legalBasis ?? '',
      retentionMonths: policy.retentionMonths,
      trigger: policy.trigger,
    })

  const submit = (event: React.FormEvent) => {
    event.preventDefault()

    const { id, ...input } = draft

    savePolicy.mutate(
      {
        id,
        input: {
          ...input,
          code: input.code.trim().toUpperCase(),
          name: input.name.trim(),
          description: input.description?.trim() || undefined,
          legalBasis: input.legalBasis?.trim() || undefined,
        },
      },
      { onSuccess: startNew },
    )
  }

  return (
    <div className="grid gap-4 lg:grid-cols-[1fr_22rem]">
      <Card>
        <CardHeader title={t('admin.retention.title')} description={t('admin.retention.subtitle')} />
        <CardBody className="p-0">
          {rowError && <Refusal message={rowError.message} className="mx-4 mt-3" />}

          {policies.isLoading && <LoadingState label={t('common.loading')} />}
          {policies.isError && <ErrorState title={t('errors.genericTitle')} body={policies.error.message} />}

          {policies.data && (
            <table className="w-full text-sm">
              <thead className="border-b border-subtle text-left text-xs text-ink-muted">
                <tr>
                  <th scope="col" className="px-4 py-2 font-medium">
                    {t('admin.retention.policy')}
                  </th>
                  <th scope="col" className="px-4 py-2 font-medium">
                    {t('admin.retention.duration')}
                  </th>
                  <th scope="col" className="px-4 py-2 font-medium">
                    {t('admin.retention.trigger')}
                  </th>
                  <th scope="col" className="px-4 py-2 font-medium">
                    {t('admin.retention.types')}
                  </th>
                  <th scope="col" className="px-4 py-2" />
                </tr>
              </thead>
              <tbody className="divide-y divide-subtle">
                {policies.data.map((policy) => (
                  <tr key={policy.id} className={policy.isActive ? undefined : 'opacity-60'}>
                    <td className="px-4 py-2">
                      <span className="block text-ink">{policy.name}</span>
                      <span className="block font-mono text-xs text-ink-muted">{policy.code}</span>
                      {policy.legalBasis && (
                        <span className="block text-xs text-ink-muted">{policy.legalBasis}</span>
                      )}
                      {!policy.isActive && <Badge tone="neutral">{t('admin.retention.withdrawn')}</Badge>}
                    </td>
                    <td className="px-4 py-2 tabular-nums text-ink">
                      {t('admin.retention.months', { count: policy.retentionMonths })}
                    </td>
                    <td className="px-4 py-2 text-xs text-ink-secondary">
                      {t(`admin.retention.triggers.${policy.trigger}`)}
                    </td>
                    <td className="px-4 py-2 tabular-nums text-ink">{policy.typeCount}</td>
                    <td className="px-4 py-2">
                      <div className="flex justify-end gap-1">
                        <Button variant="ghost" size="sm" onClick={() => edit(policy)}>
                          <Pencil aria-hidden className="size-4" />
                          {t('common.edit')}
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          loading={setActive.isPending}
                          onClick={() => setActive.mutate({ id: policy.id, isActive: !policy.isActive })}
                        >
                          {policy.isActive ? t('admin.retention.withdraw') : t('admin.retention.reinstate')}
                        </Button>
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
          title={draft.id ? t('admin.retention.editTitle') : t('admin.retention.createTitle')}
          actions={
            draft.id && (
              <Button variant="ghost" size="sm" onClick={startNew}>
                {t('admin.retention.createTitle')}
              </Button>
            )
          }
        />
        <CardBody>
          <form onSubmit={submit} aria-label={t('admin.retention.formLabel')} className="flex flex-col gap-3">
            {formError && <Refusal message={formError.message} />}

            <TextField
              label={t('admin.retention.name')}
              value={draft.name}
              onChange={(event) => setDraft({ ...draft, name: event.target.value })}
              required
            />
            <TextField
              label={t('admin.retention.code')}
              value={draft.code}
              onChange={(event) => setDraft({ ...draft, code: event.target.value })}
              required
            />
            <TextField
              label={t('admin.retention.durationMonths')}
              type="number"
              value={String(draft.retentionMonths)}
              onChange={(event) => setDraft({ ...draft, retentionMonths: Number(event.target.value) })}
              hint={t('admin.retention.durationHint')}
              required
            />

            <div className="flex flex-col gap-1.5">
              <label htmlFor="policy-trigger" className="text-xs font-medium text-ink-secondary">
                {t('admin.retention.trigger')}
              </label>
              <select
                id="policy-trigger"
                value={draft.trigger}
                onChange={(event) => setDraft({ ...draft, trigger: Number(event.target.value) })}
                className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink"
              >
                {TRIGGERS.map((value) => (
                  <option key={value} value={value}>
                    {t(`admin.retention.triggers.${value}`)}
                  </option>
                ))}
              </select>
            </div>

            <TextField
              label={t('admin.retention.legalBasis')}
              value={draft.legalBasis ?? ''}
              onChange={(event) => setDraft({ ...draft, legalBasis: event.target.value })}
              hint={t('admin.retention.legalBasisHint')}
            />

            <div className="flex justify-end gap-2">
              {draft.id && (
                <Button type="button" variant="secondary" size="sm" onClick={startNew}>
                  {t('common.cancel')}
                </Button>
              )}
              <Button type="submit" size="sm" loading={savePolicy.isPending}>
                {draft.id ? t('common.save') : t('admin.retention.create')}
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
