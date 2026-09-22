import { useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import clsx from 'clsx'
import type { DocumentText } from './types'

interface DocumentTextViewProps {
  text: DocumentText
  /** Line to show and highlight, e.g. the one a suggestion was read from. */
  highlightedLine: number | null
}

/**
 * The document's text, numbered exactly as the server numbers it for citations. When a reviewer
 * clicks a piece of evidence, this is where they check it — the citation is a place they can look,
 * not a claim they have to take on trust.
 */
export function DocumentTextView({ text, highlightedLine }: DocumentTextViewProps) {
  const { t } = useTranslation()
  const containerRef = useRef<HTMLDivElement>(null)
  const lineRefs = useRef(new Map<number, HTMLLIElement>())

  useEffect(() => {
    if (highlightedLine === null) {
      return
    }

    const line = lineRefs.current.get(highlightedLine)
    const container = containerRef.current

    if (line && container) {
      container.scrollTo({ top: line.offsetTop - container.clientHeight / 3, behavior: 'smooth' })

      // In a single-column layout the text sits above the suggestions: bring the panel itself into
      // view, or the highlighted line would be off screen.
      const bounds = container.getBoundingClientRect()
      if (bounds.bottom < 0 || bounds.top > window.innerHeight) {
        container.scrollIntoView({ behavior: 'smooth', block: 'center' })
      }
    }
  }, [highlightedLine])

  if (!text.hasText) {
    return (
      <p className="rounded-md border border-dashed border-strong px-3 py-6 text-center text-xs text-ink-muted">
        {t('ai.text.noText', { file: text.fileName })}
      </p>
    )
  }

  return (
    <div
      ref={containerRef}
      className="relative max-h-[36rem] overflow-auto rounded-md border border-subtle bg-sunken"
      aria-label={t('ai.text.title')}
    >
      <ol className="py-2 font-mono text-[12px] leading-5">
        {text.lines.map((line, index) => {
          const previous = index > 0 ? text.lines[index - 1] : undefined
          const newPage = previous !== undefined && previous.page !== line.page

          return (
            <li
              key={line.number}
              ref={(element) => {
                if (element) lineRefs.current.set(line.number, element)
                else lineRefs.current.delete(line.number)
              }}
              className={clsx(
                'grid grid-cols-[3rem_minmax(0,1fr)] gap-2 pr-3',
                newPage && 'mt-2 border-t border-dashed border-strong pt-2',
                highlightedLine === line.number
                  ? 'bg-amber-100 text-ink dark:bg-amber-900/50'
                  : 'text-ink-secondary',
              )}
              aria-current={highlightedLine === line.number ? 'location' : undefined}
            >
              <span className="select-none pr-1 text-right text-ink-muted">{line.number}</span>
              <span className="whitespace-pre">{line.text || ' '}</span>
            </li>
          )
        })}
      </ol>

      {text.truncated && (
        <p className="border-t border-subtle px-3 py-2 text-[11px] text-ink-muted">
          {t('ai.text.truncated')}
        </p>
      )}
    </div>
  )
}
