import { useTranslation } from 'react-i18next'
import { Badge } from '@/components/ui/Surface'
import {
  DocumentStatus,
  SecurityClassification,
  type DocumentStatusValue,
  type SecurityClassificationValue,
} from './types'

type Tone = 'neutral' | 'teal' | 'amber' | 'red' | 'green'

const statusTone: Record<DocumentStatusValue, Tone> = {
  [DocumentStatus.Draft]: 'neutral',
  [DocumentStatus.InReview]: 'teal',
  [DocumentStatus.PendingApproval]: 'amber',
  [DocumentStatus.Approved]: 'green',
  [DocumentStatus.Rejected]: 'red',
  [DocumentStatus.SignatureRequired]: 'amber',
  [DocumentStatus.Signed]: 'green',
  [DocumentStatus.Archived]: 'neutral',
}

const classificationTone: Record<SecurityClassificationValue, Tone> = {
  [SecurityClassification.Public]: 'green',
  [SecurityClassification.Internal]: 'neutral',
  [SecurityClassification.Confidential]: 'amber',
  [SecurityClassification.Restricted]: 'red',
}

export function StatusBadge({ status }: { status: DocumentStatusValue }) {
  const { t } = useTranslation()
  return <Badge tone={statusTone[status]}>{t(`documents.statusValues.${status}`)}</Badge>
}

export function ClassificationBadge({ value }: { value: SecurityClassificationValue }) {
  const { t } = useTranslation()
  return <Badge tone={classificationTone[value]}>{t(`documents.classificationValues.${value}`)}</Badge>
}
