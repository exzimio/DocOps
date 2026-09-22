import { useTranslation } from 'react-i18next'
import { FlaskConical, Sparkles } from 'lucide-react'
import clsx from 'clsx'
import { AiSource, type AiSourceValue } from './types'

interface AiSourceNoticeProps {
  source: AiSourceValue
  providerName?: string | null
  compact?: boolean
}

/**
 * Says where a result came from, next to the result. Required wherever AI output is shown: a
 * demonstration must never be mistaken for a model's work (ADR-004), and a model's work must never
 * be mistaken for a fact.
 */
export function AiSourceNotice({ source, providerName, compact = false }: AiSourceNoticeProps) {
  const { t } = useTranslation()
  const isDemo = source === AiSource.Demo
  const Icon = isDemo ? FlaskConical : Sparkles

  if (compact) {
    return (
      <span
        className={clsx(
          'inline-flex items-center gap-1 rounded-xs border px-1.5 py-0.5 text-[11px] font-medium',
          isDemo
            ? 'border-amber-300 bg-amber-50 text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-200'
            : 'border-teal-300 bg-teal-50 text-teal-900 dark:border-teal-800 dark:bg-teal-950 dark:text-teal-200',
        )}
        title={isDemo ? t('ai.source.demoDetail') : t('ai.source.modelDetail')}
      >
        <Icon aria-hidden className="size-3" />
        {isDemo ? t('ai.source.demoShort') : t('ai.source.modelShort')}
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
        <span className="font-semibold">
          {isDemo ? t('ai.source.demoTitle') : (providerName ?? t('ai.source.modelShort'))}
        </span>{' '}
        {isDemo ? t('ai.source.demoDetail') : t('ai.source.modelDetail')}
      </p>
    </div>
  )
}
