/** The webhook's own settings, as the server stores them. */
export interface WebhookSettings {
  endpoint?: string | null
}

/**
 * Reads the webhook endpoint out of an integration's settings JSON. Malformed or missing settings
 * read as "no endpoint configured" rather than throwing — the same value a freshly seeded
 * integration has before anyone has configured anything.
 */
export function webhookEndpoint(settingsJson: string | null): string {
  if (!settingsJson) {
    return ''
  }

  try {
    const parsed = JSON.parse(settingsJson) as WebhookSettings
    return parsed.endpoint ?? ''
  } catch {
    return ''
  }
}
