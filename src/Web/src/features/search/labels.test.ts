import { describe, expect, it } from 'vitest'
import type { TFunction } from 'i18next'
import { describeFilter, matchFieldKey } from './labels'
import { MatchField } from './types'

/**
 * A fake translator that returns the key it was asked for, or the default when the key is one the
 * locale would not have. It makes the mapping visible: what is asserted here is which key each
 * value resolves to, not how it happens to be worded today.
 */
const t = ((key: string, options?: { defaultValue?: string }) =>
  key.startsWith('missing.') ? (options?.defaultValue ?? key) : key) as unknown as TFunction

describe('matchFieldKey', () => {
  it('names every part of a document a term can be found in', () => {
    expect(matchFieldKey(MatchField.Title)).toBe('search.matched.title')
    expect(matchFieldKey(MatchField.Reference)).toBe('search.matched.reference')
    expect(matchFieldKey(MatchField.Summary)).toBe('search.matched.summary')
    expect(matchFieldKey(MatchField.Content)).toBe('search.matched.content')
    expect(matchFieldKey(MatchField.Metadata)).toBe('search.matched.metadata')
    expect(matchFieldKey(MatchField.Tag)).toBe('search.matched.tag')
  })
})

describe('describeFilter', () => {
  it('translates an enumeration the server sent by name', () => {
    expect(describeFilter({ field: 'status', value: 'PendingApproval' }, t)).toEqual({
      label: 'search.interpreted.status',
      value: 'documents.statusValues.2',
    })

    expect(describeFilter({ field: 'classification', value: 'Confidential' }, t)).toEqual({
      label: 'search.interpreted.classification',
      value: 'documents.classificationValues.2',
    })
  })

  it('translates the states that only the search has', () => {
    expect(describeFilter({ field: 'approvalState', value: 'Awaiting' }, t).value).toBe(
      'search.approvalValues.Awaiting',
    )
    expect(describeFilter({ field: 'workflowState', value: 'InProgress' }, t).value).toBe(
      'search.workflowValues.InProgress',
    )
  })

  it('shows a catalogue name as it is, because it is already the tenant’s own wording', () => {
    expect(describeFilter({ field: 'type', value: 'Fatura de fornecedor' }, t)).toEqual({
      label: 'search.interpreted.type',
      value: 'Fatura de fornecedor',
    })
  })

  it('shows a date as the server sent it', () => {
    expect(describeFilter({ field: 'createdFrom', value: '2026-08-01' }, t).value).toBe('2026-08-01')
  })

  /**
   * A value the interface has no label for must still appear: a filter that is narrowing the results
   * and is invisible is worse than one worded awkwardly.
   */
  it('falls back to the raw value rather than showing an empty chip', () => {
    const translator = ((key: string, options?: { defaultValue?: string }) =>
      options?.defaultValue ?? key) as unknown as TFunction

    expect(describeFilter({ field: 'status', value: 'SomethingNewer' }, translator).value).toBe(
      'SomethingNewer',
    )
    expect(describeFilter({ field: 'unknownField', value: 'x' }, translator)).toEqual({
      label: 'unknownField',
      value: 'x',
    })
  })
})
