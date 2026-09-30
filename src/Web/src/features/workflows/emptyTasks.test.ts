import { describe, expect, it } from 'vitest'
import { emptyTasksBodyKey, narrowingFilterKeys, widestTaskFilters } from './emptyTasks'
import { WorkflowTaskStatus, defaultTaskFilters, type TaskFilters } from './types'

const filtros = (parcial: Partial<TaskFilters> = {}): TaskFilters => ({
  ...defaultTaskFilters,
  ...parcial,
})

describe('narrowingFilterKeys', () => {
  it('nomeia os dois filtros que já vêm postos por omissão', () => {
    // O ecrã abre com «as minhas» e «só as por fechar». É por isso que a mensagem antiga estava
    // quase sempre a afirmar uma causa errada: a lista vazia raramente significava ausência de
    // trabalho.
    expect(narrowingFilterKeys(filtros(), true)).toEqual(['tasks.narrowed.Mine', 'tasks.narrowed.open'])
  })

  it('não conta o âmbito a quem não pode sair dele', () => {
    // Para quem não tem `tasks.view.all`, «as minhas» não é um filtro — é tudo o que existe. Dizer-
    // lhe que alargue seria mandá-lo carregar num botão que não muda nada.
    expect(narrowingFilterKeys(filtros({ statuses: [] }), false)).toEqual([])
  })

  it('conta o atraso e o estado por aberto', () => {
    expect(narrowingFilterKeys(filtros({ scope: 'All', onlyOverdue: true, statuses: [] }), true)).toEqual([
      'tasks.narrowed.overdue',
    ])

    expect(
      narrowingFilterKeys(filtros({ scope: 'All', statuses: [WorkflowTaskStatus.Pending] }), true),
    ).toEqual(['tasks.narrowed.open'])
  })

  it('não encontra nada a alargar no ecrã mais largo de todos', () => {
    expect(narrowingFilterKeys(filtros({ scope: 'All', statuses: [], onlyOverdue: false }), true)).toEqual([])
  })
})

describe('widestTaskFilters', () => {
  it('desfaz exatamente o que a mensagem nomeou', () => {
    const antes = filtros({ scope: 'Mine', onlyOverdue: true, page: 3 })
    const depois = widestTaskFilters(antes, true)

    expect(depois.scope).toBe('All')
    expect(depois.statuses).toEqual([])
    expect(depois.onlyOverdue).toBe(false)

    // Voltar à primeira página: alargar traz mais resultados, e a página três da lista anterior não
    // é a página três desta.
    expect(depois.page).toBe(1)
  })

  it('não promove ao âmbito de toda a organização quem não o pode ver', () => {
    // O servidor recusaria de qualquer forma; o que importa é o ecrã não pedir o que sabe que é
    // recusado.
    expect(widestTaskFilters(filtros({ scope: 'Mine' }), false).scope).toBe('Mine')
    expect(widestTaskFilters(filtros({ scope: 'Delegated' }), false).scope).toBe('Delegated')
  })

  it('preserva o tamanho de página escolhido', () => {
    expect(widestTaskFilters(filtros({ pageSize: 50 }), true).pageSize).toBe(50)
  })
})

describe('emptyTasksBodyKey', () => {
  it('afirma sobre o trabalho aquilo que cada âmbito permite afirmar', () => {
    expect(emptyTasksBodyKey('All')).toBe('tasks.emptyNone')
    expect(emptyTasksBodyKey('Delegated')).toBe('tasks.emptyDelegated')
    expect(emptyTasksBodyKey('Mine')).toBe('tasks.emptyBody')
  })
})
