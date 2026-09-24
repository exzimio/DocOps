export const MetricUnit = {
  Count: 0,
  Percent: 1,
  Hours: 2,
} as const

export type MetricUnitValue = (typeof MetricUnit)[keyof typeof MetricUnit]

export interface Metric {
  key: string
  /** Null quando não houve nada a medir — não é zero, e a diferença importa. */
  value: number | null
  previous: number | null
  unit: MetricUnitValue
}

export interface SeriesPoint {
  date: string
  value: number
}

export interface Series {
  key: string
  unit: MetricUnitValue
  points: SeriesPoint[]
}

export interface Breakdown {
  label: string
  value: number
}

export interface Bottleneck {
  workflowName: string
  stepName: string
  openTasks: number
  overdue: number
  longestWaitHours: number
}

export interface Dashboard {
  from: string
  to: string
  metrics: Metric[]
  series: Series[]
  documentsByType: Breakdown[]
  documentsByDepartment: Breakdown[]
  documentsByStatus: Breakdown[]
  bottlenecks: Bottleneck[]
  /** True quando o painel mostra só a fatia que este utilizador pode ver. */
  scopedToCaller: boolean
}
