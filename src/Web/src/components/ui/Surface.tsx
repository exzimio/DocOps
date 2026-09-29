import type { HTMLAttributes, ReactNode } from 'react'
import clsx from 'clsx'
import { AlertTriangle, Inbox, Loader2 } from 'lucide-react'

export function Card({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={clsx('rounded-lg border border-subtle bg-raised shadow-card', className)} {...props} />
  )
}

export interface CardHeaderProps {
  title: string
  description?: string
  icon?: ReactNode
  actions?: ReactNode
}

export function CardHeader({ title, description, icon, actions }: CardHeaderProps) {
  return (
    <div className="flex items-start justify-between gap-4 border-b border-subtle px-4 py-3">
      <div className="flex items-start gap-2.5">
        {icon && <span className="mt-0.5 text-teal-600 dark:text-teal-400">{icon}</span>}
        <div>
          <h2 className="text-sm font-semibold text-ink">{title}</h2>
          {description && <p className="mt-0.5 text-xs text-ink-muted">{description}</p>}
        </div>
      </div>
      {actions}
    </div>
  )
}

export function CardBody({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={clsx('p-4', className)} {...props} />
}

type BadgeTone = 'neutral' | 'teal' | 'amber' | 'red' | 'green'

const toneClasses: Record<BadgeTone, string> = {
  neutral: 'bg-sunken text-ink-secondary border-subtle',
  teal: 'bg-teal-50 text-teal-800 border-teal-200 dark:bg-teal-950 dark:text-teal-200 dark:border-teal-800',
  amber:
    'bg-amber-50 text-amber-800 border-amber-200 dark:bg-amber-900/30 dark:text-amber-200 dark:border-amber-800',
  red: 'bg-red-50 text-red-800 border-red-200 dark:bg-red-950 dark:text-red-200 dark:border-red-900',
  green:
    'bg-green-50 text-green-800 border-green-200 dark:bg-green-950 dark:text-green-200 dark:border-green-900',
}

export function Badge({
  tone = 'neutral',
  children,
  className,
}: {
  tone?: BadgeTone
  children: ReactNode
  className?: string
}) {
  return (
    <span
      className={clsx(
        'inline-flex items-center rounded-xs border px-1.5 py-0.5 text-[11px] font-medium',
        toneClasses[tone],
        className,
      )}
    >
      {children}
    </span>
  )
}

/** Definition-list row used throughout the detail panels. */
export function DataRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-4 py-1.5">
      <dt className="shrink-0 text-xs text-ink-muted">{label}</dt>
      <dd className="text-right text-sm font-medium text-ink">{children}</dd>
    </div>
  )
}

export function LoadingState({ label }: { label: string }) {
  return (
    <div role="status" className="flex items-center justify-center gap-2 p-8 text-sm text-ink-muted">
      <Loader2 aria-hidden className="size-4 animate-spin" />
      {label}
    </div>
  )
}

export function EmptyState({ title, body, action }: { title: string; body?: string; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-2 p-8 text-center">
      <Inbox aria-hidden className="size-6 text-ink-muted" />
      <p className="text-sm font-medium text-ink">{title}</p>
      {body && <p className="max-w-sm text-xs text-ink-muted">{body}</p>}
      {action}
    </div>
  )
}

export function ErrorState({
  title,
  body,
  correlationId,
  action,
}: {
  title: string
  body: string
  correlationId?: string
  action?: ReactNode
}) {
  return (
    <div role="alert" className="flex flex-col items-center gap-2 p-8 text-center">
      <AlertTriangle aria-hidden className="size-6 text-amber-600" />
      <p className="text-sm font-medium text-ink">{title}</p>
      <p className="max-w-sm text-xs text-ink-muted">{body}</p>
      {correlationId && <code className="font-mono text-[11px] text-ink-muted">{correlationId}</code>}
      {action}
    </div>
  )
}

/**
 * Uma tabela que desliza na horizontal quando não cabe — e que se consegue deslizar sem rato.
 *
 * Um `div` com `overflow-x-auto` a envolver uma tabela é invisível para quem navega por teclado: o
 * conteúdo não é focável, portanto não há como chegar lá para o deslizar, e metade das colunas
 * deixa de existir. Basta `tabIndex={0}` para o contentor entrar na ordem de tabulação e responder
 * às setas.
 *
 * Existe como componente e não como classe repetida porque foram cinco sítios a cometer o mesmo
 * erro, e o sexto iria cometê-lo também.
 */
export function TableScroller({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div tabIndex={0} role="region" aria-label={label} className="overflow-x-auto">
      {children}
    </div>
  )
}
