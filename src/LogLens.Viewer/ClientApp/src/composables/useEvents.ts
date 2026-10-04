import { ref, type Ref } from 'vue'
import { api } from '@/lib/api'
import { PAGE } from '@/lib/constants'
import { query } from '@/state/query'
import { toRow } from '@/state/profile'
import type { EventsResponse, Facets, HistogramBucket, Row, TimeRange } from '@/lib/types'

export interface EventsStore {
  rows: Ref<Row[]>
  total: Ref<number>
  elapsed: Ref<number | null>
  facets: Ref<Facets>
  histogram: Ref<HistogramBucket[]>
  range: Ref<TimeRange | null>
  hasMore: Ref<boolean>
  loaded: Ref<boolean>
  error: Ref<string>
  load(opts?: { append?: boolean; quiet?: boolean }): Promise<EventsResponse | null>
}

/** Snapshot loading with abort/ordering guarantees. */
export function useEvents(): EventsStore {
  const rows = ref<Row[]>([])
  const total = ref(0)
  const elapsed = ref<number | null>(null)
  const facets = ref<Facets>({})
  const histogram = ref<HistogramBucket[]>([])
  const range = ref<TimeRange | null>(null)
  const hasMore = ref(false)
  const loaded = ref(false)
  const error = ref('')
  let ctrl: AbortController | null = null, reqId = 0

  async function load({ append = false, quiet = false } = {}): Promise<EventsResponse | null> {
    const p = new URLSearchParams({ q: query.q, limit: String(PAGE) })
    if (query.from) p.set('from', query.from)
    if (query.to) p.set('to', query.to)
    if (append && rows.value.length) p.set('before', String(rows.value[rows.value.length - 1]!.seq))
    if (!append) { ctrl?.abort(); ctrl = new AbortController() }
    const id = ++reqId
    let data: EventsResponse
    try { data = await api<EventsResponse>('/api/events?' + p, { signal: append ? undefined : ctrl!.signal }) }
    catch (e) {
      if ((e as Error).name === 'AbortError') return null
      error.value = `Can't reach the viewer API (${(e as Error).message}).`
      loaded.value = true
      return null
    }
    if (id !== reqId && !append) return null   // a newer request won
    error.value = ''
    loaded.value = true
    if (append) {
      const have = new Set(rows.value.map(r => r.seq))
      rows.value.push(...data.events.filter(e => !have.has(e.seq)).map(toRow))
    } else if (!quiet) rows.value = data.events.map(toRow)
    hasMore.value = data.events.length >= PAGE
    total.value = data.total
    facets.value = data.facets
    histogram.value = data.histogram
    range.value = data.range
    if (!append) elapsed.value = data.elapsedMs ?? null
    return data
  }
  return { rows, total, elapsed, facets, histogram, range, hasMore, loaded, error, load }
}
