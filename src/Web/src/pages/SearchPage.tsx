import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Database, Search, SlidersHorizontal } from 'lucide-react'
import { useSearch } from '@/features/search/api'
import { SearchCriteria } from '@/features/search/SearchCriteria'
import { NaturalLanguagePanel } from '@/features/search/NaturalLanguagePanel'
import { SnippetText } from '@/features/search/presentation'
import { matchFieldKey } from '@/features/search/labels'
import {
  defaultSearchFilters,
  hasCriteria,
  type SearchFilters,
  type SearchInterpretation,
  type SearchResult,
} from '@/features/search/types'
import { ClassificationBadge, StatusBadge } from '@/features/documents/presentation'
import { formatDate } from '@/features/documents/format'
import { Button } from '@/components/ui/Button'
import { Badge, Card, EmptyState, ErrorState, LoadingState } from '@/components/ui/Surface'

export function SearchPage() {
  const { t, i18n } = useTranslation()

  const [filters, setFilters] = useState<SearchFilters>(defaultSearchFilters)
  const [text, setText] = useState('')
  const [showFilters, setShowFilters] = useState(false)

  /**
   * A question's answer is held here rather than refetched through the filter query. The reading is
   * part of the answer: rerunning the filters alone would show the same rows without the sentence
   * that produced them, and the person would lose sight of what was actually asked.
   */
  const [asked, setAsked] = useState<{
    interpretation: SearchInterpretation
    result: SearchResult
  } | null>(null)

  // Debounced so typing costs one request, not one per keystroke.
  useEffect(() => {
    const timer = setTimeout(() => {
      setFilters((current) => (current.query === text ? current : { ...current, query: text, page: 1 }))
    }, 300)

    return () => clearTimeout(timer)
  }, [text])

  const searchQuery = useSearch(filters)
  const result = asked?.result ?? searchQuery.data
  const criteria = hasCriteria(filters)

  const resetAll = () => {
    setText('')
    setFilters(defaultSearchFilters)
    setAsked(null)
  }

  return (
    <div className="mx-auto flex max-w-[95rem] flex-col gap-4">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-lg font-semibold text-ink">{t('search.title')}</h1>
          <p className="text-sm text-ink-muted">{t('search.subtitle')}</p>
        </div>

        <Button variant="secondary" size="sm" onClick={() => setShowFilters((open) => !open)}>
          <SlidersHorizontal aria-hidden className="size-4" />
          {showFilters ? t('search.hideFilters') : t('search.showFilters')}
        </Button>
      </header>

      <Card className="p-3">
        <div className="relative">
          <Search
            aria-hidden
            className="pointer-events-none absolute left-2.5 top-2.5 size-4 text-ink-muted"
          />
          <input
            type="search"
            value={text}
            onChange={(event) => {
              setText(event.target.value)
              setAsked(null)
            }}
            placeholder={t('search.placeholder')}
            aria-label={t('search.placeholder')}
            className="h-9 w-full rounded-md border border-strong bg-raised pl-8 pr-3 text-sm text-ink placeholder:text-ink-muted"
          />
        </div>
      </Card>

      <NaturalLanguagePanel
        pageSize={filters.pageSize}
        interpretation={asked?.interpretation ?? null}
        onAnswer={(interpretation, answer) => setAsked({ interpretation, result: answer })}
        onClear={() => setAsked(null)}
      />

      {showFilters && (
        <SearchCriteria
          filters={filters}
          onChange={(next) => {
            setFilters(next)
            setAsked(null)
          }}
          onReset={resetAll}
          showReset={criteria}
        />
      )}

      <Card>
        {!asked && searchQuery.isLoading && <LoadingState label={t('common.loading')} />}

        {!asked && searchQuery.isError && (
          <ErrorState
            title={t('errors.genericTitle')}
            body={searchQuery.error.message}
            correlationId={searchQuery.error.problem.correlationId}
            action={
              <Button variant="secondary" size="sm" onClick={() => void searchQuery.refetch()}>
                {t('common.retry')}
              </Button>
            }
          />
        )}

        {!asked && !criteria && <EmptyState title={t('search.startTitle')} body={t('search.startBody')} />}

        {result && result.page.items.length === 0 && (
          <EmptyState title={t('search.noResults')} body={t('search.noResultsBody')} />
        )}

        {result && result.page.items.length > 0 && (
          <>
            <ul aria-label={t('search.resultsLabel')} className="divide-y divide-subtle">
              {result.page.items.map((hit) => (
                <li key={hit.document.id} className="flex flex-col gap-1 px-3 py-2.5 hover:bg-sunken">
                  <div className="flex flex-wrap items-center gap-2">
                    <Link
                      to={`/app/documents/${hit.document.id}`}
                      className="font-medium text-ink hover:text-teal-700 hover:underline dark:hover:text-teal-300"
                    >
                      {hit.document.title}
                    </Link>
                    <span className="font-mono text-[11px] text-ink-muted">{hit.document.reference}</span>
                    <StatusBadge status={hit.document.status} />
                    <ClassificationBadge value={hit.document.classification} />
                  </div>

                  {hit.snippet && (
                    <p className="text-xs leading-relaxed text-ink-secondary">
                      <SnippetText snippet={hit.snippet} />
                    </p>
                  )}

                  <div className="flex flex-wrap items-center gap-2 text-[11px] text-ink-muted">
                    <span>{hit.document.typeName}</span>
                    <span>·</span>
                    <span>{hit.document.ownerName}</span>
                    {hit.document.departmentCode && (
                      <>
                        <span>·</span>
                        <span>{hit.document.departmentCode}</span>
                      </>
                    )}
                    <span>·</span>
                    <span>{formatDate(hit.document.createdAtUtc, i18n.language)}</span>

                    {hit.matchedIn.length > 0 && (
                      <>
                        <span>·</span>
                        <span>{t('search.matchedIn')}</span>
                        {hit.matchedIn.map((field) => (
                          <Badge key={field}>{t(matchFieldKey(field))}</Badge>
                        ))}
                      </>
                    )}

                    {hit.score > 0 && (
                      <>
                        <span>·</span>
                        <span title={t('search.scoreHint')}>
                          {t('search.score', { value: Math.round(hit.score * 100) })}
                        </span>
                      </>
                    )}

                    {hit.document.tags.slice(0, 3).map((tag) => (
                      <Badge key={tag.id}>{tag.name}</Badge>
                    ))}
                  </div>
                </li>
              ))}
            </ul>

            <div className="flex flex-wrap items-center justify-between gap-2 border-t border-subtle px-3 py-2">
              <p className="text-xs text-ink-muted">
                {t('documents.resultsCount', { count: result.page.totalCount })} ·{' '}
                {t('documents.page', { page: result.page.page, total: result.page.totalPages })}
              </p>

              {!asked && (
                <div className="flex gap-2">
                  <Button
                    variant="secondary"
                    size="sm"
                    disabled={!result.page.hasPrevious}
                    onClick={() => setFilters((current) => ({ ...current, page: current.page - 1 }))}
                  >
                    {t('documents.previous')}
                  </Button>
                  <Button
                    variant="secondary"
                    size="sm"
                    disabled={!result.page.hasNext}
                    onClick={() => setFilters((current) => ({ ...current, page: current.page + 1 }))}
                  >
                    {t('documents.next')}
                  </Button>
                </div>
              )}
            </div>
          </>
        )}
      </Card>

      {result && (
        // Which engine answered, in the open. A ranked full-text search and a substring scan return
        // different things, and a search that quietly degraded would look like one that found less.
        <p className="flex flex-wrap items-center gap-1.5 text-[11px] text-ink-muted">
          <Database aria-hidden className="size-3" />
          {result.diagnostics.fullTextIndexUsed
            ? t('search.engine.index')
            : result.diagnostics.fullTextAvailable
              ? t('search.engine.noTerms')
              : t('search.engine.fallback')}
          {result.diagnostics.terms.length > 0 && (
            <span>· {t('search.engine.terms', { terms: result.diagnostics.terms.join(', ') })}</span>
          )}
          <span>· {t('search.engine.elapsed', { ms: result.diagnostics.elapsedMilliseconds })}</span>
        </p>
      )}
    </div>
  )
}
