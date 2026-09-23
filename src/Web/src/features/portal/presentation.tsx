import { useTranslation } from 'react-i18next'
import { Badge } from '@/components/ui/Surface'
import { progressTone } from './labels'
import type { PortalProgressValue } from './types'

export function ProgressBadge({ progress }: { progress: PortalProgressValue }) {
  const { t } = useTranslation()

  return <Badge tone={progressTone(progress)}>{t(`portal.progress.${progress}`)}</Badge>
}
