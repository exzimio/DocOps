import { MetadataFieldType } from '@/features/documents/types'
import { formatMetadataValue } from '@/features/documents/format'
import { AiRunStatus, ConfidenceBand, type AiRunStatusValue, type ConfidenceBandValue } from './types'

export type Tone = 'neutral' | 'teal' | 'amber' | 'red' | 'green'

export function bandTone(band: ConfidenceBandValue | null | undefined): Tone {
  switch (band) {
    case ConfidenceBand.High:
      return 'green'
    case ConfidenceBand.Medium:
      return 'amber'
    case ConfidenceBand.Low:
      return 'red'
    default:
      return 'neutral'
  }
}

export function runStatusTone(status: AiRunStatusValue): Tone {
  switch (status) {
    case AiRunStatus.AwaitingReview:
      return 'amber'
    case AiRunStatus.Applied:
      return 'green'
    case AiRunStatus.Failed:
      return 'red'
    case AiRunStatus.Queued:
      return 'teal'
    default:
      return 'neutral'
  }
}

export function percent(value: number | null | undefined): string {
  return value === null || value === undefined ? '-' : `${Math.round(value * 100)}%`
}

/** A suggested or final value, shown the way the metadata panel shows it once it is written. */
export function formatSuggestion(
  value: string | null,
  fieldType: number,
  locale: string,
  currency: string | null,
): string {
  if (value === null || value === '') {
    return '-'
  }

  return formatMetadataValue(value, fieldType, locale, currency)
}

/** The form an edit input expects: yyyy-MM-dd for dates, the stored text otherwise. */
export function toInputValue(value: string | null, fieldType: number): string {
  if (!value) {
    return ''
  }

  return fieldType === MetadataFieldType.Date ? value.slice(0, 10) : value
}

/**
 * Whether two stored values mean different things. Values arrive in canonical but not identical
 * forms — a stored date carries a time, a stored amount may carry more decimals — so they are
 * compared by meaning, not as text.
 */
export function differs(a: string | null, b: string | null, fieldType: number): boolean {
  if (a === null || b === null) {
    return a !== b
  }

  switch (fieldType) {
    case MetadataFieldType.Date:
      return a.slice(0, 10) !== b.slice(0, 10)
    case MetadataFieldType.Number:
    case MetadataFieldType.Currency:
      return Number(a) !== Number(b)
    default:
      return a.trim().toLowerCase() !== b.trim().toLowerCase()
  }
}
