import { describe, expect, it } from 'vitest'
import { statusTone } from './labels'
import { EmailStatus } from './types'

describe('statusTone', () => {
  it('colours each status distinctly, from arrival to filed away', () => {
    expect(statusTone(EmailStatus.Unread)).toBe('teal')
    expect(statusTone(EmailStatus.Read)).toBe('amber')
    expect(statusTone(EmailStatus.Processed)).toBe('green')
    expect(statusTone(EmailStatus.Archived)).toBe('neutral')
  })
})
