import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { CornerDownLeft, FileText, Search } from 'lucide-react'
import clsx from 'clsx'
import { api, type ApiError } from '@/lib/api'
import { hasPermission, useSession } from '@/features/auth/session'
import { navigationSections } from '@/app/navigation'

interface DocumentHit {
  id: string
  reference: string
  title: string
}

interface PagedDocuments {
  items: DocumentHit[]
}

interface Command {
  key: string
  label: string
  hint?: string
  to: string
  kind: 'screen' | 'document'
}

/**
 * A paleta de comandos: `Ctrl+K` em qualquer lado.
 *
 * Quem sabe o que quer não tem de o ir buscar pela barra lateral, ecrã a ecrã.
 *
 * Duas fontes, deliberadamente: os ecrãs a que esta pessoa tem acesso, e os documentos que o
 * servidor lhe deixa ver. A segunda passa pela API de sempre, com o predicado de visibilidade de
 * sempre — uma paleta que mostrasse títulos de documentos inalcançáveis seria uma fuga por
 * conveniência.
 */
export function CommandPalette() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { data: me } = useSession()

  const [open, setOpen] = useState(false)
  const [term, setTerm] = useState('')
  const [highlighted, setHighlighted] = useState(0)

  // Escrever «contrato» são oito teclas, e sem isto seriam oito pedidos ao servidor — sete deles
  // já obsoletos quando chegam. A paleta é para escrever depressa; pedir a cada tecla é bater no
  // servidor para deitar fora quase tudo o que ele responde.
  const [settled, setSettled] = useState('')
  const settleTimer = useRef<ReturnType<typeof setTimeout>>(undefined)

  const inputRef = useRef<HTMLInputElement>(null)

  // Guardado no momento em que abre, para o foco voltar a quem o pediu ao fechar.
  const openerRef = useRef<HTMLElement | null>(null)

  const close = useCallback(() => {
    setOpen(false)

    // Fechar e deixar o foco no nada devolve quem usa teclado ao início da página.
    openerRef.current?.focus()
  }, [])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (!(event.ctrlKey || event.metaKey) || event.key.toLowerCase() !== 'k') {
        return
      }

      event.preventDefault()

      setOpen((current) => {
        if (current) {
          openerRef.current?.focus()
          return false
        }

        // Tudo o que a abertura muda acontece aqui, no evento que a causou: o campo limpo, a
        // escolha no princípio, e o sítio para onde o foco há de voltar. Fazê-lo num efeito
        // sobre `open` seria pedir ao React que voltasse a desenhar só para descobrir isso.
        openerRef.current = document.activeElement as HTMLElement | null
        setTerm('')
        setSettled('')
        setHighlighted(0)

        return true
      })
    }

    window.addEventListener('keydown', onKeyDown)

    return () => window.removeEventListener('keydown', onKeyDown)
  }, [])

  const screens = useMemo<Command[]>(
    () =>
      navigationSections
        .flatMap((section) => section.items)
        .filter((item) => item.permission === undefined || hasPermission(me, item.permission))
        .map((item) => ({
          key: item.to,
          label: t(item.labelKey),
          to: item.to,
          kind: 'screen' as const,
        })),
    [me, t],
  )

  const trimmed = term.trim()

  const documents = useQuery<PagedDocuments, ApiError>({
    queryKey: ['command-palette', settled],
    queryFn: () =>
      api.get<PagedDocuments>(
        // `query`, e não `search`: é assim que o parâmetro se chama no contrato. Com o nome errado
        // o servidor ignora-o em silêncio e devolve o catálogo inteiro — que foi o que aconteceu, e
        // só se viu ao ler a captura de ecrã e reparar que «contrato» trazia faturas.
        `/api/documents?page=1&pageSize=5&query=${encodeURIComponent(settled)}`,
      ),
    // Só depois de três letras: uma letra devolveria meio catálogo e não ajudaria ninguém.
    enabled: open && settled.length >= 3,
    staleTime: 30_000,
  })

  const commands = useMemo<Command[]>(() => {
    const lowered = trimmed.toLocaleLowerCase()

    const matchingScreens = lowered
      ? screens.filter((screen) => screen.label.toLocaleLowerCase().includes(lowered))
      : screens

    const hits: Command[] = (documents.data?.items ?? []).map((item) => ({
      key: item.id,
      label: item.title,
      hint: item.reference,
      to: `/app/documents/${item.id}`,
      kind: 'document' as const,
    }))

    return [...matchingScreens, ...hits]
  }, [screens, documents.data, trimmed])

  if (!open) {
    return null
  }

  const go = (command: Command) => {
    setOpen(false)
    navigate(command.to)
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={t('command.title')}
      className="fixed inset-0 z-50 flex items-start justify-center bg-graphite-950/50 p-4 pt-24"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) close()
      }}
    >
      <div className="w-full max-w-xl overflow-hidden rounded-lg border border-subtle bg-raised shadow-raised">
        <div className="flex items-center gap-2 border-b border-subtle px-3">
          <Search aria-hidden className="size-4 shrink-0 text-ink-muted" />
          <input
            ref={inputRef}
            autoFocus
            type="text"
            value={term}
            onChange={(event) => {
              const value = event.target.value
              setTerm(value)
              setHighlighted(0)

              clearTimeout(settleTimer.current)
              settleTimer.current = setTimeout(() => setSettled(value.trim()), 200)
            }}
            placeholder={t('command.placeholder')}
            aria-label={t('command.placeholder')}
            className="h-11 w-full bg-transparent text-sm text-ink outline-none"
            onKeyDown={(event) => {
              if (event.key === 'Escape') {
                close()
                return
              }

              if (event.key === 'ArrowDown') {
                event.preventDefault()
                setHighlighted((current) => Math.min(current + 1, commands.length - 1))
                return
              }

              if (event.key === 'ArrowUp') {
                event.preventDefault()
                setHighlighted((current) => Math.max(current - 1, 0))
                return
              }

              if (event.key === 'Enter' && commands[highlighted]) {
                event.preventDefault()
                go(commands[highlighted])
              }
            }}
          />
        </div>

        <ul className="max-h-80 overflow-y-auto py-1" aria-label={t('command.results')}>
          {commands.length === 0 && (
            <li className="px-3 py-6 text-center text-xs text-ink-muted">
              {documents.isLoading || settled !== trimmed ? t('common.loading') : t('command.nothing')}
            </li>
          )}

          {commands.map((command, index) => (
            <li key={`${command.kind}-${command.key}`}>
              <button
                type="button"
                onClick={() => go(command)}
                onMouseEnter={() => setHighlighted(index)}
                className={clsx(
                  'flex w-full items-center gap-2 px-3 py-2 text-left text-sm',
                  index === highlighted ? 'bg-sunken text-ink' : 'text-ink-secondary',
                )}
              >
                {command.kind === 'document' ? (
                  <FileText aria-hidden className="size-4 shrink-0 text-ink-muted" />
                ) : (
                  <CornerDownLeft aria-hidden className="size-4 shrink-0 text-ink-muted" />
                )}
                <span className="truncate">{command.label}</span>
                {command.hint && (
                  <span className="ml-auto shrink-0 font-mono text-[11px] text-ink-muted">
                    {command.hint}
                  </span>
                )}
              </button>
            </li>
          ))}
        </ul>

        <p className="border-t border-subtle px-3 py-2 text-[11px] text-ink-muted">{t('command.help')}</p>
      </div>
    </div>
  )
}
