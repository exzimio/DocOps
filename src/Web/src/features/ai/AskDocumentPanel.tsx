import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MessageSquareText, ShieldCheck } from 'lucide-react'
import { formatDateTime } from '@/features/documents/format'
import { Button } from '@/components/ui/Button'
import { useAskDocument } from './api'
import { AiSourceNotice } from './AiSourceNotice'

interface AskDocumentPanelProps {
  documentId: string
  onShowLine: (line: number) => void
}

/**
 * "Ask this document". An answer is shown with the lines it came from, and only with lines the
 * server confirmed are in the document; an answer the document does not support is not shown at all.
 */
export function AskDocumentPanel({ documentId, onShowLine }: AskDocumentPanelProps) {
  const { t, i18n } = useTranslation()
  const ask = useAskDocument(documentId)
  const [question, setQuestion] = useState('')

  const examples = [t('ai.ask.example1'), t('ai.ask.example2'), t('ai.ask.example3')]

  const submit = (event: React.FormEvent) => {
    event.preventDefault()
    if (question.trim().length >= 3) {
      ask.mutate(question.trim())
    }
  }

  const answer = ask.data

  return (
    <div className="flex flex-col gap-3">
      <form onSubmit={submit} className="flex gap-2">
        <input
          type="text"
          value={question}
          onChange={(event) => setQuestion(event.target.value)}
          placeholder={t('ai.ask.placeholder')}
          aria-label={t('ai.ask.label')}
          maxLength={500}
          className="h-9 min-w-0 flex-1 rounded-md border border-strong bg-raised px-3 text-sm text-ink placeholder:text-ink-muted"
        />
        <Button type="submit" size="sm" loading={ask.isPending} disabled={question.trim().length < 3}>
          <MessageSquareText aria-hidden className="size-4" />
          {t('ai.ask.submit')}
        </Button>
      </form>

      <div className="flex flex-wrap gap-1.5">
        {examples.map((example) => (
          <button
            key={example}
            type="button"
            onClick={() => {
              setQuestion(example)
              ask.mutate(example)
            }}
            className="rounded-full border border-subtle bg-sunken px-2.5 py-1 text-[11px] text-ink-secondary hover:border-teal-500 hover:text-ink"
          >
            {example}
          </button>
        ))}
      </div>

      {ask.error && (
        <p role="alert" className="text-xs font-medium text-red-700 dark:text-red-300">
          {t(`ai.errors.${ask.error.code}`, { defaultValue: ask.error.message })}
        </p>
      )}

      {answer && (
        <div className="flex flex-col gap-2 rounded-md border border-subtle p-3" aria-live="polite">
          <div className="flex items-center justify-between gap-2">
            <p className={answer.answered ? 'text-sm text-ink' : 'text-sm text-ink-muted'}>{answer.text}</p>
            <AiSourceNotice source={answer.source} providerName={answer.providerName} compact />
          </div>

          {answer.citations.length > 0 && (
            <ul className="flex flex-col gap-1">
              {answer.citations.map((citation, index) =>
                citation.kind === 'document' && citation.line !== null ? (
                  <li key={index}>
                    <button
                      type="button"
                      onClick={() => onShowLine(citation.line!)}
                      className="flex w-full items-start gap-1.5 text-left text-[11px] text-ink-muted hover:text-ink"
                    >
                      <span className="shrink-0 rounded-xs bg-sunken px-1 font-mono">
                        {citation.fileName} · {t('ai.review.line', { line: citation.line })}
                        {citation.page && citation.page > 1
                          ? ` · ${t('ai.ask.page', { page: citation.page })}`
                          : ''}
                      </span>
                      <span className="truncate">
                        {citation.section ? `${citation.section} — ` : ''}«{citation.excerpt}»
                      </span>
                    </button>
                  </li>
                ) : (
                  <li key={index} className="flex items-start gap-1.5 text-[11px] text-ink-muted">
                    <ShieldCheck aria-hidden className="mt-0.5 size-3.5 shrink-0" />
                    <span>
                      {t('ai.ask.auditRecord')} · {formatDateTime(citation.occurredAtUtc, i18n.language)} —{' '}
                      {citation.excerpt}
                    </span>
                  </li>
                ),
              )}
            </ul>
          )}

          {answer.discardedCitations > 0 && (
            <p className="text-[11px] text-ink-muted">
              {t('ai.ask.discarded', { count: answer.discardedCitations })}
            </p>
          )}
        </div>
      )}
    </div>
  )
}
