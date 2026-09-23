import { describe, expect, it } from 'vitest'
import { progressTone } from './labels'
import { PortalProgress } from './types'

describe('progressTone', () => {
  it('separates a settled outcome from a final one and from work still going on', () => {
    expect(progressTone(PortalProgress.Received)).toBe('neutral')
    expect(progressTone(PortalProgress.InProgress)).toBe('teal')
    expect(progressTone(PortalProgress.Accepted)).toBe('green')
    expect(progressTone(PortalProgress.Declined)).toBe('red')
  })

  it('gives every progress value a tone, so a new one cannot render untoned', () => {
    const tones = Object.values(PortalProgress).map(progressTone)

    expect(tones).toHaveLength(Object.values(PortalProgress).length)
    expect(tones.every(Boolean)).toBe(true)
  })
})
