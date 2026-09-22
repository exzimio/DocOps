import type { TFunction } from 'i18next'
import { MatchField, type InterpretedFilter, type MatchFieldValue } from './types'

/** The translation key for each part of a document a term can be found in. */
export function matchFieldKey(field: MatchFieldValue): string {
  switch (field) {
    case MatchField.Title:
      return 'search.matched.title'
    case MatchField.Reference:
      return 'search.matched.reference'
    case MatchField.Summary:
      return 'search.matched.summary'
    case MatchField.Content:
      return 'search.matched.content'
    case MatchField.Metadata:
      return 'search.matched.metadata'
    default:
      return 'search.matched.tag'
  }
}

/**
 * Turns one filter the question reader produced into words.
 *
 * The server sends a stable key and a raw value, never a sentence, so both languages are built here
 * from the labels the rest of the interface already uses. An enumeration whose label is missing
 * falls back to its own name rather than to an empty chip: showing nothing would hide a filter that
 * is narrowing the results.
 */
export function describeFilter(filter: InterpretedFilter, t: TFunction): { label: string; value: string } {
  const label = t(`search.interpreted.${filter.field}`, { defaultValue: filter.field })

  switch (filter.field) {
    case 'status':
      return {
        label,
        value: translateEnum(t, 'documents.statusValues', statusIndex(filter.value), filter.value),
      }
    case 'classification':
      return {
        label,
        value: translateEnum(
          t,
          'documents.classificationValues',
          classificationIndex(filter.value),
          filter.value,
        ),
      }
    case 'approvalState':
      return { label, value: t(`search.approvalValues.${filter.value}`, { defaultValue: filter.value }) }
    case 'workflowState':
      return { label, value: t(`search.workflowValues.${filter.value}`, { defaultValue: filter.value }) }
    case 'onlyMine':
      return { label, value: t('search.interpreted.onlyMineValue') }
    default:
      return { label, value: filter.value }
  }
}

const statusNames = [
  'Draft',
  'InReview',
  'PendingApproval',
  'Approved',
  'Rejected',
  'SignatureRequired',
  'Signed',
  'Archived',
]

const classificationNames = ['Public', 'Internal', 'Confidential', 'Restricted']

function statusIndex(name: string): number {
  return statusNames.indexOf(name)
}

function classificationIndex(name: string): number {
  return classificationNames.indexOf(name)
}

function translateEnum(t: TFunction, prefix: string, index: number, fallback: string): string {
  return index < 0 ? fallback : t(`${prefix}.${index}`, { defaultValue: fallback })
}
