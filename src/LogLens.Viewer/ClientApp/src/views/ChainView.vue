<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { quote, resourceOf } from '@/lib/query-dsl.js'
import { api } from '@/lib/api'
import { fmtTime, resourceColor } from '@/lib/format'
import { query } from '@/state/query'
import type { EventsResponse, LogEvent } from '@/lib/types'

/** Chain-of-events timeline: every entry sharing one correlation id, oldest first. */
defineEmits<{ open: [seq: number]; drill: [term: string]; back: [] }>()

interface ChainRow {
  e: LogEvent
  seq: number
  rel: number
  gap: number
  bigGap: boolean
  resource: string
  color: string
  newService: boolean
  span: string | null
}

const rows = ref<ChainRow[]>([])
const error = ref('')
const loading = ref(true)
const errorsOnly = ref(false)
const collapsed = reactive(new Set<string>())   // collapsed SpanId groups
async function load() {
  loading.value = true
  try {
    const p = new URLSearchParams({ q: query.ck + '=' + quote(query.cv), limit: '1000' })
    const data = await api<EventsResponse>('/api/events?' + p)
    const asc = data.events.slice().sort((a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime() || a.seq - b.seq)
    const t0 = asc.length ? new Date(asc[0]!.timestamp).getTime() : 0
    const gaps: number[] = []
    for (let i = 1; i < asc.length; i++) gaps.push(new Date(asc[i]!.timestamp).getTime() - new Date(asc[i - 1]!.timestamp).getTime())
    const sortedGaps = gaps.slice().sort((a, b) => a - b)
    const median = sortedGaps.length ? sortedGaps[Math.floor(sortedGaps.length / 2)]! : 0
    const gapThreshold = Math.max(1000, median * 5)
    rows.value = asc.map((e, i) => {
      const r = resourceOf(e)
      const gap = i === 0 ? 0 : new Date(e.timestamp).getTime() - new Date(asc[i - 1]!.timestamp).getTime()
      return {
        e, seq: e.seq, rel: new Date(e.timestamp).getTime() - t0, gap, bigGap: gap >= gapThreshold,
        resource: r.value, color: resourceColor(r.value), newService: i === 0 || r.value !== resourceOf(asc[i - 1]!).value,
        span: e.properties?.SpanId || null,
      }
    })
    error.value = ''
  } catch (e) { error.value = (e as Error).message }
  finally { loading.value = false }
}
watch(() => [query.ck, query.cv], load, { immediate: true })
const services = computed(() => [...new Set(rows.value.map(r => r.resource).filter(Boolean))])
const errorCount = computed(() => rows.value.filter(r => r.e.level === 'Error' || r.e.level === 'Fatal').length)
const wall = computed(() => rows.value.length ? rows.value[rows.value.length - 1]!.rel : 0)
const spans = computed(() => {
  const c: Record<string, number> = {}
  for (const r of rows.value) if (r.span) c[r.span] = (c[r.span] || 0) + 1
  return c
})
const visible = computed(() => rows.value.filter(r => {
  if (errorsOnly.value && r.e.level !== 'Error' && r.e.level !== 'Fatal' && r.e.level !== 'Warning') return false
  return true
}))
const fmtRel = (ms: number) => ms >= 10000 ? '+' + (ms / 1000).toFixed(2) + ' s' : '+' + Math.round(ms) + ' ms'
const fmtGap = (ms: number) => ms >= 1000 ? (ms / 1000).toFixed(2) + ' s gap' : Math.round(ms) + ' ms gap'
const toggleSpan = (id: string) => { if (collapsed.has(id)) collapsed.delete(id); else collapsed.add(id) }
const isHidden = (r: ChainRow) => !!r.span && collapsed.has(r.span) && spans.value[r.span]! > 1 && rows.value.find(x => x.span === r.span) !== r
</script>

<template>
  <section class="panel dash chain" aria-label="Chain of events">
    <div class="dash-head">
      <h2>Chain</h2>
      <span class="chip"><b>{{ query.ck }}</b> <span class="op">is</span> {{ query.cv }}</span>
      <span class="dim" v-if="rows.length">{{ rows.length }} entries · {{ services.length }} service{{ services.length === 1 ? '' : 's' }} · <span :class="{ 'num bad': errorCount }">{{ errorCount }} error{{ errorCount === 1 ? '' : 's' }}</span> · {{ fmtRel(wall).slice(1) }} wall</span>
      <span class="spacer"></span>
      <label class="check"><input type="checkbox" v-model="errorsOnly"> Problems only</label>
      <button class="link" @click="$emit('drill', query.ck + '=' + query.cv)">Open as filter</button>
      <button class="icon-btn" aria-label="Back to logs" title="Back" @click="$emit('back')">✕</button>
    </div>
    <div class="banner error" v-if="error" role="alert"><span>{{ error }}</span><button class="link" @click="load">Retry</button></div>
    <div class="grid">
      <div class="empty" v-if="!loading && !rows.length"><h3>No entries carry {{ query.ck }} = {{ query.cv }}</h3><p class="dim">The entry may have been evicted from the buffer.</p></div>
      <ol class="timeline" v-if="rows.length">
        <template v-for="r in visible" :key="r.seq">
          <li class="gaprow" v-if="r.bigGap" aria-hidden="true">⋯ {{ fmtGap(r.gap) }} ⋯</li>
          <li class="trow" :class="{ err: r.e.level === 'Error' || r.e.level === 'Fatal', warn: r.e.level === 'Warning', hidden: isHidden(r) }" v-show="!isHidden(r)">
            <span class="rel">{{ fmtRel(r.rel) }}</span>
            <span class="lane" :style="{ background: r.color }" :title="r.resource"></span>
            <span class="svc" :class="{ ghost: !r.newService }">{{ r.resource }}</span>
            <span class="lvl" :class="'lvl-' + r.e.level">{{ r.e.level }}</span>
            <button v-if="r.span && spans[r.span]! > 1" class="spanbtn" :title="'Span ' + r.span + ' (' + spans[r.span] + ' entries)'" @click="toggleSpan(r.span)">{{ collapsed.has(r.span) ? '▸' : '▾' }}</button>
            <button class="tmsg" @click="$emit('open', r.seq)">{{ r.e.message }}<span v-if="r.e.exception" class="exc-flag">⚠</span></button>
            <span class="ts dim" :title="r.e.timestamp">{{ fmtTime(r.e.timestamp) }}</span>
          </li>
        </template>
      </ol>
    </div>
  </section>
</template>
