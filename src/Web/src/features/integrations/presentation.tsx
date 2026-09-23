import { useTranslation } from 'react-i18next'
import { FlaskConical } from 'lucide-react'
import { Badge } from '@/components/ui/Surface'
import { logLevelTone, statusTone } from './labels'
import type { IntegrationLogLevelValue, IntegrationStatusValue } from './types'

export function StatusBadge({ status }: { status: IntegrationStatusValue }) {
  const { t } = useTranslation()

  return <Badge tone={statusTone(status)}>{t(`integrations.statusValues.${status}`)}</Badge>
}

export function LogLevelBadge({ level }: { level: IntegrationLogLevelValue }) {
  const { t } = useTranslation()

  return <Badge tone={logLevelTone(level)}>{t(`integrations.logLevels.${level}`)}</Badge>
}

/**
 * Says exactly what is simulated about an integration, next to the fact of it. Required wherever an
 * integration's status is shown: a simulated far end that looked connected would be a screen lying
 * about what it does (ADR-007's rule, the same one the mail connector and the AI provider follow).
 */
export function SimulationNotice({ notice }: { notice: string }) {
  const { t } = useTranslation()

  return (
    <div
      role="note"
      className="flex items-start gap-2 rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-950 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-100"
    >
      <FlaskConical aria-hidden className="mt-0.5 size-4 shrink-0" />
      <p>
        <span className="font-semibold">{t('integrations.simulated')}</span> {notice}
      </p>
    </div>
  )
}
