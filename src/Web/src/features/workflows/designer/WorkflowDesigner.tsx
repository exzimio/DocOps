import { useCallback, useMemo, useState } from 'react'
import {
  Background,
  BackgroundVariant,
  Controls,
  ReactFlow,
  type Connection,
  type Edge,
  type EdgeChange,
  type Node,
  type NodeChange,
} from '@xyflow/react'
import { useTranslation } from 'react-i18next'
import '@xyflow/react/dist/style.css'
import { DesignerNode, type DesignerNodeData } from './DesignerNode'
import { nodeCatalogue } from './nodeCatalogue'
import type { Draft, DraftEdge, DraftNode } from './graph'
import { layout, toFlowEdges, toFlowNodes } from './graph'
import type { WorkflowNodeTypeValue } from '../types'
import { Button } from '@/components/ui/Button'

const nodeTypes = { designer: DesignerNode }

export interface DesignerSelection {
  kind: 'node' | 'edge'
  id: string
}

interface WorkflowDesignerProps {
  draft: Draft
  issuesByNode: Map<string, { hasError: boolean; hasWarning: boolean }>
  readOnly: boolean
  selection: DesignerSelection | null
  onSelectionChange: (selection: DesignerSelection | null) => void
  onDraftChange: (draft: Draft) => void
  onAddNode: (type: WorkflowNodeTypeValue) => void
}

/**
 * The canvas. It owns no workflow state: every change is handed back as a new draft, so the page
 * that saves is also the page that knows what is unsaved.
 */
export function WorkflowDesigner({
  draft,
  issuesByNode,
  readOnly,
  selection,
  onSelectionChange,
  onDraftChange,
  onAddNode,
}: WorkflowDesignerProps) {
  const { t } = useTranslation()
  const [paletteOpen, setPaletteOpen] = useState(true)

  const flowNodes = useMemo(
    () =>
      toFlowNodes(draft, issuesByNode).map((node) => ({
        ...node,
        selected: selection?.kind === 'node' && selection.id === node.id,
        draggable: !readOnly,
      })),
    [draft, issuesByNode, readOnly, selection],
  )

  const flowEdges = useMemo(
    () =>
      toFlowEdges(draft).map((edge) => ({
        ...edge,
        selected: selection?.kind === 'edge' && selection.id === edge.id,
      })),
    [draft, selection],
  )

  const onNodesChange = useCallback(
    (changes: NodeChange<Node<DesignerNodeData>>[]) => {
      const positions = new Map<string, { x: number; y: number }>()
      const removed = new Set<string>()

      changes.forEach((change) => {
        if (change.type === 'position' && change.position) {
          positions.set(change.id, change.position)
        }
        if (change.type === 'remove') {
          removed.add(change.id)
        }
      })

      if (positions.size === 0 && removed.size === 0) {
        return
      }

      const nodes: DraftNode[] = draft.nodes
        .filter((node) => !removed.has(node.key))
        .map((node) => {
          const position = positions.get(node.key)

          return position ? { ...node, positionX: position.x, positionY: position.y } : node
        })

      // A removed node takes its connections with it: leaving them would be a graph the server rejects.
      const edges = draft.edges.filter(
        (edge) => !removed.has(edge.fromNodeKey) && !removed.has(edge.toNodeKey),
      )

      onDraftChange({ nodes, edges })
    },
    [draft, onDraftChange],
  )

  const onEdgesChange = useCallback(
    (changes: EdgeChange<Edge>[]) => {
      const removed = new Set(changes.filter((change) => change.type === 'remove').map((c) => c.id))

      if (removed.size === 0) {
        return
      }

      onDraftChange({ ...draft, edges: draft.edges.filter((edge) => !removed.has(edge.id)) })
    },
    [draft, onDraftChange],
  )

  const onConnect = useCallback(
    (connection: Connection) => {
      if (readOnly || !connection.source || !connection.target || connection.source === connection.target) {
        return
      }

      const exists = draft.edges.some(
        (edge) => edge.fromNodeKey === connection.source && edge.toNodeKey === connection.target,
      )

      if (exists) {
        return
      }

      const edge: DraftEdge = {
        id: `new-${connection.source}-${connection.target}-${Date.now()}`,
        fromNodeKey: connection.source,
        toNodeKey: connection.target,
        label: null,
        branch: null,
      }

      onDraftChange({ ...draft, edges: [...draft.edges, edge] })
      onSelectionChange({ kind: 'edge', id: edge.id })
    },
    [draft, onDraftChange, onSelectionChange, readOnly],
  )

  return (
    <div className="relative h-[28rem] overflow-hidden rounded-lg border border-subtle bg-sunken lg:h-[34rem]">
      {!readOnly && (
        <div className="absolute left-3 top-3 z-10 flex max-w-[15rem] flex-col gap-1.5 rounded-md border border-subtle bg-raised/95 p-2 shadow-card backdrop-blur">
          <button
            type="button"
            onClick={() => setPaletteOpen((open) => !open)}
            aria-expanded={paletteOpen}
            className="text-left text-[11px] font-semibold uppercase tracking-wide text-ink-muted hover:text-ink"
          >
            {t('workflows.designer.palette')}
          </button>

          {paletteOpen && (
            <div className="flex flex-wrap gap-1">
              {nodeCatalogue
                .filter((kind) => !kind.unique)
                .map((kind) => (
                  <button
                    key={kind.type}
                    type="button"
                    onClick={() => onAddNode(kind.type)}
                    className="inline-flex items-center gap-1 rounded-sm border border-subtle bg-sunken px-1.5 py-1 text-[11px] text-ink-secondary hover:border-teal-500 hover:text-ink"
                  >
                    <kind.icon aria-hidden className="size-3.5" />
                    {t(`workflows.nodeTypes.${kind.type}`)}
                  </button>
                ))}
            </div>
          )}
        </div>
      )}

      <div className="absolute right-3 top-3 z-10">
        <Button
          variant="secondary"
          size="sm"
          disabled={readOnly}
          onClick={() => onDraftChange({ ...draft, nodes: layout(draft.nodes, draft.edges) })}
        >
          {t('workflows.designer.autoLayout')}
        </Button>
      </div>

      <ReactFlow
        nodes={flowNodes}
        edges={flowEdges}
        nodeTypes={nodeTypes}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onConnect={onConnect}
        onNodeClick={(_, node) => onSelectionChange({ kind: 'node', id: node.id })}
        onEdgeClick={(_, edge) => onSelectionChange({ kind: 'edge', id: edge.id })}
        onPaneClick={() => onSelectionChange(null)}
        nodesConnectable={!readOnly}
        nodesDraggable={!readOnly}
        elementsSelectable
        deleteKeyCode={readOnly ? null : ['Backspace', 'Delete']}
        fitView
        // Open on the whole process rather than a legible fragment of it; the controls zoom in.
        fitViewOptions={{ padding: 0.1, maxZoom: 1 }}
        minZoom={0.3}
        proOptions={{ hideAttribution: false }}
        aria-label={t('workflows.designer.canvas')}
      >
        <Background variant={BackgroundVariant.Dots} gap={16} size={1} />
        <Controls showInteractive={false} />
      </ReactFlow>
    </div>
  )
}
