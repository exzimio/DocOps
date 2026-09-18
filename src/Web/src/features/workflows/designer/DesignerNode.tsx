import { memo } from 'react'
import { Handle, Position, type NodeProps } from '@xyflow/react'
import { useTranslation } from 'react-i18next'
import { TriangleAlert } from 'lucide-react'
import clsx from 'clsx'
import { WorkflowNodeType, type WorkflowNodeTypeValue } from '../types'
import { nodeKind } from './nodeCatalogue'

export interface DesignerNodeData extends Record<string, unknown> {
  label: string
  nodeType: WorkflowNodeTypeValue
  nodeKey: string
  summary?: string
  hasError: boolean
  hasWarning: boolean
}

const accentClasses: Record<string, string> = {
  teal: 'border-teal-500 bg-teal-50 dark:bg-teal-950',
  amber: 'border-amber-500 bg-amber-50 dark:bg-amber-900/40',
  red: 'border-red-500 bg-red-50 dark:bg-red-950',
  graphite: 'border-strong bg-raised',
}

/**
 * One box on the canvas. Start nodes have no input and end nodes have no output, so the canvas
 * cannot be used to draw a graph the engine would refuse to run.
 */
export const DesignerNode = memo(function DesignerNode({ data, selected }: NodeProps) {
  const { t } = useTranslation()
  const nodeData = data as DesignerNodeData
  const kind = nodeKind(nodeData.nodeType)
  const Icon = kind.icon

  const isStart = nodeData.nodeType === WorkflowNodeType.Start
  const isEnd = nodeData.nodeType === WorkflowNodeType.End

  return (
    <div
      className={clsx(
        'w-56 rounded-md border-2 px-3 py-2 shadow-card transition-shadow',
        accentClasses[kind.accent] ?? accentClasses.graphite,
        selected && 'ring-2 ring-teal-500 ring-offset-1',
      )}
    >
      {!isStart && (
        <Handle
          type="target"
          position={Position.Left}
          className="!size-2.5 !border-2 !border-raised !bg-graphite-500"
        />
      )}

      <div className="flex items-center gap-2">
        <Icon aria-hidden className="size-4 shrink-0 text-ink-secondary" />
        <span className="truncate text-sm font-medium text-ink">{nodeData.label}</span>
        {(nodeData.hasError || nodeData.hasWarning) && (
          <TriangleAlert
            aria-hidden
            className={clsx(
              'ml-auto size-3.5 shrink-0',
              nodeData.hasError ? 'text-red-600' : 'text-amber-600',
            )}
          />
        )}
      </div>

      <p className="mt-0.5 truncate text-[11px] text-ink-muted">
        {t(`workflows.nodeTypes.${nodeData.nodeType}`)}
        {!kind.executable && ` · ${t('workflows.designer.notExecutable')}`}
      </p>

      {nodeData.summary && (
        <p className="mt-1 truncate text-[11px] text-ink-secondary" title={nodeData.summary}>
          {nodeData.summary}
        </p>
      )}

      {!isEnd && (
        <Handle
          type="source"
          position={Position.Right}
          className="!size-2.5 !border-2 !border-raised !bg-graphite-500"
        />
      )}
    </div>
  )
})
