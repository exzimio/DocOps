import { useTranslation } from 'react-i18next'
import { FlaskConical, Plug } from 'lucide-react'
import clsx from 'clsx'
import { Badge } from '@/components/ui/Surface'
import {
  EmailImportance,
  MailSource,
  type EmailImportanceValue,
  type EmailStatusValue,
  type MailSourceValue,
} from './types'
import { statusTone } from './labels'

export function StatusBadge({ status }: { status: EmailStatusValue }) {
  const { t } = useTranslation()

  return <Badge tone={statusTone(status)}>{t(`inbox.statusValues.${status}`)}</Badge>
}

export function ImportanceBadge({ importance }: { importance: EmailImportanceValue }) {
  const { t } = useTranslation()

  if (importance === EmailImportance.Normal) {
    return null
  }

  return (
    <Badge tone={importance === EmailImportance.High ? 'red' : 'neutral'}>
      {t(`inbox.importanceValues.${importance}`)}
    </Badge>
  )
}

/**
 * Says which mail system answered, next to the mailboxes it answered for. Required wherever the
 * inbox is shown: a simulated mailbox must never be mistaken for a connected one (ADR-004's rule,
 * applied to mail the same way it applies to AI).
 */
export function ConnectorNotice({
  source,
  name,
  description,
  compact = false,
}: {
  /** Typed, not a bare number: this component is the guarantee that a simulated mailbox is never
      shown as a connected one, and a stray value would quietly make it claim the wrong thing. */
  source: MailSourceValue
  name: string
  description: string
  compact?: boolean
}) {
  const isDemo = source === MailSource.Demo
  const Icon = isDemo ? FlaskConical : Plug

  if (compact) {
    return (
      <span
        className={clsx(
          'inline-flex items-center gap-1 rounded-xs border px-1.5 py-0.5 text-[11px] font-medium',
          isDemo
            ? 'border-amber-300 bg-amber-50 text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-200'
            : 'border-teal-300 bg-teal-50 text-teal-900 dark:border-teal-800 dark:bg-teal-950 dark:text-teal-200',
        )}
        title={description}
      >
        <Icon aria-hidden className="size-3" />
        {name}
      </span>
    )
  }

  return (
    <div
      role="note"
      className={clsx(
        'flex items-start gap-2 rounded-md border px-3 py-2 text-xs',
        isDemo
          ? 'border-amber-300 bg-amber-50 text-amber-950 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100'
          : 'border-teal-300 bg-teal-50 text-teal-950 dark:border-teal-800 dark:bg-teal-950 dark:text-teal-100',
      )}
    >
      <Icon aria-hidden className="mt-0.5 size-4 shrink-0" />
      <p>
        <span className="font-semibold">{name}</span> {description}
      </p>
    </div>
  )
}
