import { WorkflowNodeType } from '../types'

/**
 * Branch names the engine understands, per node type. Anything else would never be matched when
 * the engine resolves the outgoing edges of a step, so the designer only offers these.
 */
export function branchOptions(type: number | undefined): string[] {
  switch (type) {
    case WorkflowNodeType.Condition:
      return ['true', 'false']
    case WorkflowNodeType.Approval:
    case WorkflowNodeType.HumanTask:
      return ['approved', 'rejected']
    // An AI step continues on its unconditional exit, or on "failed" when there was nothing to read.
    case WorkflowNodeType.AiClassification:
    case WorkflowNodeType.AiExtraction:
      return ['failed']
    default:
      return []
  }
}
