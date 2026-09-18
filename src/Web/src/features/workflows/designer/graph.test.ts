import { describe, expect, it } from 'vitest'
import { WorkflowNodeType } from '../types'
import type { WorkflowEdge, WorkflowNode } from '../types'
import { draftIssues, layout, toDraft, toSaveEdges, toSaveNodes, type Draft } from './graph'
import { buildNodeKey } from './nodeCatalogue'
import { branchOptions } from './branches'
import { parseConfiguration, serialiseConfiguration } from './nodeConfiguration'

function node(partial: Partial<WorkflowNode> & Pick<WorkflowNode, 'key' | 'type'>): WorkflowNode {
  return {
    id: partial.key,
    name: partial.key,
    description: null,
    configJson: null,
    positionX: 0,
    positionY: 0,
    ...partial,
  }
}

function edge(from: string, to: string, branch: string | null = null): WorkflowEdge {
  return { id: `${from}-${to}`, fromNodeKey: from, toNodeKey: to, label: null, branch }
}

describe('toDraft', () => {
  it('lays the graph out when every stored node sits at the origin', () => {
    const draft = toDraft(
      [
        node({ key: 'start', type: WorkflowNodeType.Start }),
        node({ key: 'task', type: WorkflowNodeType.HumanTask }),
        node({ key: 'end', type: WorkflowNodeType.End }),
      ],
      [edge('start', 'task'), edge('task', 'end')],
    )

    expect(draft.nodes.map((n) => n.positionX)).toEqual([0, 320, 640])
  })

  it('keeps the positions a designer already chose', () => {
    const draft = toDraft(
      [
        node({ key: 'start', type: WorkflowNodeType.Start, positionX: 40, positionY: 90 }),
        node({ key: 'end', type: WorkflowNodeType.End, positionX: 500, positionY: 90 }),
      ],
      [edge('start', 'end')],
    )

    expect(draft.nodes[0]).toMatchObject({ positionX: 40, positionY: 90 })
    expect(draft.nodes[1]).toMatchObject({ positionX: 500, positionY: 90 })
  })
})

describe('layout', () => {
  it('stacks the two sides of a branch in the same column', () => {
    const laid = layout(
      [
        {
          key: 'start',
          name: 'Início',
          description: null,
          type: WorkflowNodeType.Start,
          configuration: {},
          positionX: 0,
          positionY: 0,
        },
        {
          key: 'yes',
          name: 'Sim',
          description: null,
          type: WorkflowNodeType.Approval,
          configuration: {},
          positionX: 0,
          positionY: 0,
        },
        {
          key: 'no',
          name: 'Não',
          description: null,
          type: WorkflowNodeType.End,
          configuration: {},
          positionX: 0,
          positionY: 0,
        },
      ],
      [
        { id: '1', fromNodeKey: 'start', toNodeKey: 'yes', label: null, branch: 'true' },
        { id: '2', fromNodeKey: 'start', toNodeKey: 'no', label: null, branch: 'false' },
      ],
    )

    const [start, yes, no] = laid
    expect(start!.positionX).toBe(0)
    expect(yes!.positionX).toBe(320)
    expect(no!.positionX).toBe(320)
    expect(yes!.positionY).not.toBe(no!.positionY)
  })

  it('gives a node nothing reaches a column of its own instead of dropping it', () => {
    const laid = layout(
      [
        {
          key: 'start',
          name: 'Início',
          description: null,
          type: WorkflowNodeType.Start,
          configuration: {},
          positionX: 0,
          positionY: 0,
        },
        {
          key: 'orphan',
          name: 'Solto',
          description: null,
          type: WorkflowNodeType.Notification,
          configuration: {},
          positionX: 0,
          positionY: 0,
        },
      ],
      [],
    )

    expect(laid).toHaveLength(2)
    expect(laid[1]!.positionX).toBeGreaterThan(laid[0]!.positionX)
  })
})

describe('draftIssues', () => {
  const draft = (nodes: Draft['nodes'], edges: Draft['edges']): Draft => ({ nodes, edges })

  it('reports a step with no way out and a step nothing reaches', () => {
    const issues = draftIssues(
      draft(
        [
          {
            key: 'start',
            name: 'Início',
            description: null,
            type: WorkflowNodeType.Start,
            configuration: {},
            positionX: 0,
            positionY: 0,
          },
          {
            key: 'solto',
            name: 'Solto',
            description: null,
            type: WorkflowNodeType.HumanTask,
            configuration: {},
            positionX: 0,
            positionY: 0,
          },
          {
            key: 'end',
            name: 'Fim',
            description: null,
            type: WorkflowNodeType.End,
            configuration: {},
            positionX: 0,
            positionY: 0,
          },
        ],
        [{ id: '1', fromNodeKey: 'start', toNodeKey: 'end', label: null, branch: null }],
      ),
    )

    expect(issues).toContainEqual({ nodeKey: 'solto', code: 'node_without_exit' })
    expect(issues).toContainEqual({ nodeKey: 'solto', code: 'node_unreachable' })
  })

  it('reports a condition that is missing one of its two branches', () => {
    const issues = draftIssues(
      draft(
        [
          {
            key: 'start',
            name: 'Início',
            description: null,
            type: WorkflowNodeType.Start,
            configuration: {},
            positionX: 0,
            positionY: 0,
          },
          {
            key: 'cond',
            name: 'Condição',
            description: null,
            type: WorkflowNodeType.Condition,
            configuration: {},
            positionX: 0,
            positionY: 0,
          },
          {
            key: 'end',
            name: 'Fim',
            description: null,
            type: WorkflowNodeType.End,
            configuration: {},
            positionX: 0,
            positionY: 0,
          },
        ],
        [
          { id: '1', fromNodeKey: 'start', toNodeKey: 'cond', label: null, branch: null },
          { id: '2', fromNodeKey: 'cond', toNodeKey: 'end', label: null, branch: 'true' },
        ],
      ),
    )

    expect(issues).toContainEqual({ nodeKey: 'cond', code: 'condition_missing_branch' })
  })

  it('says nothing about a process that is complete', () => {
    const issues = draftIssues(
      draft(
        [
          {
            key: 'start',
            name: 'Início',
            description: null,
            type: WorkflowNodeType.Start,
            configuration: {},
            positionX: 0,
            positionY: 0,
          },
          {
            key: 'end',
            name: 'Fim',
            description: null,
            type: WorkflowNodeType.End,
            configuration: {},
            positionX: 0,
            positionY: 0,
          },
        ],
        [{ id: '1', fromNodeKey: 'start', toNodeKey: 'end', label: null, branch: null }],
      ),
    )

    expect(issues).toEqual([])
  })
})

describe('buildNodeKey', () => {
  it('produces a key the server accepts from an accented label', () => {
    expect(buildNodeKey('Aprovação de direção', [])).toMatch(/^[a-z0-9][a-z0-9-]*$/)
  })

  it('never reuses a key that is already on the canvas', () => {
    expect(buildNodeKey('Aprovação', ['aprovacao'])).toBe('aprovacao-2')
    expect(buildNodeKey('Aprovação', ['aprovacao', 'aprovacao-2'])).toBe('aprovacao-3')
  })

  it('falls back to a usable key when the label has nothing to slug', () => {
    expect(buildNodeKey('***', [])).toMatch(/^[a-z0-9][a-z0-9-]*$/)
  })
})

describe('branchOptions', () => {
  it('offers only the branch names the engine matches on', () => {
    expect(branchOptions(WorkflowNodeType.Condition)).toEqual(['true', 'false'])
    expect(branchOptions(WorkflowNodeType.Approval)).toEqual(['approved', 'rejected'])
    expect(branchOptions(WorkflowNodeType.Notification)).toEqual([])
  })
})

describe('node configuration', () => {
  it('drops empty values so a node stores only what it needs', () => {
    const json = serialiseConfiguration({ taskTitle: 'Aprovar', taskDescription: '', dueInHours: null })

    expect(JSON.parse(json!)).toEqual({ taskTitle: 'Aprovar' })
  })

  it('stores nothing at all when a node has no settings', () => {
    expect(serialiseConfiguration({})).toBeNull()
  })

  it('shows an unreadable configuration as empty rather than breaking the canvas', () => {
    expect(parseConfiguration('{ not json')).toEqual({})
    expect(parseConfiguration(null)).toEqual({})
  })
})

describe('save payload', () => {
  it('rounds positions and trims what the designer typed', () => {
    const draft: Draft = {
      nodes: [
        {
          key: 'task',
          name: '  Tarefa  ',
          description: '   ',
          type: WorkflowNodeType.HumanTask,
          configuration: { taskTitle: 'Rever' },
          positionX: 120.4,
          positionY: 61.7,
        },
      ],
      edges: [{ id: '1', fromNodeKey: 'task', toNodeKey: 'task', label: '  Sim  ', branch: 'true' }],
    }

    expect(toSaveNodes(draft)[0]).toMatchObject({
      name: 'Tarefa',
      description: null,
      positionX: 120,
      positionY: 62,
      configJson: '{"taskTitle":"Rever"}',
    })
    expect(toSaveEdges(draft)[0]).toMatchObject({ label: 'Sim', branch: 'true' })
  })
})
