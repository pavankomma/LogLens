<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import NavRail from '@/components/NavRail.vue'
import AppToolbar from '@/components/AppToolbar.vue'
import QuickViews from '@/components/QuickViews.vue'
import FilterChips from '@/components/FilterChips.vue'
import AddFilterDialog from '@/components/AddFilterDialog.vue'
import FiltersPanel from '@/components/FiltersPanel.vue'
import HistogramChart from '@/components/HistogramChart.vue'
import EventRow from '@/components/EventRow.vue'
import DetailsPane from '@/components/DetailsPane.vue'
import HelpDialog from '@/components/HelpDialog.vue'
import UsersView from '@/views/UsersView.vue'
import RequestsView from '@/views/RequestsView.vue'
import OverviewView from '@/views/OverviewView.vue'
import ChainView from '@/views/ChainView.vue'
import { useEvents } from '@/composables/useEvents'
import { useSelection } from '@/composables/useSelection'
import { useLive } from '@/composables/useLive'
import { useTheme } from '@/composables/useTheme'
import { useStats } from '@/composables/useStats'
import { RESOURCE_KEYS, quote, tokenize } from '@/lib/query-dsl.js'
import { api } from '@/lib/api'
import { MAX_ROWS } from '@/lib/constants'
import { fmtSpan, fmtTime, fmtTimeUtc, resourceColor } from '@/lib/format'
import { query, readHash, setTerms, terms, toggleTerm, writeHash } from '@/state/query'
import { profile, toRow } from '@/state/profile'
import type { AggregateResponse, LogEvent, Profile } from '@/lib/types'

const ev = useEvents()
const sel = useSelection(ev.rows)
const grid = ref<HTMLElement | null>(null)
const live = useLive(ev.rows, ev.load, sel, grid)
const { theme } = useTheme()
const { stats, rate, poll } = useStats()
const help = ref(false)
const addFilter = ref(false)
const menu = ref(false)

const resourceKey = computed(() => {
  for (const k of RESOURCE_KEYS) if (ev.facets.value?.properties?.[k]) return k
  return null
})

// Grid preferences (persisted): visible columns and timestamp zone.
const COL_DEFS: [string, string][] = [['resource', 'Resource'], ['level', 'Level'], ['timestamp', 'Timestamp'], ['category', 'Category'], ['trace', 'Trace'], ['details', 'Details']]
const cols = reactive<Record<string, boolean>>({ resource: true, level: true, timestamp: true, category: true, trace: true, details: true })
try { for (const k of (JSON.parse(localStorage.getItem('hiddenCols') || 'null') as string[] | null) || []) if (k in cols) cols[k] = false } catch { /* storage unavailable */ }
watch(cols, c => { try { localStorage.setItem('hiddenCols', JSON.stringify(COL_DEFS.map(([k]) => k).filter(k => !c[k]))) } catch { /* storage unavailable */ } })
const colsMenu = ref(false)
const utcMode = ref(false)
try { utcMode.value = localStorage.getItem('tz') === 'utc' } catch { /* storage unavailable */ }
watch(utcMode, v => { try { localStorage.setItem('tz', v ? 'utc' : 'local') } catch { /* storage unavailable */ } })
const fmtT = (iso: string) => utcMode.value ? fmtTimeUtc(iso) : fmtTime(iso)
const bucketInfo = computed(() => {
  const r = ev.range.value, b = ev.histogram.value
  if (!r || !b?.length) return ''
  const span = (new Date(r.to).getTime() - new Date(r.from).getTime()) / b.length
  return span > 0 ? `${b.length} × ${fmtSpan(span)} buckets` : ''
})

// Group-by histogram: split buckets by a field via /api/aggregate per-group trends.
const groupBy = ref('')
const groups = ref<{ key: string; color: string; trend: number[]; count: number }[]>([])
const groupOptions = computed(() => {
  const out: [string, string][] = []
  if (resourceKey.value) out.push([resourceKey.value, 'Resource'])
  if (Object.keys(ev.facets.value?.categories || {}).length) out.push(['category', 'Category'])
  return out.concat(Object.keys(ev.facets.value?.properties || {}).filter(k => !RESOURCE_KEYS.includes(k)).map(k => [k, k] as [string, string]))
})
let groupCtrl: AbortController | null = null
async function loadGroups() {
  if (!groupBy.value) { groups.value = []; return }
  groupCtrl?.abort()
  groupCtrl = new AbortController()
  const p = new URLSearchParams({ by: groupBy.value, q: query.q, buckets: '60', limit: '8', top: '0' })
  if (query.from) p.set('from', query.from)
  if (query.to) p.set('to', query.to)
  try {
    const data = await api<AggregateResponse>('/api/aggregate?' + p, { signal: groupCtrl.signal })
    groups.value = data.groups.map(g => ({ key: g.key, color: resourceColor(g.key), trend: g.trend, count: g.count }))
  } catch (e) { if ((e as Error).name !== 'AbortError') groups.value = [] }
}
watch([groupBy, () => ev.histogram.value], loadGroups)   // histogram is reassigned on every load, live refreshes included
watch(groupOptions, o => { if (groupBy.value && !o.some(([v]) => v === groupBy.value)) groupBy.value = '' })
const groupTerm = (k: string) => groupBy.value === 'category' ? 'category:' + quote(k) : groupBy.value + '=' + quote(k)
const resources = computed(() => {
  if (resourceKey.value) return ev.facets.value.properties![resourceKey.value]!
  const out: Record<string, number> = {}
  for (const r of ev.rows.value) if (r.resource) out[r.resource] = (out[r.resource] || 0) + 1
  return out
})
const hasFilters = computed(() => terms.value.length > 0 || !!query.from || !!query.to)
const truncated = computed(() => ev.rows.value.length >= MAX_ROWS)
const modeLabel = computed(() => ({ off: 'Explore', live: 'Live', paused: 'Paused', reconnecting: 'Reconnecting…' })[live.conn.value])

function select(i: number, open = true) {
  const r = ev.rows.value[Math.max(0, Math.min(ev.rows.value.length - 1, i))]
  if (!r) return
  sel.selectedSeq.value = r.seq
  if (open) sel.open.value = true
  nextTick(() => {
    const row = grid.value?.querySelector<HTMLElement>('tr.selected')
    row?.scrollIntoView({ block: 'nearest' })
    if (!open) row?.focus()
  })
}
function closeDetails() {
  sel.open.value = false
  nextTick(() => {
    grid.value?.querySelector<HTMLElement>('tr.selected')?.focus()
    if (live.pinnedToTop.value) live.showPending()
  })
}
const lastView = ref('logs')
function chainOpen(key: string, value: string) {
  lastView.value = query.view === 'chain' ? lastView.value : query.view
  query.ck = key
  query.cv = value
  query.view = 'chain'
}
const chain = (row: { trace: { key: string; value: string } | null }) => { if (row.trace) chainOpen(row.trace.key, row.trace.value) }
const chainBack = () => { query.view = lastView.value === 'chain' ? 'logs' : lastView.value as typeof query.view }
const clearFilters = () => { query.q = ''; query.from = ''; query.to = '' }
async function deleteAll() {
  menu.value = false
  if (!confirm('Delete all retained log entries from this viewer? This cannot be undone.')) return
  try { await api('/api/events', { method: 'DELETE' }) }
  catch (e) { ev.error.value = `Delete failed (${(e as Error).message}). If an API key is configured, deletion needs it.` }
  ev.load()
  poll()
}
async function openSeq(seq: number) {
  try {
    const e = await api<LogEvent>('/api/events/' + seq)
    if (!ev.rows.value.some(r => r.seq === e.seq)) ev.rows.value.unshift(toRow(e))
    sel.selectedSeq.value = e.seq
    sel.open.value = true
  } catch { ev.error.value = `Entry #${seq} is no longer retained.` }
}

function onKey(e: KeyboardEvent) {
  if (e.defaultPrevented || e.ctrlKey || e.metaKey || e.altKey) return
  if (help.value || addFilter.value) return         // dialogs own the keyboard
  const tag = (e.target as HTMLElement).tagName
  if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA') return
  if (e.key === '/') { e.preventDefault(); const i = document.getElementById('q') as HTMLInputElement; i.focus(); i.select() }
  else if (e.key === 'l' && !query.to) query.live = !query.live
  else if (e.key === 'Escape') { if (menu.value) menu.value = false; else if (sel.open.value) closeDetails() }
  else if (e.key === 'j' || e.key === 'ArrowDown') { e.preventDefault(); select(sel.index.value + 1, sel.open.value) }
  else if (e.key === 'k' || e.key === 'ArrowUp') { e.preventDefault(); select(sel.index.value - 1, sel.open.value) }
  else if (e.key === 'Enter' && sel.index.value >= 0) { if (sel.open.value) closeDetails(); else sel.open.value = true }
}

readHash()
const apply = () => { if (query.live && query.view === 'logs') live.start(); else { live.stop(); ev.load() } }
watch(() => [query.q, query.from, query.to], () => { writeHash(true); apply() })
watch(() => query.view, () => { writeHash(true); apply() })
function drill(term: string) { query.view = 'logs'; setTerms([...new Set([...terms.value, ...tokenize(term)])]) }
watch(() => query.live, on => { writeHash(); if (on) live.start(); else { live.stop(); ev.load({ quiet: true }) } })
watch(() => query.to, to => { if (to && query.live) query.live = false })

const onDocClick = (e: MouseEvent) => {
  const t = e.target as Element
  if (!t.closest('.menu')) menu.value = false
  if (!t.closest('.colmenu')) colsMenu.value = false
}
const onPopState = () => { readHash(); apply() }
onMounted(() => {
  api<Profile>('/api/profile').then(p => Object.assign(profile, p)).catch(() => { /* profile is optional */ })
  ;(query.live ? live.start() : ev.load()).then(() => { if (query.seq) openSeq(query.seq) })
  window.addEventListener('popstate', onPopState)
  document.addEventListener('keydown', onKey)
  document.addEventListener('click', onDocClick)
})
onBeforeUnmount(() => {
  live.stop()
  window.removeEventListener('popstate', onPopState)
  document.removeEventListener('keydown', onKey)
  document.removeEventListener('click', onDocClick)
})
</script>

<template>
  <NavRail />
  <div class="page">
    <div class="pagehead">
      <h1>{{ { logs: 'Structured logs', overview: 'Overview', users: 'Users', requests: 'Requests', chain: 'Chain of events' }[query.view] }}</h1>
      <span class="sub" v-if="stats">{{ stats.count.toLocaleString() }} of {{ stats.capacity.toLocaleString() }} retained<template v-if="stats.sources.length > 1"> · {{ stats.sources.length - 1 }} file source{{ stats.sources.length === 2 ? '' : 's' }}</template></span>
      <span class="spacer"></span>
      <label class="field theme"><span class="sr">Theme</span><select v-model="theme" aria-label="Theme"><option value="">Theme: System</option><option value="light">Theme: Light</option><option value="dark">Theme: Dark</option></select></label>
      <button class="icon-btn" aria-label="Help" @click="help = true">?</button>
      <div class="menu"><button class="icon-btn" aria-label="More actions" :aria-expanded="menu" @click="menu = !menu">⋯</button>
        <div class="menu-list" v-if="menu" role="menu"><button role="menuitem" class="danger" @click="deleteAll">Delete all retained entries…</button></div></div>
    </div>
    <AppToolbar :resources="resources" :resource-key="resourceKey" :facets="ev.facets.value" :paused="live.paused.value" :buffered="live.pendingCount.value" @help="help = true" @pause="live.paused.value = !live.paused.value" @addfilter="addFilter = true" />
    <QuickViews />
    <FilterChips />
    <div class="banner error" v-if="ev.error.value" role="alert"><span>{{ ev.error.value }}</span><button class="link" @click="ev.load()">Retry</button></div>
    <div class="body" v-if="query.view === 'overview'" style="grid-template-columns: minmax(0, 1fr)">
      <OverviewView :facets="ev.facets.value" :histogram="ev.histogram.value" :range="ev.range.value" :total="ev.total.value" :stats="stats" :rate="rate" @drill="drill" />
    </div>
    <div class="body" v-else-if="query.view === 'users'" style="grid-template-columns: minmax(0, 1fr)">
      <UsersView :facets="ev.facets.value" @drill="drill" />
    </div>
    <div class="body" v-else-if="query.view === 'requests'" style="grid-template-columns: minmax(0, 1fr)">
      <RequestsView :facets="ev.facets.value" @drill="drill" @open="seq => { openSeq(seq) }" />
    </div>
    <div class="body" v-else-if="query.view === 'chain'" :style="{ gridTemplateColumns: sel.open.value ? 'minmax(0, 1fr) auto' : 'minmax(0, 1fr)' }">
      <ChainView @open="seq => openSeq(seq)" @drill="drill" @back="chainBack" />
      <DetailsPane v-if="sel.open.value && sel.current.value" :event="sel.current.value" :index="sel.index.value" :count="ev.rows.value.length" @close="closeDetails" @prev="select(sel.index.value - 1)" @next="select(sel.index.value + 1)" @chain="chainOpen" />
    </div>
    <div class="body" v-else>
      <FiltersPanel :facets="ev.facets.value" :resources="resources" :resource-key="resourceKey" />
      <section class="panel gridwrap">
        <div class="summary">
          <strong>{{ ev.total.value.toLocaleString() }}</strong><span class="dim">matching</span>
          <span class="dim" v-if="ev.elapsed.value != null" :title="'The query took ' + ev.elapsed.value + ' ms'">· {{ ev.elapsed.value }} ms</span>
          <span class="dim" v-if="truncated">· showing latest {{ MAX_ROWS.toLocaleString() }}</span>
          <span class="spacer"></span>
          <div class="field mini"><label for="groupSel">Group by</label>
            <select id="groupSel" v-model="groupBy"><option value="">Severity</option><option v-for="[v, label] in groupOptions" :key="v" :value="v">{{ label }}</option></select>
          </div>
          <div class="seg mini" role="group" aria-label="Timestamp zone">
            <button :class="{ on: !utcMode }" :aria-pressed="!utcMode" @click="utcMode = false">Local</button>
            <button :class="{ on: utcMode }" :aria-pressed="utcMode" @click="utcMode = true">UTC</button>
          </div>
          <div class="menu colmenu">
            <button class="btn mini" :aria-expanded="colsMenu" aria-haspopup="true" @click="colsMenu = !colsMenu">Columns</button>
            <div class="menu-list cols" v-if="colsMenu">
              <label class="check" v-for="[k, label] in COL_DEFS" :key="k"><input type="checkbox" v-model="cols[k]"> {{ label }}</label>
            </div>
          </div>
          <button class="link" v-if="hasFilters" @click="clearFilters">Clear filters</button>
        </div>
        <HistogramChart :buckets="ev.histogram.value" :range="ev.range.value" :groups="groupBy ? groups : null" />
        <div class="legend" v-if="groupBy && groups.length">
          <button class="lg" v-for="g in groups" :key="g.key" :title="'Filter to ' + g.key" @click="toggleTerm(groupTerm(g.key))"><span class="swatch" :style="{ background: g.color }"></span><span class="v">{{ g.key }}</span><span class="n">{{ g.count.toLocaleString() }}</span></button>
        </div>
        <div class="axis" v-if="ev.range.value"><span>{{ fmtT(ev.range.value.from) }}</span><span class="hint" v-if="bucketInfo">{{ bucketInfo }} · hover to inspect · drag to zoom</span><span>{{ fmtT(ev.range.value.to) }}</span></div>
        <div ref="grid" class="grid" @scroll.passive="live.onScroll">
          <button v-if="live.pendingCount.value && !live.paused.value" class="pill" aria-live="polite" @click="live.showPending">▲ {{ live.pendingCount.value.toLocaleString() }} new {{ live.pendingCount.value === 1 ? 'entry' : 'entries' }}</button>
          <table class="dg" role="grid" :aria-rowcount="ev.rows.value.length" aria-label="Log entries">
            <colgroup><col v-if="cols.resource" style="width:150px"><col v-if="cols.level" style="width:118px"><col v-if="cols.timestamp" style="width:112px"><col v-if="cols.category" class="c-cat" style="width:150px"><col><col v-if="cols.trace" class="c-trace" style="width:96px"><col v-if="cols.details" style="width:60px"></colgroup>
            <thead><tr role="row"><th v-if="cols.resource" scope="col">Resource</th><th v-if="cols.level" scope="col">Level</th><th v-if="cols.timestamp" scope="col">Timestamp <span class="dim">({{ utcMode ? 'UTC' : 'local' }})</span></th><th v-if="cols.category" scope="col" class="c-cat">Category</th><th scope="col">Message</th><th v-if="cols.trace" scope="col" class="c-trace">Trace</th><th v-if="cols.details" scope="col">Details</th></tr></thead>
            <tbody>
              <EventRow v-for="(r, i) in ev.rows.value" :key="r.seq" :row="r" :cols="cols" :utc="utcMode" :selected="r.seq === sel.selectedSeq.value" :tabbable="i === (sel.index.value < 0 ? 0 : sel.index.value)" @select="select(i)" @chain="chain(r)" />
            </tbody>
          </table>
          <div class="more" v-if="ev.hasMore.value"><button class="btn" @click="ev.load({ append: true })">Load older</button></div>
          <div class="empty" v-if="ev.loaded.value && !ev.error.value && ev.total.value === 0">
            <template v-if="hasFilters"><h3>Nothing matches these filters</h3><p><button class="link" @click="clearFilters">Clear filters</button></p></template>
            <template v-else>
              <h3>No log entries yet</h3>
              <p class="dim">Send some from your app — pick your stack:</p>
              <div class="snips">
                <div><b>Serilog</b><pre>.WriteTo.Seq("http://localhost:5341")</pre></div>
                <div><b>ASP.NET Core / ILogger</b><pre>builder.Logging.AddLogLens(cfg =&gt; cfg.WriteTo.Http("http://localhost:5341"))</pre></div>
                <div><b>Files</b><pre>LogLens.Viewer --tail C:\logs</pre></div>
                <div><b>Try it</b><pre>LogLens.Viewer --demo</pre></div>
              </div>
            </template>
          </div>
        </div>
      </section>
      <DetailsPane v-if="sel.open.value && sel.current.value" :event="sel.current.value" :index="sel.index.value" :count="ev.rows.value.length" @close="closeDetails" @prev="select(sel.index.value - 1)" @next="select(sel.index.value + 1)" @chain="chainOpen" />
    </div>
    <footer class="status" aria-live="polite">
      <span>{{ modeLabel }}</span>
      <span v-if="rate > 0">{{ rate.toFixed(rate < 10 ? 1 : 0) }} entries/s</span>
      <span class="spacer"></span>
      <span v-if="stats && stats.count >= stats.capacity">Buffer full — oldest entries are being dropped</span>
    </footer>
    <HelpDialog v-if="help" @close="help = false" />
    <AddFilterDialog v-if="addFilter" :facets="ev.facets.value" :resources="resources" :resource-key="resourceKey" @close="addFilter = false" />
  </div>
</template>
