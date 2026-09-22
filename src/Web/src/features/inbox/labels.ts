import { EmailStatus, type EmailStatusValue } from './types'

/** The colour a status is shown in, kept apart from the components so this file stays reusable. */
export function statusTone(status: EmailStatusValue): 'neutral' | 'teal' | 'amber' | 'green' {
  switch (status) {
    case EmailStatus.Unread:
      return 'teal'
    case EmailStatus.Read:
      return 'amber'
    case EmailStatus.Processed:
      return 'green'
    default:
      return 'neutral'
  }
}
