import type { MetadataFieldTypeValue } from '@/features/documents/types'

/** Where AI output came from. Mirrors DocOps.Domain.Intelligence.AiSource. */
export const AiSource = {
  Demo: 0,
  Model: 1,
} as const

export type AiSourceValue = (typeof AiSource)[keyof typeof AiSource]

export const AiRunStatus = {
  Queued: 0,
  AwaitingReview: 1,
  Applied: 2,
  Discarded: 3,
  Failed: 4,
} as const

export type AiRunStatusValue = (typeof AiRunStatus)[keyof typeof AiRunStatus]

export const AiRunOrigin = {
  User: 0,
  Workflow: 1,
} as const

export type AiRunOriginValue = (typeof AiRunOrigin)[keyof typeof AiRunOrigin]

export const AiSuggestionDecision = {
  Pending: 0,
  Accepted: 1,
  Edited: 2,
  Rejected: 3,
} as const

export type AiSuggestionDecisionValue = (typeof AiSuggestionDecision)[keyof typeof AiSuggestionDecision]

export const ConfidenceBand = {
  Low: 0,
  Medium: 1,
  High: 2,
} as const

export type ConfidenceBandValue = (typeof ConfidenceBand)[keyof typeof ConfidenceBand]

export interface AiProvider {
  source: AiSourceValue
  name: string
  description: string
  isDemo: boolean
}

export interface TextReference {
  line: number
  page: number
  section: string | null
  excerpt: string
}

export interface AiDocumentTypeRef {
  id: string
  code: string
  name: string
}

export interface AiFieldSuggestion {
  id: string
  fieldCode: string
  label: string
  fieldType: MetadataFieldTypeValue
  isRequired: boolean
  options: string[]
  suggestedValue: string | null
  confidence: number
  band: ConfidenceBandValue
  evidence: TextReference | null
  decision: AiSuggestionDecisionValue
  finalValue: string | null
  currentValue: string | null
  decidedByName: string | null
  decidedAtUtc: string | null
}

export interface AiRun {
  id: string
  documentId: string
  documentReference: string
  documentTitle: string
  versionNumber: number
  status: AiRunStatusValue
  origin: AiRunOriginValue
  source: AiSourceValue
  providerName: string | null
  createdAtUtc: string
  completedAtUtc: string | null
  appliedAtUtc: string | null
  requestedByName: string | null
  appliedByName: string | null
  failureReason: string | null
  originalType: AiDocumentTypeRef
  suggestedType: AiDocumentTypeRef | null
  classificationConfidence: number | null
  classificationBand: ConfidenceBandValue | null
  classificationEvidence: TextReference | null
  classificationDecision: AiSuggestionDecisionValue
  fields: AiFieldSuggestion[]
  missingRequiredFields: string[]
  isStale: boolean
  canReview: boolean
  canApply: boolean
}

export interface AiRunListItem {
  id: string
  documentId: string
  documentReference: string
  documentTitle: string
  documentTypeName: string
  status: AiRunStatusValue
  origin: AiRunOriginValue
  source: AiSourceValue
  suggestedTypeName: string | null
  classificationConfidence: number | null
  fieldsFound: number
  fieldsMissing: number
  fieldsDecided: number
  createdAtUtc: string
  requestedByName: string | null
}

export interface AiStatistics {
  awaitingReview: number
  appliedLast30Days: number
  suggestionsDecided: number
  suggestionsAccepted: number
  suggestionsEdited: number
  suggestionsRejected: number
  acceptanceRate: number | null
}

export interface AiCitation {
  kind: 'document' | 'audit'
  documentTitle: string
  fileName: string | null
  versionNumber: number | null
  page: number | null
  line: number | null
  section: string | null
  excerpt: string
  occurredAtUtc: string | null
}

export interface DocumentAnswer {
  answered: boolean
  text: string
  confidence: number
  source: AiSourceValue
  providerName: string
  citations: AiCitation[]
  discardedCitations: number
}

export interface DocumentTextLine {
  number: number
  page: number
  section: string | null
  text: string
}

export interface DocumentText {
  versionNumber: number
  fileName: string
  contentType: string
  hasText: boolean
  truncated: boolean
  lines: DocumentTextLine[]
}
