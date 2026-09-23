import { Database, FileSignature, FolderInput, Mail, Users, Webhook, type LucideIcon } from 'lucide-react'
import {
  IntegrationKind,
  IntegrationLogLevel,
  IntegrationStatus,
  type IntegrationKindValue,
  type IntegrationLogLevelValue,
  type IntegrationStatusValue,
} from './types'

/** One glyph per system, so the row of cards reads at a glance before anyone reads a word. */
const icons: Record<IntegrationKindValue, LucideIcon> = {
  [IntegrationKind.MicrosoftOutlook]: Mail,
  [IntegrationKind.Erp]: Database,
  [IntegrationKind.Crm]: Users,
  [IntegrationKind.ElectronicSignature]: FileSignature,
  [IntegrationKind.Webhooks]: Webhook,
  [IntegrationKind.FileImport]: FolderInput,
}

export function integrationIcon(kind: IntegrationKindValue): LucideIcon {
  return icons[kind]
}

/** The colour a status is shown in, kept apart from the components so this file stays reusable. */
export function statusTone(status: IntegrationStatusValue): 'neutral' | 'teal' | 'amber' | 'red' | 'green' {
  switch (status) {
    case IntegrationStatus.Connected:
      return 'green'
    case IntegrationStatus.Degraded:
      return 'amber'
    case IntegrationStatus.Failing:
      return 'red'
    case IntegrationStatus.Disabled:
      return 'neutral'
    default:
      return 'teal'
  }
}

export function logLevelTone(level: IntegrationLogLevelValue): 'neutral' | 'amber' | 'red' {
  switch (level) {
    case IntegrationLogLevel.Warning:
      return 'amber'
    case IntegrationLogLevel.Error:
      return 'red'
    default:
      return 'neutral'
  }
}
