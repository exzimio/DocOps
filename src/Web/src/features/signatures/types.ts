export const SignatureStatus = {
  Pending: 0,
  Signed: 1,
  Declined: 2,
  Cancelled: 3,
} as const

export type SignatureStatusValue = (typeof SignatureStatus)[keyof typeof SignatureStatus]

export interface SignatureRequest {
  id: string
  documentId: string
  documentReference: string
  documentTitle: string
  versionNumber: number
  requestedContentHash: string
  signedContentHash: string | null
  /** False once the document has a newer version than the one signed. */
  contentStillMatches: boolean
  signerUserId: string
  signerDisplayName: string
  signerEmail: string
  requestedByDisplayName: string
  requestedAtUtc: string
  reason: string | null
  status: SignatureStatusValue
  completedAtUtc: string | null
  declineReason: string | null
  canSign: boolean
  canCancel: boolean
}
