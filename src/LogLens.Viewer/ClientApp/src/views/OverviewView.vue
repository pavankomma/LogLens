<script setup lang="ts">
import { computed, watch } from 'vue'
import SparklineBar from '@/components/SparklineBar.vue'
import { useAggregate } from '@/composables/useAggregate'
import { RESOURCE_KEYS } from '@/lib/query-dsl.js'
import { fmtMs, resourceColor } from '@/lib/format'
import { query } from '@/state/query'
import { detectKey, durationKeys } from '@/state/profile'
import type { Facets, HistogramBucket, Stats, TimeRange } from '@/lib/types'

/** Overview: tiles + who is failing. */
const props = defineProps<{
  facets?: Facets | null
  histogram?: HistogramBucket[] | null
  range?: TimeRange | null
  total: number
  stats: Stats | null
  rate: number
}>()
defineEmits<{ drill: [term: string] }>()

const agg = useAggregate()
const reload = () => agg.load(detectKey(props.facets, RESOURCE_KEYS) || 'category', detectKey(props.facets, durationKeys()) || durationKeys()[0])
watch([() => props.facets, () => query.q, () => query.from, () => query.to], reload, { immediate: true, deep: false })
const errorCount = computed(() => Object.entries(props.facets?.levels || {}).filter(([l]) => l === 'Error' || l === 'Fatal').reduce((a, [, n]) => a + n, 0))
const errRate = computed(() => props.total ? (errorCount.value / props.total * 100) : 0)
const failing = computed(() => (agg.data.value?.groups || []).filter(g => g.errors).sort((a, b) => b.errors - a.errors).slice(0, 5))
const slowest = computed(() => (agg.data.value?.groups || []).filter(g => g.p95 != null).sort((a, b) => b.p95! - a.p95!).slice(0, 5))
</script>

<template>
  <section class="dash-col" aria-label="Overview">
    <div class="tiles">
      <div class="tile"><div class="n">Entries (range)</div><div class="v">{{ (total || 0).toLocaleString() }}</div></div>
      <div class="tile"><div class="n">Rate</div><div class="v">{{ rate > 0 ? rate.toFixed(rate < 10 ? 1 : 0) : '0' }}<span class="u"> entries/s</span></div></div>
      <div class="tile" :class="{ bad: errRate > 3, warn: errRate > 0 && errRate <= 3 }"><div class="n">Errors</div><div class="v">{{ errorCount.toLocaleString() }}<span class="u"> · {{ errRate.toFixed(1) }} %</span></div></div>
      <div class="tile"><div class="n">Retained</div><div class="v">{{ stats ? stats.count.toLocaleString() : '—' }}<span class="u" v-if="stats"> / {{ stats.capacity.toLocaleString() }}</span></div></div>
    </div>
    <div class="two-col">
      <div class="panel dash">
        <div class="dash-head"><h2>Most errors</h2><span class="dim">by {{ agg.data.value?.by || 'resource' }}</span></div>
        <table class="dg">
          <thead><tr><th scope="col">Name</th><th scope="col">Errors</th><th scope="col">Entries</th><th scope="col">Trend</th></tr></thead>
          <tbody>
            <tr v-for="g in failing" :key="g.key" tabindex="0" @click="$emit('drill', (agg.data.value!.by === 'category' ? 'category:' : agg.data.value!.by + '=') + g.key + ' level:error+')">
              <td><span class="res"><span class="swatch" :style="{ background: resourceColor(g.key) }"></span><span class="v">{{ g.key }}</span></span></td>
              <td class="num bad">{{ g.errors.toLocaleString() }}</td>
              <td class="num">{{ g.count.toLocaleString() }}</td>
              <td><SparklineBar :values="g.trend.map(v => v / Math.max(1, ...g.trend))" /></td>
            </tr>
            <tr v-if="!failing.length"><td colspan="4" class="dim">No errors in this range 🎉</td></tr>
          </tbody>
        </table>
      </div>
      <div class="panel dash">
        <div class="dash-head"><h2>Slowest</h2><span class="dim">p95 duration</span></div>
        <table class="dg">
          <thead><tr><th scope="col">Name</th><th scope="col">P95</th><th scope="col">Max</th><th scope="col">Calls</th></tr></thead>
          <tbody>
            <tr v-for="g in slowest" :key="g.key" tabindex="0" @click="$emit('drill', (agg.data.value!.by === 'category' ? 'category:' : agg.data.value!.by + '=') + g.key)">
              <td><span class="res"><span class="v">{{ g.key }}</span></span></td>
              <td class="num warn">{{ fmtMs(g.p95) }}</td>
              <td class="num">{{ fmtMs(g.max) }}</td>
              <td class="num">{{ g.count.toLocaleString() }}</td>
            </tr>
            <tr v-if="!slowest.length"><td colspan="4" class="dim">No duration property found (log an Elapsed value to see latency here)</td></tr>
          </tbody>
        </table>
      </div>
    </div>
  </section>
</template>
