export const WorkflowNodeType = {
  Start: 0,
  HumanTask: 1,
  Approval: 2,
  Condition: 3,
  Notification: 4,
  ParallelBranch: 5,
  Join: 6,
  Delay: 7,
  End: 8,
  AiClassification: 9,
  AiExtraction: 10,
  Integration: 11,
} as const

export type WorkflowNodeTypeValue = (typeof WorkflowNodeType)[keyof typeof WorkflowNodeType]

export const WorkflowInstanceStatus = {
  Running: 0,
  Waiting: 1,
  Completed: 2,
  Rejected: 3,
  Cancelled: 4,
  Failed: 5,
} as const

export type WorkflowInstanceStatusValue = (typeof WorkflowInstanceStatus)[keyof typeof WorkflowInstanceStatus]

export const WorkflowTaskStatus = {
  Pending: 0,
  InProgress: 1,
  Completed: 2,
  Rejected: 3,
  Cancelled: 4,
} as const

export type WorkflowTaskStatusValue = (typeof WorkflowTaskStatus)[keyof typeof WorkflowTaskStatus]

export const WorkflowTaskPriority = {
  Low: 0,
  Normal: 1,
  High: 2,
  Critical: 3,
} as const

export type WorkflowTaskPriorityValue = (typeof WorkflowTaskPriority)[keyof typeof WorkflowTaskPriority]

export const WorkflowTaskDecision = {
  Approved: 0,
  Rejected: 1,
  InformationRequested: 2,
  Completed: 3,
} as const

export type TaskAction = 'approve' | 'reject' | 'complete' | 'request-info' | 'reassign' | 'claim'

export type TaskScope = 'Mine' | 'All' | 'Delegated'

export interface WorkflowNode {
  id: string
  key: string
  name: string
  description: string | null
  type: WorkflowNodeTypeValue
  configJson: string | null
  positionX: number
  positionY: number
}

export interface WorkflowEdge {
  id: string
  fromNodeKey: string
  toNodeKey: string
  label: string | null
  branch: string | null
}

export interface WorkflowDefinitionSummary {
  id: string
  code: string
  name: string
  description: string | null
  version: number
  isActive: boolean
  trigger: number
  documentTypeId: string | null
  documentTypeName: string | null
  nodeCount: number
  runningInstances: number
  completedInstances: number
  lastStartedAtUtc: string | null
  /** False for a superseded version kept only so its running instances still resolve their nodes. */
  isLatestVersion: boolean
  /** True while the definition has never been used, which is when it can be edited in place. */
  isEditableInPlace: boolean
}

export interface WorkflowValidationIssue {
  severity: string
  code: string
  message: string
  nodeKey: string | null
}

export interface WorkflowDefinitionDetail {
  summary: WorkflowDefinitionSummary
  documentTypeId: string | null
  nodes: WorkflowNode[]
  edges: WorkflowEdge[]
  concurrencyToken: string
  validationIssues: WorkflowValidationIssue[]
}

export interface WorkflowInstanceListItem {
  id: string
  reference: string
  definitionId: string
  definitionName: string
  status: WorkflowInstanceStatusValue
  documentId: string | null
  documentReference: string | null
  documentTitle: string | null
  startedAtUtc: string
  completedAtUtc: string | null
  startedByName: string | null
  openTaskCount: number
  overdueTaskCount: number
  currentStepName: string | null
  outcome: string | null
  elapsedHours: number
}

export interface WorkflowEvent {
  id: string
  occurredAtUtc: string
  eventType: string
  title: string
  detail: string | null
  nodeKey: string | null
  actorDisplayName: string
}

export interface TaskListItem {
  id: string
  instanceId: string
  instanceReference: string
  definitionName: string
  nodeKey: string
  title: string
  description: string | null
  status: WorkflowTaskStatusValue
  priority: WorkflowTaskPriorityValue
  requiresDecision: boolean
  assigneeDisplayName: string
  assignedToUserId: string | null
  assignedToDepartmentId: string | null
  assignedToRole: string | null
  documentId: string | null
  documentReference: string | null
  documentTitle: string | null
  assignedAtUtc: string
  dueAtUtc: string | null
  completedAtUtc: string | null
  completedByName: string | null
  decision: number | null
  decisionComment: string | null
  isOverdue: boolean
  escalatedAtUtc: string | null
  slaRemainingFraction: number | null
  availableActions: TaskAction[]
}

export interface WorkflowInstanceDetail {
  summary: WorkflowInstanceListItem
  statusReason: string | null
  resumeAtUtc: string | null
  activeNodeKeys: string[]
  variables: Record<string, string | null>
  timeline: WorkflowEvent[]
  tasks: TaskListItem[]
  nodes: WorkflowNode[]
  edges: WorkflowEdge[]
  canCancel: boolean
}

export interface TaskDistribution {
  label: string
  open: number
  overdue: number
}

export interface TaskStatistics {
  pending: number
  inProgress: number
  overdue: number
  completedLast30Days: number
  unassigned: number
  byAssignee: TaskDistribution[]
  byPriority: TaskDistribution[]
}

export interface Notification {
  id: string
  kind: string
  title: string
  body: string | null
  link: string | null
  isRead: boolean
  createdAtUtc: string
}

export interface TaskFilters {
  scope: TaskScope
  statuses: WorkflowTaskStatusValue[]
  onlyOverdue: boolean
  page: number
  pageSize: number
}

export const defaultTaskFilters: TaskFilters = {
  scope: 'Mine',
  statuses: [WorkflowTaskStatus.Pending, WorkflowTaskStatus.InProgress],
  onlyOverdue: false,
  page: 1,
  pageSize: 20,
}
