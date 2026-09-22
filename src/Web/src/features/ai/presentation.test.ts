import { describe, expect, it } from 'vitest'
import { MetadataFieldType } from '@/features/documents/types'
import { AiRunStatus, ConfidenceBand } from './types'
import { bandTone, differs, formatSuggestion, percent, runStatusTone, toInputValue } from './presentation'

describe('confidence', () => {
  it('colours a band by how much it should be trusted', () => {
    expect(bandTone(ConfidenceBand.High)).toBe('green')
    expect(bandTone(ConfidenceBand.Medium)).toBe('amber')
    expect(bandTone(ConfidenceBand.Low)).toBe('red')
    expect(bandTone(null)).toBe('neutral')
  })

  it('shows a confidence as a percentage, and an absent one as a dash', () => {
    expect(percent(0.935)).toBe('94%')
    expect(percent(0)).toBe('0%')
    expect(percent(null)).toBe('—')
  })

  it('colours a run by its state', () => {
    expect(runStatusTone(AiRunStatus.AwaitingReview)).toBe('amber')
    expect(runStatusTone(AiRunStatus.Applied)).toBe('green')
    expect(runStatusTone(AiRunStatus.Failed)).toBe('red')
  })
})

describe('formatSuggestion', () => {
  it('formats an amount in the currency the document itself states', () => {
    expect(formatSuggestion('3250.00', MetadataFieldType.Currency, 'pt-PT', 'GBP')).toContain('£')
    expect(formatSuggestion('1906.50', MetadataFieldType.Currency, 'pt-PT', 'EUR')).toContain('€')
  })

  it('never assumes a currency when the document does not state one', () => {
    const formatted = formatSuggestion('1906.50', MetadataFieldType.Currency, 'pt-PT', null)

    expect(formatted).not.toContain('€')
    expect(formatted).toContain('1906,50')
  })

  it('shows a value that was not found as a dash rather than as empty', () => {
    expect(formatSuggestion(null, MetadataFieldType.Text, 'pt-PT', null)).toBe('—')
  })
})

describe('toInputValue', () => {
  it('gives a date input the day it expects, dropping the stored time', () => {
    expect(toInputValue('2026-10-03T00:00:00.0000000+00:00', MetadataFieldType.Date)).toBe('2026-10-03')
  })

  it('leaves other values as they are stored', () => {
    expect(toInputValue('1906.50', MetadataFieldType.Currency)).toBe('1906.50')
    expect(toInputValue(null, MetadataFieldType.Text)).toBe('')
  })
})

describe('differs', () => {
  it('compares dates by the day, not by the stored text', () => {
    // The document holds an ISO instant; the suggestion holds a plain day. Same date.
    expect(differs('2026-09-03T00:00:00.0000000+00:00', '2026-09-03', MetadataFieldType.Date)).toBe(false)
    expect(differs('2026-09-03T00:00:00.0000000+00:00', '2026-10-10', MetadataFieldType.Date)).toBe(true)
  })

  it('compares amounts by value, not by how many decimals were stored', () => {
    expect(differs('1550.0000', '1550.00', MetadataFieldType.Currency)).toBe(false)
    expect(differs('1550.0000', '1560.00', MetadataFieldType.Currency)).toBe(true)
  })

  it('compares text ignoring case and surrounding space', () => {
    expect(differs(' Atlas Tecnologia, Lda. ', 'atlas tecnologia, lda.', MetadataFieldType.Text)).toBe(false)
    expect(differs('Atlas Tecnologia', 'Northstar Supplies', MetadataFieldType.Text)).toBe(true)
  })

  it('treats a missing value on either side as a difference', () => {
    expect(differs(null, '1550.00', MetadataFieldType.Currency)).toBe(true)
    expect(differs(null, null, MetadataFieldType.Currency)).toBe(false)
  })
})
