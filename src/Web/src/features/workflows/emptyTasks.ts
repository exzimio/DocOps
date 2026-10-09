import { defaultTaskFilters, type TaskFilters, type TaskScope } from './types'

/**
 * O que dizer quando a lista de tarefas sai vazia.
 *
 * Uma frase única, «quando um processo lhe atribuir trabalho, aparece aqui», só é verdade num dos
 * casos. Os filtros por omissão são dois (as minhas, e só as que continuam por fechar), portanto essa
 * frase apareceria quase sempre a afirmar uma causa que o ecrã não conhece: a pessoa leria que não
 * tem trabalho, acreditaria, e sairia do ecrã quando bastava alargar. Um ecrã que afirma com
 * confiança o que não sabe é a mesma falsidade que uma página de marcador de posição, só que mais
 * difícil de ver.
 *
 * O que estas funções decidem é o que *pode* ser alargado, e não o que está apertado em abstrato:
 * quem não tem `tasks.view.all` já está a ver o máximo que lhe é permitido, e oferecer-lhe um botão
 * que não muda nada seria um botão sem ação.
 */

/**
 * As restrições em vigor que o botão de alargar vai desfazer, como chaves de tradução.
 *
 * A lista é exatamente a mesma coisa que {@link widestTaskFilters} altera — o texto nomeia o que o
 * botão faz, e não uma ideia geral de «filtros».
 */
export function narrowingFilterKeys(filters: TaskFilters, canViewAll: boolean): string[] {
  const keys: string[] = []

  // O âmbito só conta como restrição para quem pode sair dele. Para os outros, «as minhas» não é um
  // filtro: é o universo.
  if (canViewAll && filters.scope !== 'All') {
    keys.push(`tasks.narrowed.${filters.scope}`)
  }

  if (filters.onlyOverdue) {
    keys.push('tasks.narrowed.overdue')
  }

  if (filters.statuses.length > 0) {
    keys.push('tasks.narrowed.open')
  }

  return keys
}

/** Os filtros mais largos que esta pessoa pode ver, a partir dos que tem postos. */
export function widestTaskFilters(filters: TaskFilters, canViewAll: boolean): TaskFilters {
  return {
    ...filters,
    scope: canViewAll ? 'All' : filters.scope,
    statuses: [],
    onlyOverdue: false,
    page: defaultTaskFilters.page,
  }
}

/**
 * A frase para quando não há nada para alargar — e aí a lista vazia é mesmo um facto sobre o
 * trabalho, não sobre os filtros. Cada âmbito afirma o seu: dizer «quando um processo lhe atribuir
 * trabalho» a quem está a ver as tarefas que delegou seria falar de outra coisa.
 */
export function emptyTasksBodyKey(scope: TaskScope): string {
  switch (scope) {
    case 'All':
      return 'tasks.emptyNone'
    case 'Delegated':
      return 'tasks.emptyDelegated'
    default:
      return 'tasks.emptyBody'
  }
}
