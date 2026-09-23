import { describe, expect, it } from 'vitest'
import { integrationIcon, logLevelTone, statusTone } from './labels'
import { IntegrationKind, IntegrationLogLevel, IntegrationStatus } from './types'

describe('statusTone', () => {
  it('colours each status distinctly, from unconfigured to failing', () => {
    expect(statusTone(IntegrationStatus.NotConfigured)).toBe('teal')
    expect(statusTone(IntegrationStatus.Connected)).toBe('green')
    expect(statusTone(IntegrationStatus.Degraded)).toBe('amber')
    expect(statusTone(IntegrationStatus.Failing)).toBe('red')
    expect(statusTone(IntegrationStatus.Disabled)).toBe('neutral')
  })
})

describe('logLevelTone', () => {
  it('only warnings and errors stand out from the log', () => {
    expect(logLevelTone(IntegrationLogLevel.Information)).toBe('neutral')
    expect(logLevelTone(IntegrationLogLevel.Warning)).toBe('amber')
    expect(logLevelTone(IntegrationLogLevel.Error)).toBe('red')
  })
})

describe('integrationIcon', () => {
  it('gives every kind of integration its own glyph', () => {
    const icons = [
      IntegrationKind.MicrosoftOutlook,
      IntegrationKind.Erp,
      IntegrationKind.Crm,
      IntegrationKind.ElectronicSignature,
      IntegrationKind.Webhooks,
      IntegrationKind.FileImport,
    ].map(integrationIcon)

    // Every kind resolves to something, and no two kinds share a glyph — a shared icon would make
    // two different systems look like the same card at a glance.
    expect(icons).toHaveLength(new Set(icons).size)
  })
})
