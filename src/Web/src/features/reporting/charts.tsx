import { useId, useState } from 'react'
import type { SeriesPoint } from './types'

/**
 * The chart marks, drawn as inline SVG.
 *
 * No charting library: three small multiples and a bar list do not justify a dependency, and the
 * marks a dashboard needs — a line with an area under it, a horizontal bar — are a path and a rect.
 *
 * Each chart carries **one series**. Three measures on three different scales share no axis, which
 * is the rule that matters most here: a second y-scale is the way dashboards lie without anyone
 * writing a wrong number.
 */

/** Teal, at the step that keeps enough chroma to read as a colour rather than as grey. */
const LINE = 'var(--chart-line)'

interface LineChartProps {
  points: SeriesPoint[]
  /** Formatted for the tooltip — the caller knows whether this is hours or a count. */
  format: (value: number) => string
  label: string
}

export function LineChart({ points, format, label }: LineChartProps) {
  const gradientId = useId()
  const [hover, setHover] = useState<number | null>(null)

  if (points.length === 0) {
    return null
  }

  const width = 100
  const height = 32
  const max = Math.max(...points.map((point) => point.value), 1)

  const x = (index: number) => (index / Math.max(points.length - 1, 1)) * width
  const y = (value: number) => height - (value / max) * (height - 2) - 1

  const line = points
    .map((point, index) => `${index === 0 ? 'M' : 'L'}${x(index)},${y(point.value)}`)
    .join(' ')
  const area = `${line} L${width},${height} L0,${height} Z`

  const active = hover === null ? null : points[hover]

  return (
    <div className="relative">
      <svg
        viewBox={`0 0 ${width} ${height}`}
        preserveAspectRatio="none"
        className="h-20 w-full"
        role="img"
        aria-label={label}
        onMouseLeave={() => setHover(null)}
        onMouseMove={(event) => {
          const box = event.currentTarget.getBoundingClientRect()
          const ratio = (event.clientX - box.left) / box.width
          setHover(Math.min(points.length - 1, Math.max(0, Math.round(ratio * (points.length - 1)))))
        }}
      >
        <defs>
          <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={LINE} stopOpacity="0.18" />
            <stop offset="100%" stopColor={LINE} stopOpacity="0" />
          </linearGradient>
        </defs>

        <path d={area} fill={`url(#${gradientId})`} />
        <path
          d={line}
          fill="none"
          stroke={LINE}
          strokeWidth="2"
          vectorEffect="non-scaling-stroke"
          strokeLinejoin="round"
          strokeLinecap="round"
        />

        {hover !== null && (
          <line
            x1={x(hover)}
            y1="0"
            x2={x(hover)}
            y2={height}
            stroke={LINE}
            strokeWidth="1"
            vectorEffect="non-scaling-stroke"
            strokeOpacity="0.4"
          />
        )}
      </svg>

      {active && (
        <p className="pointer-events-none absolute -top-1 right-0 rounded-md border border-subtle bg-raised px-2 py-0.5 text-[11px] text-ink shadow-card">
          {new Date(active.date).toLocaleDateString()} · {format(active.value)}
        </p>
      )}
    </div>
  )
}

interface BarListProps {
  rows: { label: string; value: number }[]
  emptyLabel: string
}

/**
 * Magnitude by category, as horizontal bars. Horizontal because the labels are words — a document
 * type, a department — and words read horizontally; vertical bars would turn them on their side or
 * truncate them.
 */
export function BarList({ rows, emptyLabel }: BarListProps) {
  if (rows.length === 0) {
    return <p className="text-sm text-ink-muted">{emptyLabel}</p>
  }

  const max = Math.max(...rows.map((row) => row.value), 1)

  return (
    <ul className="flex flex-col gap-1.5">
      {rows.map((row) => (
        <li key={row.label} className="grid grid-cols-[minmax(0,9rem)_1fr_auto] items-center gap-2">
          <span className="truncate text-xs text-ink-secondary" title={row.label}>
            {row.label}
          </span>
          <span className="h-2.5 rounded-xs bg-sunken">
            <span
              className="block h-full rounded-xs"
              style={{ width: `${Math.max((row.value / max) * 100, 2)}%`, backgroundColor: LINE }}
            />
          </span>
          <span className="text-xs tabular-nums text-ink">{row.value}</span>
        </li>
      ))}
    </ul>
  )
}
