import type { PagedResult } from '@/features/documents/types'

/** Mirrors DocOps.Domain.Integrations.IntegrationKind. A closed set: one card per adapter that exists. */
export const IntegrationKind = {
  MicrosoftOutlook: 0,
  Erp: 1,
  Crm: 2,
  ElectronicSignature: 3,
  Webhooks: 4,
  FileImport: 5,
} as const

export type IntegrationKindValue = (typeof IntegrationKind)[keyof typeof IntegrationKind]

/** Mirrors DocOps.Domain.Integrations.IntegrationStatus. */
export const IntegrationStatus = {
  NotConfigured: 0,
  Connected: 1,
  Degraded: 2,
  Failing: 3,
  Disabled: 4,
} as const

export type IntegrationStatusValue = (typeof IntegrationStatus)[keyof typeof IntegrationStatus]

/** Mirrors DocOps.Domain.Integrations.IntegrationLogLevel. */
export const IntegrationLogLevel = {
  Information: 0,
  Warning: 1,
  Error: 2,
} as const

export type IntegrationLogLevelValue = (typeof IntegrationLogLevel)[keyof typeof IntegrationLogLevel]

/** Mirrors DocOps.Application.Integrations.IntegrationRunOutcome. */
export const IntegrationRunOutcome = {
  Succeeded: 0,
  PartiallyFailed: 1,
  Failed: 2,
  Skipped: 3,
} as const

export type IntegrationRunOutcomeValue = (typeof IntegrationRunOutcome)[keyof typeof IntegrationRunOutcome]

export interface IntegrationCard {
  id: string
  kind: IntegrationKindValue
  name: string
  description: string | null
  status: IntegrationStatusValue
  isEnabled: boolean
  lastRunAtUtc: string | null
  lastSuccessAtUtc: string | null
  lastActivityAtUtc: string | null
  lastRunSummary: string | null
  recordsProcessed: number
  errorCount: number

  /** Whether the far side is simulated, and — when it is — exactly what is simulated. */
  isSimulated: boolean
  simulationNotice: string | null

  requiresCredentials: boolean

  /** False only if no adapter is registered — should not happen outside a half-finished deploy. */
  hasAdapter: boolean

  /** The integration's own non-secret settings, as JSON — e.g. the webhook's configured address. */
  settingsJson: string | null
}

export interface IntegrationLogEntry {
  id: string
  occurredAtUtc: string
  level: IntegrationLogLevelValue
  operation: string
  summary: string
  detail: string | null
  recordsProcessed: number
  durationMs: number
  manual: boolean
}

export interface IntegrationRunResult {
  integrationId: string
  outcome: IntegrationRunOutcomeValue
  status: IntegrationStatusValue
  recordsProcessed: number
  summary: string
  ranAtUtc: string
  durationMs: number
}

export type IntegrationLogPage = PagedResult<IntegrationLogEntry>
