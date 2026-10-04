<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import KeyPicker from '@/components/KeyPicker.vue'
import SparklineBar from '@/components/SparklineBar.vue'
import { useAggregate } from '@/composables/useAggregate'
import { fmtMs } from '@/lib/format'
import { query } from '@/state/query'
import { durationKeys, routeKeys } from '@/state/profile'
import type { AggregateGroup, Facets } from '@/lib/types'

/** Requests dashboard: routes by duration percentiles and failures. */
const props = defineProps<{ facets?: Facets | null }>()
defineEmits<{ drill: [term: string]; open: [seq: number] }>()

const agg = useAggregate()
const routeOptions = computed(() => {
  const found = routeKeys().filter(k => props.facets?.properties?.[k])
  return found.length ? found : routeKeys().slice(0, 1)
})
const durOptions = computed(() => {
  const found = durationKeys().filter(k => props.facets?.properties?.[k])
  return found.length ? found : durationKeys().slice(0, 1)
})
const routeKey = ref('')
const durKey = ref('')
watch(routeOptions, o => { if (!routeKey.value || !o.includes(routeKey.value)) routeKey.value = o[0] || '' }, { immediate: true })
watch(durOptions, o => { if (!durKey.value || !o.includes(durKey.value)) durKey.value = o[0] || '' }, { immediate: true })
const reload = () => agg.load(routeKey.value, durKey.value)
watch([routeKey, durKey, () => query.q, () => query.from, () => query.to], reload, { immediate: true })
const rows = computed(() => (agg.data.value?.groups || []).slice().sort((a, b) => (b.p95 ?? -1) - (a.p95 ?? -1)))
const maxP95 = computed(() => Math.max(1, ...rows.value.map(g => g.p95 ?? 0)))
const spark = (g: AggregateGroup) => { const m = Math.max(1, ...g.trendP95.map(v => v ?? 0)); return g.trendP95.map(v => (v ?? 0) / m) }
const hot = (g: AggregateGroup) => { const m = Math.max(1, ...g.trendP95.map(v => v ?? 0)); return g.trendP95.map(v => (v ?? 0) > .8 * m && m > 0) }
</script>

<template>
  <section class="panel dash" aria-label="Requests">
    <div class="dash-head">
      <h2>Requests</h2>
      <span class="dim" v-if="agg.data.value">{{ agg.data.value.groups.length }} routes · slowest first (p95)</span>
      <span class="spacer"></span>
      <KeyPicker label="Group by" v-model="routeKey" :options="routeOptions" />
      <KeyPicker label="Duration" v-model="durKey" :options="durOptions" />
    </div>
    <div class="banner error" v-if="agg.error.value" role="alert"><span>{{ agg.error.value }}</span><button class="link" @click="reload">Retry</button></div>
    <div class="grid">
      <table class="dg" aria-label="Requests">
        <colgroup><col><col style="width:90px"><col style="width:90px"><col style="width:110px"><col style="width:90px"><col style="width:70px"><col style="width:150px"><col style="width:80px"></colgroup>
        <thead><tr><th scope="col">{{ routeKey }}</th><th scope="col">Calls</th><th scope="col">P50</th><th scope="col">P95</th><th scope="col">Max</th><th scope="col">5xx</th><th scope="col">P95 trend</th><th scope="col"></th></tr></thead>
        <tbody>
          <tr v-for="g in rows" :key="g.key" tabindex="0" @click="$emit('drill', routeKey + '=' + g.key)" @keydown.enter.prevent="$emit('drill', routeKey + '=' + g.key)">
            <td :title="g.key"><span class="res"><span class="v">{{ g.key }}</span></span></td>
            <td class="num">{{ g.count.toLocaleString() }}</td>
            <td class="num">{{ fmtMs(g.p50) }}</td>
            <td class="num" :class="{ bad: g.p95 != null && g.p95 >= .8 * maxP95 && g.p95 > 500, warn: g.p95 != null && g.p95 > 500 && g.p95 < .8 * maxP95 }">{{ fmtMs(g.p95) }}</td>
            <td class="num">{{ fmtMs(g.max) }}</td>
            <td class="num" :class="{ bad: g.status5xx }">{{ g.status5xx.toLocaleString() }}</td>
            <td><SparklineBar :values="spark(g)" :hot="hot(g)" /></td>
            <td><button v-if="g.slowestSeq" class="view" @click.stop="$emit('open', g.slowestSeq)">slowest</button></td>
          </tr>
        </tbody>
      </table>
      <div class="empty" v-if="agg.data.value && !agg.data.value.groups.length"><h3>No entries carry a {{ routeKey }} property</h3><p class="dim">ASP.NET Core request logging provides Route/Elapsed automatically; or log them yourself.</p></div>
    </div>
  </section>
</template>
