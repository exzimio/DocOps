import { describe, expect, it } from 'vitest'
import { webhookEndpoint } from './settings'

describe('webhookEndpoint', () => {
  it('reads the configured address', () => {
    expect(webhookEndpoint('{"endpoint":"https://hooks.example/docops"}')).toBe(
      'https://hooks.example/docops',
    )
  })

  it('reads as unconfigured when there is no address yet', () => {
    expect(webhookEndpoint(null)).toBe('')
    expect(webhookEndpoint('{}')).toBe('')
    expect(webhookEndpoint('{"endpoint":null}')).toBe('')
  })

  it('reads malformed settings as unconfigured rather than throwing', () => {
    expect(webhookEndpoint('not json')).toBe('')
  })
})
