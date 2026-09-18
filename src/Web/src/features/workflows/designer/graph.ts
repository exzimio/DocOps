import type { Edge, Node } from '@xyflow/react'
import { WorkflowNodeType, type WorkflowEdge, type WorkflowNode, type WorkflowNodeTypeValue } from '../types'
import type { DesignerNodeData } from './DesignerNode'
import {
  describeConfiguration,
  parseConfiguration,
  serialiseConfiguration,
  type NodeConfiguration,
} from './nodeConfiguration'
import type { SaveWorkflowEdgeInput, SaveWorkflowNodeInput } from '../api'

/** The editable graph, kept in the shape the server speaks so saving is a direct translation. */
export interface DraftNode {
  key: string
  name: string
  description: string | null
  type: WorkflowNodeTypeValue
  configuration: NodeConfiguration
  positionX: number
  positionY: number
}

export interface DraftEdge {
  id: string
  fromNodeKey: string
  toNodeKey: string
  label: string | null
  branch: string | null
}

export interface Draft {
  nodes: DraftNode[]
  edges: DraftEdge[]
}

// A node box is 14rem wide, so the column has to leave room for the box plus the edge label.
const columnWidth = 320
const rowHeight = 160

export function toDraft(nodes: WorkflowNode[], edges: WorkflowEdge[]): Draft {
  const draftNodes: DraftNode[] = nodes.map((node) => ({
    key: node.key,
    name: node.name,
    description: node.description,
    type: node.type,
    configuration: parseConfiguration(node.configJson),
    positionX: node.positionX,
    positionY: node.positionY,
  }))

  const draftEdges: DraftEdge[] = edges.map((edge) => ({
    id: edge.id,
    fromNodeKey: edge.fromNodeKey,
    toNodeKey: edge.toNodeKey,
    label: edge.label,
    branch: edge.branch,
  }))

  // A definition saved before the designer existed, or seeded by code, may have every node at the
  // origin. Laying those out once keeps the canvas readable without touching stored positions.
  const needsLayout = draftNodes.every((node) => node.positionX === 0 && node.positionY === 0)

  return { nodes: needsLayout ? layout(draftNodes, draftEdges) : draftNodes, edges: draftEdges }
}

/**
 * Places nodes in columns by their distance from the start node, and stacks siblings vertically.
 * Deliberately simple: the designer stores whatever the user drags afterwards.
 */
export function layout(nodes: DraftNode[], edges: DraftEdge[]): DraftNode[] {
  const depth = new Map<string, number>()
  const start = nodes.find((node) => node.type === WorkflowNodeType.Start) ?? nodes[0]

  if (!start) {
    return nodes
  }

  const queue: string[] = [start.key]
  depth.set(start.key, 0)

  while (queue.length > 0) {
    const current = queue.shift()!
    const currentDepth = depth.get(current) ?? 0

    edges
      .filter((edge) => edge.fromNodeKey === current)
      .forEach((edge) => {
        const known = depth.get(edge.toNodeKey)
        if (known === undefined || known < currentDepth + 1) {
          depth.set(edge.toNodeKey, currentDepth + 1)
          queue.push(edge.toNodeKey)
        }
      })
  }

  // Nodes no edge reaches still need somewhere to live: park them in a column of their own.
  const maxDepth = Math.max(0, ...depth.values())
  nodes.filter((node) => !depth.has(node.key)).forEach((node) => depth.set(node.key, maxDepth + 1))

  const perColumn = new Map<number, number>()

  return nodes.map((node) => {
    const column = depth.get(node.key) ?? 0
    const row = perColumn.get(column) ?? 0
    perColumn.set(column, row + 1)

    return { ...node, positionX: column * columnWidth, positionY: row * rowHeight }
  })
}

export function toFlowNodes(
  draft: Draft,
  issuesByNode: Map<string, { hasError: boolean; hasWarning: boolean }>,
): Node<DesignerNodeData>[] {
  return draft.nodes.map((node) => {
    const issues = issuesByNode.get(node.key)

    return {
      id: node.key,
      type: 'designer',
      position: { x: node.positionX, y: node.positionY },
      data: {
        label: node.name,
        nodeType: node.type,
        nodeKey: node.key,
        summary: describeConfiguration(node.type, node.configuration),
        hasError: issues?.hasError ?? false,
        hasWarning: issues?.hasWarning ?? false,
      },
    }
  })
}

export function toFlowEdges(draft: Draft): Edge[] {
  return draft.edges.map((edge) => ({
    id: edge.id,
    source: edge.fromNodeKey,
    target: edge.toNodeKey,
    label: edge.label ?? edge.branch ?? undefined,
    animated: false,
    labelBgPadding: [6, 3] as [number, number],
    labelBgBorderRadius: 3,
  }))
}

export function toSaveNodes(draft: Draft): SaveWorkflowNodeInput[] {
  return draft.nodes.map((node) => ({
    key: node.key,
    name: node.name.trim(),
    description: node.description?.trim() || null,
    type: node.type,
    configJson: serialiseConfiguration(node.configuration),
    positionX: Math.round(node.positionX),
    positionY: Math.round(node.positionY),
  }))
}

export function toSaveEdges(draft: Draft): SaveWorkflowEdgeInput[] {
  return draft.edges.map((edge) => ({
    fromNodeKey: edge.fromNodeKey,
    toNodeKey: edge.toNodeKey,
    label: edge.label?.trim() || null,
    branch: edge.branch?.trim() || null,
  }))
}

/**
 * Local checks the designer can answer without a round trip. The server validator remains the
 * authority: these only tell the user what is obviously missing while they draw.
 */
export function draftIssues(draft: Draft): { nodeKey: string | null; code: string }[] {
  const issues: { nodeKey: string | null; code: string }[] = []

  const startNodes = draft.nodes.filter((node) => node.type === WorkflowNodeType.Start)
  if (startNodes.length === 0) {
    issues.push({ nodeKey: null, code: 'missing_start' })
  }
  if (startNodes.length > 1) {
    issues.push({ nodeKey: null, code: 'multiple_start' })
  }
  if (!draft.nodes.some((node) => node.type === WorkflowNodeType.End)) {
    issues.push({ nodeKey: null, code: 'missing_end' })
  }

  draft.nodes.forEach((node) => {
    const outgoing = draft.edges.filter((edge) => edge.fromNodeKey === node.key)
    const incoming = draft.edges.filter((edge) => edge.toNodeKey === node.key)

    if (node.type !== WorkflowNodeType.End && outgoing.length === 0) {
      issues.push({ nodeKey: node.key, code: 'node_without_exit' })
    }

    if (node.type !== WorkflowNodeType.Start && incoming.length === 0) {
      issues.push({ nodeKey: node.key, code: 'node_unreachable' })
    }

    if (node.type === WorkflowNodeType.Condition) {
      const branches = new Set(outgoing.map((edge) => edge.branch))
      if (!branches.has('true') || !branches.has('false')) {
        issues.push({ nodeKey: node.key, code: 'condition_missing_branch' })
      }
    }
  })

  return issues
}
