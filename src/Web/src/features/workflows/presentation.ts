import { WorkflowInstanceStatus, WorkflowTaskStatus } from './types'

export type Tone = 'neutral' | 'teal' | 'amber' | 'red' | 'green'

export function instanceTone(status: number): Tone {
  switch (status) {
    case WorkflowInstanceStatus.Completed:
      return 'green'
    case WorkflowInstanceStatus.Rejected:
    case WorkflowInstanceStatus.Failed:
      return 'red'
    case WorkflowInstanceStatus.Waiting:
      return 'amber'
    case WorkflowInstanceStatus.Running:
      return 'teal'
    default:
      return 'neutral'
  }
}

export function taskTone(status: number, isOverdue: boolean): Tone {
  switch (status) {
    case WorkflowTaskStatus.Completed:
      return 'green'
    case WorkflowTaskStatus.Rejected:
      return 'red'
    case WorkflowTaskStatus.Cancelled:
      return 'neutral'
    default:
      return isOverdue ? 'amber' : status === WorkflowTaskStatus.InProgress ? 'teal' : 'neutral'
  }
}
