import { ref, watch, type Ref } from 'vue'
import { LIVE_BATCH_MAX, MAX_ROWS, ROW_FLUSH_MS, SUMMARY_MS } from '@/lib/constants'
import { query } from '@/state/query'
import { toRow } from '@/state/profile'
import type { LogEvent, Row } from '@/lib/types'
import type { Selection } from './useSelection'

export type ConnState = 'off' | 'live' | 'paused' | 'reconnecting'

export interface LiveStream {
  paused: Ref<boolean>
  pendingCount: Ref<number>
  pinnedToTop: Ref<boolean>
  conn: Ref<ConnState>
  start(): Promise<void>
  stop(): void
  showPending(): void
  onScroll(): void
}

/** SSE live stream with batching, a stash for when the user isn't at the top / is reading, and buffered pause. */
export function useLive(rows: Ref<Row[]>, load: (opts?: { quiet?: boolean }) => Promise<unknown>, sel: Selection, grid: Ref<HTMLElement | null>): LiveStream {
  const paused = ref(false)
  const pendingCount = ref(0)
  const pinnedToTop = ref(true)
  const conn = ref<ConnState>('off')
  let es: EventSource | null = null
  let rowTimer: ReturnType<typeof setInterval> | undefined, summaryTimer: ReturnType<typeof setInterval> | undefined
  let pending: LogEvent[] = [], maxSeq = 0, gen = 0
  const stash: LogEvent[] = []

  const refreshMaxSeq = () => { maxSeq = rows.value.reduce((m, r) => Math.max(m, r.seq), 0) }
  function insert(batch: LogEvent[]) {
    const fresh = batch.map(toRow)
    rows.value.unshift(...fresh)
    if (rows.value.length > MAX_ROWS) rows.value.length = MAX_ROWS
    maxSeq = Math.max(maxSeq, fresh[0]!.seq)
  }
  function flush() {
    if (!pending.length) return
    const seen = new Set<number>()
    const batch = pending.filter(e => e.seq > maxSeq && !seen.has(e.seq) && !!seen.add(e.seq)).sort((a, b) => b.seq - a.seq)
    pending = []
    if (!batch.length) return
    if (!pinnedToTop.value || sel.open.value || paused.value) { stash.push(...batch); pendingCount.value = stash.length; return }
    insert(batch)
  }
  function showPending() {
    if (stash.length) insert(stash.sort((a, b) => b.seq - a.seq).splice(0))
    pendingCount.value = 0
    pinnedToTop.value = true
    if (grid.value) grid.value.scrollTop = 0
  }
  function onScroll() {
    if (!grid.value) return
    pinnedToTop.value = grid.value.scrollTop < 8
    if (pinnedToTop.value && stash.length && !sel.open.value && !paused.value) showPending()
  }
  function stop() {
    es?.close(); es = null
    clearInterval(rowTimer); clearInterval(summaryTimer)
    pending = []; stash.length = 0; pendingCount.value = 0
    conn.value = 'off'
  }
  async function start() {
    const my = ++gen
    stop()
    await load()                  // snapshot first, then stream everything newer than it
    if (!query.live || my !== gen) return   // a newer start() superseded this one
    refreshMaxSeq()
    es = new EventSource('/api/stream?q=' + encodeURIComponent(query.q))
    es.onopen = () => conn.value = paused.value ? 'paused' : 'live'
    es.onerror = () => conn.value = 'reconnecting'
    es.onmessage = m => {
      try { pending.push(JSON.parse(m.data as string) as LogEvent) } catch { return }
      if (pending.length >= LIVE_BATCH_MAX) flush()
    }
    rowTimer = setInterval(flush, ROW_FLUSH_MS)
    summaryTimer = setInterval(() => { if (!paused.value && es?.readyState === 1) load({ quiet: true }) }, SUMMARY_MS)
  }
  watch(paused, p => {
    conn.value = es ? (p ? 'paused' : (es.readyState === 1 ? 'live' : 'reconnecting')) : 'off'
    if (!p && pinnedToTop.value && !sel.open.value) showPending()
  })
  return { paused, pendingCount, pinnedToTop, conn, start, stop, showPending, onScroll }
}
