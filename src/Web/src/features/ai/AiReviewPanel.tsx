import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Check, FileSearch, Pencil, RefreshCw, TriangleAlert, X } from 'lucide-react'
import clsx from 'clsx'
import type { ApiError } from '@/lib/api'
import { MetadataFieldType, type DocumentDetail } from '@/features/documents/types'
import { formatDateTime } from '@/features/documents/format'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Surface'
import {
  useAcceptAll,
  useApplyRun,
  useDecideClassification,
  useDecideField,
  useDiscardRun,
  useLatestAiRun,
  useProcessDocument,
} from './api'
import { AiSourceNotice } from './AiSourceNotice'
import { bandTone, differs, formatSuggestion, percent, runStatusTone, toInputValue } from './presentation'
import {
  AiRunOrigin,
  AiRunStatus,
  AiSuggestionDecision,
  type AiFieldSuggestion,
  type AiRun,
  type TextReference,
} from './types'

interface AiReviewPanelProps {
  document: DocumentDetail
  canProcess: boolean
  onShowLine: (line: number) => void
}

/**
 * Suggestions and the decisions about them. Every button here is a request the server authorises,
 * validates and audits; the panel keeps no decision state of its own, so what it shows is always
 * what was recorded.
 */
export function AiReviewPanel({ document, canProcess, onShowLine }: AiReviewPanelProps) {
  const { t } = useTranslation()
  const documentId = document.summary.id
  const runQuery = useLatestAiRun(documentId)
  const process = useProcessDocument(documentId)

  const run = runQuery.data ?? null
  const canStartAnalysis = canProcess && document.canChangeMetadata

  const analyse = (
    <Button
      size="sm"
      variant={run ? 'secondary' : 'primary'}
      loading={process.isPending}
      disabled={!canStartAnalysis}
      onClick={() => process.mutate()}
    >
      {run ? <RefreshCw aria-hidden className="size-4" /> : <FileSearch aria-hidden className="size-4" />}
      {run ? t('ai.review.analyseAgain') : t('ai.review.analyse')}
    </Button>
  )

  if (runQuery.isLoading) {
    return <p className="text-xs text-ink-muted">{t('common.loading')}</p>
  }

  return (
    <div className="flex flex-col gap-3">
      {!run && (
        <div className="flex flex-col items-start gap-3 rounded-md border border-dashed border-strong p-4">
          <p className="text-sm text-ink-secondary">{t('ai.review.neverAnalysed')}</p>
          {canStartAnalysis ? (
            analyse
          ) : (
            <p className="text-xs text-ink-muted">{t('ai.review.cannotProcess')}</p>
          )}
        </div>
      )}

      {process.error && <ErrorLine error={process.error} />}

      {run && (
        <RunReview
          key={run.id}
          run={run}
          document={document}
          analyse={canStartAnalysis ? analyse : null}
          onShowLine={onShowLine}
        />
      )}
    </div>
  )
}

function RunReview({
  run,
  document,
  analyse,
  onShowLine,
}: {
  run: AiRun
  document: DocumentDetail
  analyse: React.ReactNode
  onShowLine: (line: number) => void
}) {
  const { t, i18n } = useTranslation()
  const documentId = document.summary.id

  const decideClassification = useDecideClassification(documentId, run.id)
  const acceptAll = useAcceptAll(documentId, run.id)
  const apply = useApplyRun(documentId, run.id)
  const discard = useDiscardRun(documentId, run.id)

  const reviewing = run.status === AiRunStatus.AwaitingReview
  const currency =
    run.fields.find((field) => field.fieldCode === 'currency')?.finalValue ??
    run.fields.find((field) => field.fieldCode === 'currency')?.suggestedValue ??
    null

  const error = decideClassification.error ?? acceptAll.error ?? apply.error ?? discard.error

  return (
    <div className="flex flex-col gap-3">
      <AiSourceNotice source={run.source} providerName={run.providerName} />

      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-2 text-xs text-ink-muted">
          <Badge tone={runStatusTone(run.status)}>{t(`ai.status.${run.status}`)}</Badge>
          <span>
            {t('ai.review.readVersion', { version: run.versionNumber })} ·{' '}
            {run.origin === AiRunOrigin.Workflow ? t('ai.review.byWorkflow') : run.requestedByName} ·{' '}
            {formatDateTime(run.createdAtUtc, i18n.language)}
          </span>
        </div>
        {analyse}
      </div>

      {run.isStale && reviewing && <Warning>{t('ai.review.stale')}</Warning>}

      {run.status === AiRunStatus.Failed && <Warning>{run.failureReason}</Warning>}

      {run.status === AiRunStatus.Applied && (
        <p className="rounded-md border border-green-300 bg-green-50 px-3 py-2 text-xs text-green-900 dark:border-green-800 dark:bg-green-950 dark:text-green-100">
          {t('ai.review.appliedBy', {
            name: run.appliedByName ?? '-',
            when: formatDateTime(run.appliedAtUtc, i18n.language),
          })}
        </p>
      )}

      {run.status === AiRunStatus.Discarded && (
        <p className="rounded-md border border-subtle bg-sunken px-3 py-2 text-xs text-ink-secondary">
          {run.failureReason ?? t('ai.review.discarded')}
        </p>
      )}

      {run.suggestedType && (
        <section className="rounded-md border border-subtle p-3">
          <h3 className="text-[11px] font-semibold uppercase tracking-wide text-ink-muted">
            {t('ai.review.classification')}
          </h3>

          <div className="mt-2 flex flex-wrap items-center gap-2">
            <span className="text-sm text-ink-secondary">{run.originalType.name}</span>
            <span aria-hidden className="text-ink-muted">
              →
            </span>
            <span className="text-sm font-semibold text-ink">{run.suggestedType.name}</span>
            <Badge tone={bandTone(run.classificationBand)}>{percent(run.classificationConfidence)}</Badge>
            <DecisionBadge decision={run.classificationDecision} />
          </div>

          {run.classificationEvidence && (
            <EvidenceLink evidence={run.classificationEvidence} onShowLine={onShowLine} />
          )}

          {reviewing && run.canReview && run.classificationDecision === AiSuggestionDecision.Pending && (
            <div className="mt-2 flex gap-2">
              <Button
                size="sm"
                loading={decideClassification.isPending}
                onClick={() => decideClassification.mutate(true)}
              >
                <Check aria-hidden className="size-4" />
                {t('ai.review.acceptType', { type: run.suggestedType.name })}
              </Button>
              <Button
                size="sm"
                variant="secondary"
                disabled={decideClassification.isPending}
                onClick={() => decideClassification.mutate(false)}
              >
                <X aria-hidden className="size-4" />
                {t('ai.review.keepType', { type: run.originalType.name })}
              </Button>
            </div>
          )}
        </section>
      )}

      {run.fields.length > 0 ? (
        <section className="rounded-md border border-subtle">
          <h3 className="border-b border-subtle px-3 py-2 text-[11px] font-semibold uppercase tracking-wide text-ink-muted">
            {t('ai.review.fields', {
              type:
                run.suggestedType && run.classificationDecision !== AiSuggestionDecision.Rejected
                  ? run.suggestedType.name
                  : run.originalType.name,
            })}
          </h3>
          <ul className="divide-y divide-subtle">
            {run.fields.map((field) => (
              <SuggestionRow
                key={field.id}
                field={field}
                run={run}
                documentId={documentId}
                currency={currency}
                onShowLine={onShowLine}
              />
            ))}
          </ul>
        </section>
      ) : (
        run.status !== AiRunStatus.Failed && (
          <p className="text-xs text-ink-muted">{t('ai.review.nothingRecognised')}</p>
        )
      )}

      {reviewing && run.missingRequiredFields.length > 0 && (
        <Warning>{t('ai.review.missing', { fields: run.missingRequiredFields.join(', ') })}</Warning>
      )}

      {error && <ErrorLine error={error} />}

      {reviewing && run.canReview && (
        <div className="flex flex-wrap gap-2">
          <Button
            size="sm"
            variant="secondary"
            loading={acceptAll.isPending}
            onClick={() => acceptAll.mutate()}
          >
            <Check aria-hidden className="size-4" />
            {t('ai.review.acceptAll')}
          </Button>
          <Button
            size="sm"
            loading={apply.isPending}
            disabled={!run.canApply}
            title={run.canApply ? undefined : t('ai.review.applyBlocked')}
            onClick={() => apply.mutate()}
          >
            {t('ai.review.apply')}
          </Button>
          <Button size="sm" variant="ghost" loading={discard.isPending} onClick={() => discard.mutate()}>
            {t('ai.review.discard')}
          </Button>
        </div>
      )}

      {reviewing && !run.canReview && <p className="text-xs text-ink-muted">{t('ai.review.readOnly')}</p>}
    </div>
  )
}

function SuggestionRow({
  field,
  run,
  documentId,
  currency,
  onShowLine,
}: {
  field: AiFieldSuggestion
  run: AiRun
  documentId: string
  currency: string | null
  onShowLine: (line: number) => void
}) {
  const { t, i18n } = useTranslation()
  const decide = useDecideField(documentId, run.id)
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState(toInputValue(field.finalValue ?? field.suggestedValue, field.fieldType))

  const reviewing = run.status === AiRunStatus.AwaitingReview && run.canReview
  const found = field.suggestedValue !== null
  const shownValue = field.decision === AiSuggestionDecision.Edited ? field.finalValue : field.suggestedValue

  const save = async (event: React.FormEvent) => {
    event.preventDefault()

    try {
      await decide.mutateAsync({
        suggestionId: field.id,
        decision: AiSuggestionDecision.Edited,
        value: draft,
      })
      setEditing(false)
    } catch {
      // Shown below from decide.error.
    }
  }

  return (
    <li className="flex flex-col gap-1.5 px-3 py-2.5">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="text-xs font-medium text-ink-secondary">
            {field.label}
            {field.isRequired && (
              <span className="ml-0.5 text-red-600" aria-label={t('ai.review.required')}>
                *
              </span>
            )}
          </p>
          <p
            className={clsx(
              'text-sm',
              found || field.decision === AiSuggestionDecision.Edited ? 'text-ink' : 'text-ink-muted',
            )}
          >
            {found || field.decision === AiSuggestionDecision.Edited
              ? formatSuggestion(shownValue, field.fieldType, i18n.language, currency)
              : t('ai.review.notFound')}
          </p>
          {run.status === AiRunStatus.AwaitingReview &&
            field.currentValue !== null &&
            differs(field.currentValue, field.suggestedValue, field.fieldType) && (
              <p className="text-[11px] text-ink-muted">
                {t('ai.review.current', {
                  value: formatSuggestion(field.currentValue, field.fieldType, i18n.language, currency),
                })}
              </p>
            )}
        </div>

        <div className="flex items-center gap-1.5">
          {found && <Badge tone={bandTone(field.band)}>{percent(field.confidence)}</Badge>}
          <DecisionBadge decision={field.decision} />
        </div>
      </div>

      {field.evidence && <EvidenceLink evidence={field.evidence} onShowLine={onShowLine} />}

      {editing ? (
        <form onSubmit={save} className="flex flex-wrap items-center gap-2">
          <EditInput field={field} value={draft} onChange={setDraft} />
          <Button type="submit" size="sm" loading={decide.isPending} disabled={!draft.trim()}>
            {t('ai.review.saveEdit')}
          </Button>
          <Button type="button" size="sm" variant="ghost" onClick={() => setEditing(false)}>
            {t('common.cancel')}
          </Button>
        </form>
      ) : (
        reviewing && (
          <div className="flex flex-wrap gap-1.5">
            {found && field.decision !== AiSuggestionDecision.Accepted && (
              <Button
                size="sm"
                variant="secondary"
                disabled={decide.isPending}
                onClick={() =>
                  decide.mutate({ suggestionId: field.id, decision: AiSuggestionDecision.Accepted })
                }
                aria-label={t('ai.review.acceptField', { field: field.label })}
              >
                <Check aria-hidden className="size-3.5" />
                {t('ai.review.accept')}
              </Button>
            )}
            <Button
              size="sm"
              variant="secondary"
              disabled={decide.isPending}
              onClick={() => setEditing(true)}
              aria-label={t('ai.review.editField', { field: field.label })}
            >
              <Pencil aria-hidden className="size-3.5" />
              {found ? t('ai.review.edit') : t('ai.review.fillIn')}
            </Button>
            {field.decision !== AiSuggestionDecision.Rejected && (
              <Button
                size="sm"
                variant="ghost"
                disabled={decide.isPending}
                onClick={() =>
                  decide.mutate({ suggestionId: field.id, decision: AiSuggestionDecision.Rejected })
                }
                aria-label={t('ai.review.rejectField', { field: field.label })}
              >
                <X aria-hidden className="size-3.5" />
                {t('ai.review.reject')}
              </Button>
            )}
          </div>
        )
      )}

      {decide.error && <ErrorLine error={decide.error} />}
    </li>
  )
}

function EditInput({
  field,
  value,
  onChange,
}: {
  field: AiFieldSuggestion
  value: string
  onChange: (value: string) => void
}) {
  const className = 'h-8 min-w-0 flex-1 rounded-md border border-strong bg-raised px-2 text-sm text-ink'

  if (field.fieldType === MetadataFieldType.Select && field.options.length > 0) {
    return (
      <select
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className={className}
        aria-label={field.label}
      >
        <option value="">-</option>
        {field.options.map((option) => (
          <option key={option} value={option}>
            {option}
          </option>
        ))}
      </select>
    )
  }

  const type =
    field.fieldType === MetadataFieldType.Date
      ? 'date'
      : field.fieldType === MetadataFieldType.Number || field.fieldType === MetadataFieldType.Currency
        ? 'number'
        : 'text'

  return (
    <input
      type={type}
      step={field.fieldType === MetadataFieldType.Currency ? '0.01' : undefined}
      value={value}
      onChange={(event) => onChange(event.target.value)}
      className={className}
      aria-label={field.label}
      autoFocus
    />
  )
}

function EvidenceLink({
  evidence,
  onShowLine,
}: {
  evidence: TextReference
  onShowLine: (line: number) => void
}) {
  const { t } = useTranslation()

  return (
    <button
      type="button"
      onClick={() => onShowLine(evidence.line)}
      className="group flex w-full items-start gap-1.5 rounded-xs text-left text-[11px] text-ink-muted hover:text-ink"
    >
      <span className="shrink-0 rounded-xs bg-sunken px-1 font-mono group-hover:bg-amber-100 dark:group-hover:bg-amber-900/50">
        {t('ai.review.line', { line: evidence.line })}
      </span>
      <span className="truncate">«{evidence.excerpt}»</span>
    </button>
  )
}

function DecisionBadge({ decision }: { decision: number }) {
  const { t } = useTranslation()

  if (decision === AiSuggestionDecision.Pending) {
    return null
  }

  const tone =
    decision === AiSuggestionDecision.Rejected
      ? 'red'
      : decision === AiSuggestionDecision.Edited
        ? 'teal'
        : 'green'

  return <Badge tone={tone}>{t(`ai.decision.${decision}`)}</Badge>
}

function Warning({ children }: { children: React.ReactNode }) {
  return (
    <p className="flex items-start gap-1.5 rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-950 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100">
      <TriangleAlert aria-hidden className="mt-0.5 size-3.5 shrink-0" />
      <span>{children}</span>
    </p>
  )
}

function ErrorLine({ error }: { error: ApiError }) {
  const { t } = useTranslation()

  return (
    <p role="alert" className="text-xs font-medium text-red-700 dark:text-red-300">
      {t(`ai.errors.${error.code}`, { defaultValue: error.message })}
    </p>
  )
}
