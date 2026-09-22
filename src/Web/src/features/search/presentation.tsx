import type { SearchSnippet } from './types'

/**
 * Renders the excerpt with the matched words marked.
 *
 * The position comes from the server, which found the words in the document's own text; nothing is
 * searched for again here. That matters: the mark would otherwise be able to highlight something the
 * document does not say, and an excerpt that highlights the wrong thing is worse than no excerpt.
 */
export function SnippetText({ snippet }: { snippet: SearchSnippet }) {
  const { text, start, length } = snippet

  if (length <= 0 || start < 0 || start + length > text.length) {
    return <span>{text}</span>
  }

  return (
    <span>
      {text.slice(0, start)}
      <mark className="rounded-xs bg-amber-200 px-0.5 text-ink dark:bg-amber-700/60 dark:text-amber-50">
        {text.slice(start, start + length)}
      </mark>
      {text.slice(start + length)}
    </span>
  )
}
