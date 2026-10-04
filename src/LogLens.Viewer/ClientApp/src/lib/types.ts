/** Shapes of the viewer API responses (src/LogLens.Viewer/Endpoints.cs). */

export interface LogEvent {
  seq: number
  timestamp: string
  level: string
  message: string
  category?: string
  template?: string
  exception?: string
  source?: string
  properties?: Record<string, string>
}

export interface Facets {
  levels?: Record<string, number>
  categories?: Record<string, number>
  properties?: Record<string, Record<string, number>>
}

export interface HistogramBucket {
  t: string
  count: number
  errors?: number
  warnings?: number
}

export interface TimeRange {
  from: string
  to: string
}

export interface EventsResponse {
  events: LogEvent[]
  total: number
  facets: Facets
  histogram: HistogramBucket[]
  range: TimeRange | null
  elapsedMs?: number
}

export interface AggregateGroup {
  key: string
  count: number
  errors: number
  last: string
  top: string[]
  p50: number | null
  p95: number | null
  max: number | null
  status5xx: number
  trend: number[]
  trendP95: (number | null)[]
  slowestSeq: number | null
}

export interface AggregateResponse {
  by: string
  total: number
  distinctKeys: number
  unattributed: number
  groups: AggregateGroup[]
}

export interface Stats {
  count: number
  capacity: number
  ingested: number
  sources: unknown[]
}

export interface ProfileEntity {
  label?: string
  link?: string
}

export interface QuickView {
  name: string
  query: string
  from?: string
}

export interface Profile {
  name: string | null
  entities: Record<string, ProfileEntity>
  quickViews: QuickView[]
  redact: string[]
  keys: { identity: string[]; duration: string[]; route: string[]; correlation: string[] }
}

/** Per-row view model, built once so the table doesn't recompute helpers on every render. */
export interface Row {
  e: LogEvent
  seq: number
  resource: string
  resourceKey: string | null
  color: string
  trace: { key: string; value: string } | null
  time: string
  timeUtc: string
}
