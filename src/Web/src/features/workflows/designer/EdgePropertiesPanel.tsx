import { useTranslation } from 'react-i18next'
import { Trash2 } from 'lucide-react'
import type { DraftEdge, DraftNode } from './graph'
import { branchOptions } from './branches'
import { Button } from '@/components/ui/Button'
import { TextField } from '@/components/ui/Field'

interface EdgePropertiesPanelProps {
  edge: DraftEdge
  nodes: DraftNode[]
  readOnly: boolean
  onChange: (edge: DraftEdge) => void
  onDelete: () => void
}

/**
 * A connection carries a branch only when it leaves a decision point. Offering a branch on any
 * other node would let the designer produce a graph the engine silently ignores.
 */
export function EdgePropertiesPanel({ edge, nodes, readOnly, onChange, onDelete }: EdgePropertiesPanelProps) {
  const { t } = useTranslation()

  const source = nodes.find((node) => node.key === edge.fromNodeKey)
  const branches = branchOptions(source?.type)

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-start justify-between gap-2">
        <div>
          <p className="text-sm font-semibold text-ink">{t('workflows.designer.connection')}</p>
          <code className="font-mono text-[11px] text-ink-muted">
            {edge.fromNodeKey} → {edge.toNodeKey}
          </code>
        </div>

        {!readOnly && (
          <Button
            variant="ghost"
            size="sm"
            onClick={onDelete}
            aria-label={t('workflows.designer.deleteConnection')}
          >
            <Trash2 aria-hidden className="size-4" />
          </Button>
        )}
      </div>

      {branches.length > 0 ? (
        <label className="flex flex-col gap-1.5">
          <span className="text-xs font-medium text-ink-secondary">{t('workflows.designer.branch')}</span>
          <select
            value={edge.branch ?? ''}
            disabled={readOnly}
            onChange={(event) => onChange({ ...edge, branch: event.target.value || null })}
            className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink disabled:opacity-60"
          >
            <option value="">-</option>
            {branches.map((branch) => (
              <option key={branch} value={branch}>
                {t(`workflows.designer.branches.${branch}`)}
              </option>
            ))}
          </select>
          <span className="text-[11px] text-ink-muted">{t('workflows.designer.branchHint')}</span>
        </label>
      ) : (
        <p className="text-[11px] text-ink-muted">{t('workflows.designer.branchNotApplicable')}</p>
      )}

      <TextField
        label={t('workflows.designer.connectionLabel')}
        value={edge.label ?? ''}
        disabled={readOnly}
        onChange={(event) => onChange({ ...edge, label: event.target.value })}
      />
    </div>
  )
}
