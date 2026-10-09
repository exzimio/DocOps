export function formatBytes(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined) {
    return '-'
  }

  if (bytes < 1024) {
    return `${bytes} B`
  }

  const units = ['kB', 'MB', 'GB']
  let value = bytes / 1024
  let unitIndex = 0

  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024
    unitIndex += 1
  }

  return `${value.toFixed(value < 10 ? 1 : 0)} ${units[unitIndex]}`
}

export function formatDate(value: string | null | undefined, locale: string): string {
  if (!value) {
    return '-'
  }

  return new Intl.DateTimeFormat(locale, { dateStyle: 'short' }).format(new Date(value))
}

export function formatDateTime(value: string | null | undefined, locale: string): string {
  if (!value) {
    return '-'
  }

  return new Intl.DateTimeFormat(locale, { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value))
}

/**
 * Metadata values arrive as canonical strings (ISO dates, invariant decimals). Rendering applies the
 * viewer's locale without ever changing what the server stored.
 */
export function formatMetadataValue(
  value: string | null,
  fieldType: number,
  locale: string,
  currency?: string | null,
): string {
  if (!value) {
    return '-'
  }

  switch (fieldType) {
    case 2:
      return formatDate(value, locale)
    case 1:
      return new Intl.NumberFormat(locale).format(Number(value))
    case 4:
      // The document's own currency when it has one; a bare amount otherwise, never an assumed euro.
      return currency && /^[A-Z]{3}$/.test(currency)
        ? new Intl.NumberFormat(locale, { style: 'currency', currency }).format(Number(value))
        : new Intl.NumberFormat(locale, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(
            Number(value),
          )
    case 3:
      return value.toLowerCase() === 'true' ? '✓' : '✗'
    default:
      return value
  }
}
