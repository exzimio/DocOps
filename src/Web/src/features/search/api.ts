import { useMutation, useQuery } from '@tanstack/react-query'
import { ApiError, api } from '@/lib/api'
import { hasCriteria, type NaturalLanguageSearchResult, type SearchFilters, type SearchResult } from './types'

export const searchKeys = {
  all: ['search'] as const,
  results: (filters: SearchFilters) => ['search', 'results', filters] as const,
}

export function buildSearchParams(filters: SearchFilters): string {
  const params = new URLSearchParams()

  if (filters.query.trim()) params.set('query', filters.query.trim())
  filters.typeIds.forEach((id) => params.append('typeIds', id))
  filters.statuses.forEach((status) => params.append('statuses', String(status)))
  filters.departmentIds.forEach((id) => params.append('departmentIds', id))
  filters.tagIds.forEach((id) => params.append('tagIds', id))
  filters.classifications.forEach((value) => params.append('classifications', String(value)))
  filters.workflowStates.forEach((value) => params.append('workflowStates', String(value)))
  filters.approvalStates.forEach((value) => params.append('approvalStates', String(value)))
  if (filters.ownerUserId) params.set('ownerUserId', filters.ownerUserId)
  if (filters.createdFromUtc) params.set('createdFromUtc', filters.createdFromUtc)
  if (filters.createdToUtc) params.set('createdToUtc', filters.createdToUtc)
  if (filters.onlyMine) params.set('onlyMine', 'true')
  if (!filters.includeArchived) params.set('includeArchived', 'false')
  params.set('sortBy', filters.sortBy)
  params.set('sortDescending', String(filters.sortDescending))
  params.set('page', String(filters.page))
  params.set('pageSize', String(filters.pageSize))

  return params.toString()
}

/**
 * Runs the search. Nothing is requested until something is asked for: an empty search screen would
 * otherwise page through the whole repository to show a list the person did not ask to see.
 */
export function useSearch(filters: SearchFilters) {
  return useQuery<SearchResult, ApiError>({
    queryKey: searchKeys.results(filters),
    queryFn: () => api.get<SearchResult>(`/api/search?${buildSearchParams(filters)}`),
    enabled: hasCriteria(filters),
    placeholderData: (previous) => previous,
  })
}

export function useNaturalLanguageSearch() {
  return useMutation<NaturalLanguageSearchResult, ApiError, { question: string; pageSize: number }>({
    mutationFn: ({ question, pageSize }) =>
      api.post<NaturalLanguageSearchResult>('/api/search/natural-language', {
        question,
        paging: { page: 1, pageSize },
      }),
  })
}
