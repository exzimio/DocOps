import { WorkflowNodeType, type WorkflowNodeTypeValue } from '../types'

export type AssignmentStrategy = 'User' | 'Department' | 'Role' | 'DocumentOwner' | 'DepartmentManager'

export type ConditionOperator =
  | 'Equals'
  | 'NotEquals'
  | 'GreaterThan'
  | 'GreaterThanOrEqual'
  | 'LessThan'
  | 'LessThanOrEqual'
  | 'Contains'
  | 'IsEmpty'
  | 'IsNotEmpty'

export type TaskPriority = 'Low' | 'Normal' | 'High' | 'Critical'

export type DocumentStatusName =
  | 'Draft'
  | 'InReview'
  | 'PendingApproval'
  | 'Approved'
  | 'Rejected'
  | 'SignatureRequired'
  | 'Signed'
  | 'Archived'

export interface AssignmentConfiguration {
  strategy: AssignmentStrategy
  userId?: string | null
  departmentCode?: string | null
  role?: string | null
}

export interface ConditionConfiguration {
  field: string
  operator: ConditionOperator
  value?: string | null
}

export interface NotificationConfiguration {
  title: string
  body?: string | null
  recipients?: AssignmentConfiguration | null
}

/** Mirrors the server's NodeConfiguration; every field is optional and read per node type. */
export interface NodeConfiguration {
  taskTitle?: string | null
  taskDescription?: string | null
  assignment?: AssignmentConfiguration | null
  dueInHours?: number | null
  escalateAfterHours?: number | null
  priority?: TaskPriority | null
  condition?: ConditionConfiguration | null
  notification?: NotificationConfiguration | null
  delayHours?: number | null
  documentStatus?: DocumentStatusName | null
  outcome?: string | null
  endsAsRejected?: boolean
}

export function parseConfiguration(configJson: string | null | undefined): NodeConfiguration {
  if (!configJson) {
    return {}
  }

  try {
    return JSON.parse(configJson) as NodeConfiguration
  } catch {
    // A node whose configuration cannot be read is shown empty rather than breaking the canvas;
    // saving replaces it with something valid.
    return {}
  }
}

/** Drops empty values so the stored JSON stays the minimum the node actually needs. */
export function serialiseConfiguration(configuration: NodeConfiguration): string | null {
  const cleaned: Record<string, unknown> = {}

  Object.entries(configuration).forEach(([key, value]) => {
    if (value === null || value === undefined || value === '' || value === false) {
      return
    }

    cleaned[key] = value
  })

  return Object.keys(cleaned).length === 0 ? null : JSON.stringify(cleaned)
}

export const roleOptions = ['TenantAdmin', 'Manager', 'FinanceApprover', 'Employee', 'ExternalPartner']

export const priorityOptions: TaskPriority[] = ['Low', 'Normal', 'High', 'Critical']

export const operatorOptions: ConditionOperator[] = [
  'Equals',
  'NotEquals',
  'GreaterThan',
  'GreaterThanOrEqual',
  'LessThan',
  'LessThanOrEqual',
  'Contains',
  'IsEmpty',
  'IsNotEmpty',
]

export const documentStatusOptions: DocumentStatusName[] = [
  'InReview',
  'PendingApproval',
  'Approved',
  'Rejected',
  'SignatureRequired',
  'Signed',
  'Archived',
]

/** A one-line description of what a node does, shown under its name on the canvas. */
export function describeConfiguration(
  type: WorkflowNodeTypeValue,
  configuration: NodeConfiguration,
): string | undefined {
  switch (type) {
    case WorkflowNodeType.HumanTask:
    case WorkflowNodeType.Approval: {
      const assignment = configuration.assignment
      const who =
        assignment?.strategy === 'Role'
          ? assignment.role
          : assignment?.strategy === 'Department'
            ? assignment.departmentCode
            : assignment?.strategy === 'DocumentOwner'
              ? 'proprietário'
              : assignment?.strategy === 'DepartmentManager'
                ? 'responsável do departamento'
                : assignment?.strategy === 'User'
                  ? 'utilizador'
                  : undefined

      const due = configuration.dueInHours ? `${configuration.dueInHours} h` : undefined

      return [who, due].filter(Boolean).join(' · ') || undefined
    }

    case WorkflowNodeType.Condition:
      return configuration.condition
        ? `${configuration.condition.field} ${configuration.condition.operator} ${configuration.condition.value ?? ''}`.trim()
        : undefined

    case WorkflowNodeType.Notification:
      return configuration.notification?.title ?? undefined

    case WorkflowNodeType.Delay:
      return configuration.delayHours ? `${configuration.delayHours} h` : undefined

    case WorkflowNodeType.End:
      return configuration.outcome ?? undefined

    default:
      return undefined
  }
}
