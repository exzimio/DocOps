import {
  Bell,
  Bot,
  CircleCheckBig,
  Flag,
  GitBranch,
  GitMerge,
  Merge,
  Play,
  Plug,
  ScanText,
  Split,
  Timer,
  UserCheck,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { WorkflowNodeType, type WorkflowNodeTypeValue } from '../types'

export interface NodeKind {
  type: WorkflowNodeTypeValue
  icon: LucideIcon
  /** Border and accent colour on the canvas. */
  accent: string
  /** Node types the engine cannot execute yet: usable on the canvas, but they block activation. */
  executable: boolean
  /** Only one start node is allowed per definition. */
  unique?: boolean
}

export const nodeCatalogue: NodeKind[] = [
  { type: WorkflowNodeType.Start, icon: Play, accent: 'teal', executable: true, unique: true },
  { type: WorkflowNodeType.HumanTask, icon: UserCheck, accent: 'graphite', executable: true },
  { type: WorkflowNodeType.Approval, icon: CircleCheckBig, accent: 'amber', executable: true },
  { type: WorkflowNodeType.Condition, icon: GitBranch, accent: 'teal', executable: true },
  { type: WorkflowNodeType.Notification, icon: Bell, accent: 'graphite', executable: true },
  { type: WorkflowNodeType.ParallelBranch, icon: Split, accent: 'graphite', executable: true },
  { type: WorkflowNodeType.Join, icon: Merge, accent: 'graphite', executable: true },
  { type: WorkflowNodeType.Delay, icon: Timer, accent: 'graphite', executable: true },
  { type: WorkflowNodeType.End, icon: Flag, accent: 'graphite', executable: true },
  { type: WorkflowNodeType.AiClassification, icon: Bot, accent: 'teal', executable: true },
  { type: WorkflowNodeType.AiExtraction, icon: ScanText, accent: 'teal', executable: true },
  { type: WorkflowNodeType.Integration, icon: Plug, accent: 'red', executable: true },
]

export function nodeKind(type: WorkflowNodeTypeValue): NodeKind {
  return nodeCatalogue.find((kind) => kind.type === type) ?? nodeCatalogue[1]!
}

export const conditionIcon = GitMerge

/** Node types that produce a human task, and therefore need an assignee and a deadline. */
export function isHumanNode(type: WorkflowNodeTypeValue): boolean {
  return type === WorkflowNodeType.HumanTask || type === WorkflowNodeType.Approval
}

/** Builds an unused node key from a label, matching the server's accepted format. */
export function buildNodeKey(label: string, existing: string[]): string {
  const base =
    label
      .normalize('NFD')
      .replace(/[̀-ͯ]/g, '')
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .slice(0, 40) || 'no'

  if (!existing.includes(base)) {
    return base
  }

  for (let suffix = 2; suffix < 1000; suffix += 1) {
    const candidate = `${base}-${suffix}`
    if (!existing.includes(candidate)) {
      return candidate
    }
  }

  return `${base}-${Date.now()}`
}
