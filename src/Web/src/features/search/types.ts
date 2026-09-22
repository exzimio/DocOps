import type {
  DocumentListItem,
  DocumentStatusValue,
  PagedResult,
  SecurityClassificationValue,
} from '@/features/documents/types'

/** Mirrors `DocumentWorkflowState`. Derived from the workflows that ran on a document. */
export const WorkflowState = {
  None: 0,
  InProgress: 1,
  Completed: 2,
  Rejected: 3,
  Cancelled: 4,
  Failed: 5,
} as const

export type WorkflowStateValue = (typeof WorkflowState)[keyof typeof WorkflowState]

/** Mirrors `DocumentApprovalState`. Derived from the decision tasks raised for a document. */
export const ApprovalState = {
  NotRequested: 0,
  Awaiting: 1,
  Approved: 2,
  Rejected: 3,
  InformationRequested: 4,
} as const

export type ApprovalStateValue = (typeof ApprovalState)[keyof typeof ApprovalState]

/** Mirrors `SearchMatchField`: which part of a document the terms were found in. */
export const MatchField = {
  Title: 0,
  Reference: 1,
  Summary: 2,
  Content: 3,
  Metadata: 4,
  Tag: 5,
} as const

export type MatchFieldValue = (typeof MatchField)[keyof typeof MatchField]

/** Mirrors `AiOrigin`. A demo reading is never shown as a model's. */
export const AiOrigin = {
  Demo: 0,
  Model: 1,
} as const

export type AiOriginValue = (typeof AiOrigin)[keyof typeof AiOrigin]

export interface SearchSnippet {
  text: string
  start: number
  length: number
}

export interface SearchHit {
  document: DocumentListItem
  score: number
  matchedIn: MatchFieldValue[]
  snippet: SearchSnippet | null
}

export interface SearchDiagnostics {
  fullTextIndexUsed: boolean
  fullTextAvailable: boolean
  terms: string[]
  elapsedMilliseconds: number
}

export interface SearchResult {
  page: PagedResult<SearchHit>
  diagnostics: SearchDiagnostics
}

export interface InterpretedFilter {
  field: string
  value: string
}

export interface SearchInterpretation {
  question: string
  filters: InterpretedFilter[]
  text: string | null
  understood: boolean
  source: AiOriginValue
}

export interface NaturalLanguageSearchResult {
  interpretation: SearchInterpretation
  result: SearchResult
}

export type SearchSortField = 'Relevance' | 'CreatedAt' | 'ModifiedAt' | 'Title' | 'Reference' | 'Status'

export interface SearchFilters {
  query: string
  typeIds: string[]
  statuses: DocumentStatusValue[]
  departmentIds: string[]
  tagIds: string[]
  classifications: SecurityClassificationValue[]
  workflowStates: WorkflowStateValue[]
  approvalStates: ApprovalStateValue[]
  ownerUserId: string | null
  createdFromUtc: string | null
  createdToUtc: string | null
  onlyMine: boolean
  includeArchived: boolean
  sortBy: SearchSortField
  sortDescending: boolean
  page: number
  pageSize: number
}

export const defaultSearchFilters: SearchFilters = {
  query: '',
  typeIds: [],
  statuses: [],
  departmentIds: [],
  tagIds: [],
  classifications: [],
  workflowStates: [],
  approvalStates: [],
  ownerUserId: null,
  createdFromUtc: null,
  createdToUtc: null,
  onlyMine: false,
  includeArchived: true,
  sortBy: 'Relevance',
  sortDescending: true,
  page: 1,
  pageSize: 20,
}

/**
 * Whether anything narrows the search. Used to decide between "no results" and "nothing asked yet",
 * which are different things to tell someone.
 */
export function hasCriteria(filters: SearchFilters): boolean {
  return (
    filters.query.trim() !== '' ||
    filters.typeIds.length > 0 ||
    filters.statuses.length > 0 ||
    filters.departmentIds.length > 0 ||
    filters.tagIds.length > 0 ||
    filters.classifications.length > 0 ||
    filters.workflowStates.length > 0 ||
    filters.approvalStates.length > 0 ||
    filters.ownerUserId !== null ||
    filters.createdFromUtc !== null ||
    filters.createdToUtc !== null ||
    filters.onlyMine ||
    !filters.includeArchived
  )
}
