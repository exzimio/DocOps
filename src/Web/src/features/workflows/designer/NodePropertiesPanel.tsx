import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Trash2 } from 'lucide-react'
import { WorkflowNodeType, type WorkflowNodeTypeValue } from '../types'
import {
  documentStatusOptions,
  integrationKindOptions,
  operatorOptions,
  priorityOptions,
  roleOptions,
  type AssignmentConfiguration,
  type AssignmentStrategy,
  type ConditionOperator,
  type DocumentStatusName,
  type IntegrationKindValue,
  type NodeConfiguration,
  type TaskPriority,
} from './nodeConfiguration'
import { isHumanNode } from './nodeCatalogue'
import { Button } from '@/components/ui/Button'
import { TextField } from '@/components/ui/Field'
import type { Department } from '@/lib/types'

export interface SelectedNode {
  id: string
  key: string
  name: string
  description: string | null
  type: WorkflowNodeTypeValue
  configuration: NodeConfiguration
}

interface NodePropertiesPanelProps {
  node: SelectedNode
  departments: Department[]
  readOnly: boolean
  onChange: (node: SelectedNode) => void
  onDelete: () => void
}

/**
 * Edits one node. Which fields appear is decided by the node type — the same rule the engine and the
 * validator use — so the panel can never offer a setting the node would ignore.
 */
export function NodePropertiesPanel({
  node,
  departments,
  readOnly,
  onChange,
  onDelete,
}: NodePropertiesPanelProps) {
  const { t } = useTranslation()

  const update = (changes: Partial<SelectedNode>) => onChange({ ...node, ...changes })

  const updateConfig = (changes: Partial<NodeConfiguration>) =>
    update({ configuration: { ...node.configuration, ...changes } })

  const updateAssignment = (changes: Partial<AssignmentConfiguration>) =>
    updateConfig({
      assignment: {
        strategy: node.configuration.assignment?.strategy ?? 'Role',
        ...node.configuration.assignment,
        ...changes,
      },
    })

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-start justify-between gap-2">
        <div>
          <p className="text-sm font-semibold text-ink">{t(`workflows.nodeTypes.${node.type}`)}</p>
          <code className="font-mono text-[11px] text-ink-muted">{node.key}</code>
        </div>

        {!readOnly && node.type !== WorkflowNodeType.Start && (
          <Button
            variant="ghost"
            size="sm"
            onClick={onDelete}
            aria-label={t('workflows.designer.deleteNode')}
          >
            <Trash2 aria-hidden className="size-4" />
          </Button>
        )}
      </div>

      <TextField
        label={t('workflows.designer.nodeName')}
        value={node.name}
        disabled={readOnly}
        onChange={(event) => update({ name: event.target.value })}
      />

      <Field label={t('workflows.designer.nodeDescription')}>
        <textarea
          rows={2}
          value={node.description ?? ''}
          disabled={readOnly}
          onChange={(event) => update({ description: event.target.value })}
          className="rounded-md border border-strong bg-raised p-2 text-sm text-ink disabled:opacity-60"
        />
      </Field>

      {isHumanNode(node.type) && (
        <>
          <TextField
            label={t('workflows.designer.taskTitle')}
            value={node.configuration.taskTitle ?? ''}
            disabled={readOnly}
            onChange={(event) => updateConfig({ taskTitle: event.target.value })}
          />

          <Field label={t('workflows.designer.assignTo')}>
            <select
              value={node.configuration.assignment?.strategy ?? 'Role'}
              disabled={readOnly}
              onChange={(event) =>
                updateAssignment({
                  strategy: event.target.value as AssignmentStrategy,
                  role: null,
                  departmentCode: null,
                  userId: null,
                })
              }
              className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink disabled:opacity-60"
            >
              {(['Role', 'Department', 'DocumentOwner', 'DepartmentManager'] as AssignmentStrategy[]).map(
                (strategy) => (
                  <option key={strategy} value={strategy}>
                    {t(`workflows.designer.assignment.${strategy}`)}
                  </option>
                ),
              )}
            </select>
          </Field>

          {node.configuration.assignment?.strategy === 'Role' && (
            <Field label={t('workflows.designer.role')}>
              <select
                value={node.configuration.assignment.role ?? ''}
                disabled={readOnly}
                onChange={(event) => updateAssignment({ role: event.target.value })}
                className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink disabled:opacity-60"
              >
                <option value="">-</option>
                {roleOptions.map((role) => (
                  <option key={role} value={role}>
                    {role}
                  </option>
                ))}
              </select>
            </Field>
          )}

          {node.configuration.assignment?.strategy === 'Department' && (
            <Field label={t('workflows.designer.department')}>
              <select
                value={node.configuration.assignment.departmentCode ?? ''}
                disabled={readOnly}
                onChange={(event) => updateAssignment({ departmentCode: event.target.value })}
                className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink disabled:opacity-60"
              >
                <option value="">-</option>
                {departments.map((department) => (
                  <option key={department.id} value={department.code}>
                    {department.name} ({department.code})
                  </option>
                ))}
              </select>
            </Field>
          )}

          <div className="grid grid-cols-2 gap-3">
            <NumberField
              label={t('workflows.designer.dueInHours')}
              value={node.configuration.dueInHours}
              disabled={readOnly}
              onChange={(value) => updateConfig({ dueInHours: value })}
            />
            <NumberField
              label={t('workflows.designer.escalateAfterHours')}
              value={node.configuration.escalateAfterHours}
              disabled={readOnly}
              onChange={(value) => updateConfig({ escalateAfterHours: value })}
            />
          </div>

          <Field label={t('tasks.priority')}>
            <select
              value={node.configuration.priority ?? 'Normal'}
              disabled={readOnly}
              onChange={(event) => updateConfig({ priority: event.target.value as TaskPriority })}
              className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink disabled:opacity-60"
            >
              {priorityOptions.map((priority) => (
                <option key={priority} value={priority}>
                  {priority}
                </option>
              ))}
            </select>
          </Field>
        </>
      )}

      {node.type === WorkflowNodeType.Condition && (
        <>
          <TextField
            label={t('workflows.designer.conditionField')}
            value={node.configuration.condition?.field ?? ''}
            disabled={readOnly}
            hint={t('workflows.designer.conditionFieldHint')}
            onChange={(event) =>
              updateConfig({
                condition: {
                  operator: node.configuration.condition?.operator ?? 'Equals',
                  value: node.configuration.condition?.value ?? '',
                  field: event.target.value,
                },
              })
            }
          />

          <Field label={t('workflows.designer.conditionOperator')}>
            <select
              value={node.configuration.condition?.operator ?? 'Equals'}
              disabled={readOnly}
              onChange={(event) =>
                updateConfig({
                  condition: {
                    field: node.configuration.condition?.field ?? '',
                    value: node.configuration.condition?.value ?? '',
                    operator: event.target.value as ConditionOperator,
                  },
                })
              }
              className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink disabled:opacity-60"
            >
              {operatorOptions.map((operator) => (
                <option key={operator} value={operator}>
                  {t(`workflows.designer.operators.${operator}`)}
                </option>
              ))}
            </select>
          </Field>

          <TextField
            label={t('workflows.designer.conditionValue')}
            value={node.configuration.condition?.value ?? ''}
            disabled={readOnly}
            onChange={(event) =>
              updateConfig({
                condition: {
                  field: node.configuration.condition?.field ?? '',
                  operator: node.configuration.condition?.operator ?? 'Equals',
                  value: event.target.value,
                },
              })
            }
          />
        </>
      )}

      {node.type === WorkflowNodeType.Notification && (
        <>
          <TextField
            label={t('workflows.designer.notificationTitle')}
            value={node.configuration.notification?.title ?? ''}
            disabled={readOnly}
            onChange={(event) =>
              updateConfig({
                notification: {
                  ...node.configuration.notification,
                  title: event.target.value,
                },
              })
            }
          />

          <Field label={t('workflows.designer.notificationBody')}>
            <textarea
              rows={2}
              value={node.configuration.notification?.body ?? ''}
              disabled={readOnly}
              onChange={(event) =>
                updateConfig({
                  notification: {
                    title: node.configuration.notification?.title ?? '',
                    ...node.configuration.notification,
                    body: event.target.value,
                  },
                })
              }
              className="rounded-md border border-strong bg-raised p-2 text-sm text-ink disabled:opacity-60"
            />
          </Field>

          <Field label={t('workflows.designer.notificationRecipients')}>
            <select
              value={node.configuration.notification?.recipients?.strategy ?? 'DocumentOwner'}
              disabled={readOnly}
              onChange={(event) =>
                updateConfig({
                  notification: {
                    title: node.configuration.notification?.title ?? '',
                    body: node.configuration.notification?.body,
                    recipients: { strategy: event.target.value as AssignmentStrategy },
                  },
                })
              }
              className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink disabled:opacity-60"
            >
              {(['DocumentOwner', 'Department', 'Role', 'DepartmentManager'] as AssignmentStrategy[]).map(
                (strategy) => (
                  <option key={strategy} value={strategy}>
                    {t(`workflows.designer.assignment.${strategy}`)}
                  </option>
                ),
              )}
            </select>
          </Field>

          {node.configuration.notification?.recipients?.strategy === 'Department' && (
            <Field label={t('workflows.designer.department')}>
              <select
                value={node.configuration.notification.recipients.departmentCode ?? ''}
                disabled={readOnly}
                onChange={(event) =>
                  updateConfig({
                    notification: {
                      title: node.configuration.notification?.title ?? '',
                      body: node.configuration.notification?.body,
                      recipients: { strategy: 'Department', departmentCode: event.target.value },
                    },
                  })
                }
                className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink disabled:opacity-60"
              >
                <option value="">-</option>
                {departments.map((department) => (
                  <option key={department.id} value={department.code}>
                    {department.name} ({department.code})
                  </option>
                ))}
              </select>
            </Field>
          )}
        </>
      )}

      {(node.type === WorkflowNodeType.AiClassification || node.type === WorkflowNodeType.AiExtraction) && (
        <div className="flex flex-col gap-2 rounded-md border border-subtle bg-sunken p-3 text-xs text-ink-secondary">
          <p>
            {node.type === WorkflowNodeType.AiClassification
              ? t('workflows.designer.aiClassificationHelp')
              : t('workflows.designer.aiExtractionHelp')}
          </p>
          <p>{t('workflows.designer.aiNeverApplies')}</p>
          <p className="font-mono text-[11px] text-ink-muted">
            {node.type === WorkflowNodeType.AiClassification
              ? 'ai.status · ai.suggestedType · ai.classificationConfidence'
              : 'ai.status · ai.fieldsFound · ai.requiredMissing · ai.<campo>'}
          </p>
        </div>
      )}

      {node.type === WorkflowNodeType.Integration && (
        <Field
          label={t('workflows.designer.integrationKind')}
          hint={t('workflows.designer.integrationKindHint')}
        >
          <select
            value={node.configuration.integrationKind ?? ''}
            disabled={readOnly}
            onChange={(event) =>
              updateConfig({
                integrationKind:
                  event.target.value === '' ? null : (Number(event.target.value) as IntegrationKindValue),
              })
            }
            className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink disabled:opacity-60"
          >
            <option value="">-</option>
            {integrationKindOptions.map((kind) => (
              <option key={kind} value={kind}>
                {t(`integrations.kindValues.${kind}`)}
              </option>
            ))}
          </select>
        </Field>
      )}

      {node.type === WorkflowNodeType.Delay && (
        <NumberField
          label={t('workflows.designer.delayHours')}
          value={node.configuration.delayHours}
          disabled={readOnly}
          onChange={(value) => updateConfig({ delayHours: value })}
        />
      )}

      {node.type === WorkflowNodeType.End && (
        <>
          <TextField
            label={t('workflows.outcome')}
            value={node.configuration.outcome ?? ''}
            disabled={readOnly}
            onChange={(event) => updateConfig({ outcome: event.target.value })}
          />

          <label className="flex items-center gap-2 text-xs text-ink-secondary">
            <input
              type="checkbox"
              checked={node.configuration.endsAsRejected ?? false}
              disabled={readOnly}
              onChange={(event) => updateConfig({ endsAsRejected: event.target.checked })}
              className="size-4 accent-teal-700"
            />
            {t('workflows.designer.endsAsRejected')}
          </label>
        </>
      )}

      {node.type !== WorkflowNodeType.Start && (
        <Field
          label={t('workflows.designer.documentStatus')}
          hint={t('workflows.designer.documentStatusHint')}
        >
          <select
            value={node.configuration.documentStatus ?? ''}
            disabled={readOnly}
            onChange={(event) =>
              updateConfig({ documentStatus: (event.target.value || null) as DocumentStatusName | null })
            }
            className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink disabled:opacity-60"
          >
            <option value="">-</option>
            {documentStatusOptions.map((status) => (
              <option key={status} value={status}>
                {status}
              </option>
            ))}
          </select>
        </Field>
      )}
    </div>
  )
}

function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className="text-xs font-medium text-ink-secondary">{label}</span>
      {children}
      {hint && <span className="text-[11px] text-ink-muted">{hint}</span>}
    </label>
  )
}

function NumberField({
  label,
  value,
  disabled,
  onChange,
}: {
  label: string
  value: number | null | undefined
  disabled: boolean
  onChange: (value: number | null) => void
}) {
  return (
    <Field label={label}>
      <input
        type="number"
        min={1}
        value={value ?? ''}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value === '' ? null : Number(event.target.value))}
        className="h-9 rounded-md border border-strong bg-raised px-2 text-sm text-ink disabled:opacity-60"
      />
    </Field>
  )
}
