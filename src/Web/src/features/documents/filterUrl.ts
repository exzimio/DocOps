import {
  defaultFilters,
  type DocumentFilters,
  type DocumentStatusValue,
  type SecurityClassificationValue,
} from './types'

/**
 * Os filtros da listagem, escritos no endereço e lidos de volta.
 *
 * Sem isto, uma vista filtrada existe apenas dentro daquele separador: não se envia a um colega,
 * não se guarda nos favoritos, e recarregar a página perde-a. Num produto operacional, «faturas por
 * aprovar do departamento financeiro» é uma coisa que se diz a alguém — e dizer-se-lhe com uma
 * ligação é a diferença entre uma ferramenta e um ecrã.
 *
 * Só o que difere do estado por omissão é escrito. Um endereço que enumerasse os doze filtros
 * mesmo quando nenhum está posto seria ilegível, e mudaria de forma sem que nada tivesse mudado de
 * conteúdo.
 */

const LISTAS = ['typeIds', 'departmentIds', 'tagIds'] as const

export function filtersToSearchParams(filters: DocumentFilters): URLSearchParams {
  const params = new URLSearchParams()

  if (filters.query) {
    params.set('q', filters.query)
  }

  for (const chave of LISTAS) {
    if (filters[chave].length > 0) {
      params.set(chave, filters[chave].join(','))
    }
  }

  if (filters.statuses.length > 0) {
    params.set('statuses', filters.statuses.join(','))
  }

  if (filters.classifications.length > 0) {
    params.set('classifications', filters.classifications.join(','))
  }

  if (filters.onlyMine) {
    params.set('onlyMine', '1')
  }

  // Escrito só quando difere da omissão, que é «incluir»: o endereço fala do que foi decidido.
  if (!filters.includeArchived) {
    params.set('includeArchived', '0')
  }

  if (filters.page > 1) {
    params.set('page', String(filters.page))
  }

  if (filters.sortBy !== defaultFilters.sortBy) {
    params.set('sortBy', filters.sortBy)
  }

  if (filters.sortDescending !== defaultFilters.sortDescending) {
    params.set('sortDescending', filters.sortDescending ? '1' : '0')
  }

  return params
}

export function filtersFromSearchParams(params: URLSearchParams): DocumentFilters {
  const lista = (chave: string): string[] => {
    const cru = params.get(chave)

    return cru ? cru.split(',').filter((valor) => valor.length > 0) : []
  }

  const numeros = (chave: string): number[] =>
    lista(chave)
      .map((valor) => Number(valor))
      .filter((valor) => Number.isInteger(valor))

  const pagina = Number(params.get('page'))

  return {
    ...defaultFilters,
    query: params.get('q') ?? '',
    typeIds: lista('typeIds'),
    departmentIds: lista('departmentIds'),
    tagIds: lista('tagIds'),
    statuses: numeros('statuses') as DocumentStatusValue[],
    classifications: numeros('classifications') as SecurityClassificationValue[],
    onlyMine: params.get('onlyMine') === '1',
    includeArchived: params.get('includeArchived') !== '0',

    // Um endereço escrito à mão pode trazer disparates. Uma página zero ou «abc» não é motivo para
    // partir o ecrã: vale a primeira.
    page: Number.isInteger(pagina) && pagina > 0 ? pagina : 1,
    sortBy: (params.get('sortBy') as DocumentFilters['sortBy']) || defaultFilters.sortBy,
    sortDescending: params.get('sortDescending')
      ? params.get('sortDescending') === '1'
      : defaultFilters.sortDescending,
  }
}
