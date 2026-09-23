import { PortalProgress, type PortalProgressValue } from './types'

/** Colour follows meaning: accepted is settled, declined is final, in review is neutral. */
export function progressTone(progress: PortalProgressValue): 'teal' | 'green' | 'red' | 'neutral' {
  switch (progress) {
    case PortalProgress.Accepted:
      return 'green'
    case PortalProgress.Declined:
      return 'red'
    case PortalProgress.InProgress:
      return 'teal'
    default:
      return 'neutral'
  }
}
