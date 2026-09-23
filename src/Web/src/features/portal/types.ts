export const PortalProgress = {
  Received: 0,
  InProgress: 1,
  Accepted: 2,
  Declined: 3,
} as const

export type PortalProgressValue = (typeof PortalProgress)[keyof typeof PortalProgress]

export interface PortalSubmission {
  id: string
  reference: string
  title: string
  status: number
  submittedAtUtc: string
  documentNumber: string | null
  submittedHere: boolean
  progress: PortalProgressValue
  awaitingResponse: boolean
}

export interface PortalMessage {
  id: string
  fromOrganisation: boolean
  body: string
  occurredAtUtc: string
}

export interface PortalSubmissionDetail {
  summary: PortalSubmission
  description: string | null
  fileName: string
  fileSizeBytes: number
  messages: PortalMessage[]
}
