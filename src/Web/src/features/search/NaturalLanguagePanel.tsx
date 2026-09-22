import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { MessageCircleQuestion } from 'lucide-react'
import { AiSourceNotice } from '@/features/ai/AiSourceNotice'
import { AiSource } from '@/features/ai/types'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Surface'
import { useNaturalLanguageSearch } from './api'
import { describeFilter } from './labels'
import { AiOrigin, type SearchInterpretation, type SearchResult } from './types'

const examples = ['search.natural.example1', 'search.natural.example2', 'search.natural.example3']

interface NaturalLanguagePanelProps {
  pageSize: number
  onAnswer: (interpretation: SearchInterpretation, result: SearchResult) => void
  onClear: () => void
  interpretation: SearchInterpretation | null
}

/**
 * Asks a question in words and shows what it was understood to mean.
 *
 * The reading is never hidden: the filters the question became are listed before the results, so a
 * question read wrongly is visible as a wrong reading rather than as a strange answer. Which engine
 * read it is stated too — the demonstration reader is a table of rules, and saying otherwise would
 * be the one thing this project must not do.
 */
export function NaturalLanguagePanel({
  pageSize,
  onAnswer,
  onClear,
  interpretation,
}: NaturalLanguagePanelProps) {
  const { t } = useTranslation()
  const [question, setQuestion] = useState('')
  const ask = useNaturalLanguageSearch()

  const submit = (value: string) => {
    const trimmed = value.trim()

    if (trimmed.length === 0) {
      return
    }

    setQuestion(trimmed)
    ask.mutate(
      { question: trimmed, pageSize },
      { onSuccess: (data) => onAnswer(data.interpretation, data.result) },
    )
  }

  return (
    <Card className="p-3">
      <form
        className="flex flex-col gap-2"
        onSubmit={(event) => {
          event.preventDefault()
          submit(question)
        }}
      >
        <label htmlFor="search-question" className="text-xs font-medium text-ink-secondary">
          {t('search.natural.label')}
        </label>

        <div className="flex flex-wrap gap-2">
          <input
            id="search-question"
            type="text"
            value={question}
            maxLength={500}
            onChange={(event) => setQuestion(event.target.value)}
            placeholder={t('search.natural.placeholder')}
            className="h-9 min-w-[18rem] flex-1 rounded-md border border-strong bg-raised px-3 text-sm text-ink placeholder:text-ink-muted"
          />
          <Button type="submit" disabled={ask.isPending || question.trim().length === 0}>
            <MessageCircleQuestion aria-hidden className="size-4" />
            {ask.isPending ? t('search.natural.asking') : t('search.natural.ask')}
          </Button>
          {interpretation && (
            <Button
              type="button"
              variant="ghost"
              onClick={() => {
                setQuestion('')
                ask.reset()
                onClear()
              }}
            >
              {t('search.natural.clear')}
            </Button>
          )}
        </div>

        {!interpretation && !ask.isPending && (
          <p className="flex flex-wrap items-center gap-1 text-[11px] text-ink-muted">
            {t('search.natural.tryIt')}
            {examples.map((key) => (
              <button
                key={key}
                type="button"
                onClick={() => submit(t(key))}
                className="rounded-xs border border-subtle px-1.5 py-0.5 text-ink-secondary hover:bg-sunken"
              >
                {t(key)}
              </button>
            ))}
          </p>
        )}

        {ask.isError && (
          <p role="alert" className="text-xs text-red-700 dark:text-red-300">
            {ask.error.message}
          </p>
        )}
      </form>

      {interpretation && (
        <div className="mt-3 flex flex-col gap-2 border-t border-subtle pt-3">
          <div>
            <AiSourceNotice
              source={interpretation.source === AiOrigin.Demo ? AiSource.Demo : AiSource.Model}
              compact
            />
          </div>

          <p className="text-xs text-ink-secondary">
            {interpretation.understood ? t('search.natural.understood') : t('search.natural.notUnderstood')}
          </p>

          <ul aria-label={t('search.natural.readingLabel')} className="flex flex-wrap gap-1.5">
            {interpretation.filters.map((filter, index) => {
              const described = describeFilter(filter, t)

              return (
                <li
                  key={`${filter.field}-${filter.value}-${index}`}
                  className="rounded-xs border border-subtle bg-sunken px-2 py-0.5 text-[11px] text-ink-secondary"
                >
                  <span className="text-ink-muted">{described.label}:</span> {described.value}
                </li>
              )
            })}
          </ul>
        </div>
      )}
    </Card>
  )
}
