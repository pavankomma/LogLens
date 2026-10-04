<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import FacetList from './FacetList.vue'
import { LEVELS, RESOURCE_KEYS, quote, resourceTerm } from '@/lib/query-dsl.js'
import { LEVEL_VAR } from '@/lib/constants'
import { resourceColor } from '@/lib/format'
import { profile } from '@/state/profile'
import type { Facets } from '@/lib/types'

const props = defineProps<{
  facets?: Facets | null
  resources?: Record<string, number> | null
  resourceKey: string | null
}>()

const collapsed = ref(false)
try { collapsed.value = localStorage.getItem('filters') === '0' } catch { /* storage unavailable */ }
watch(collapsed, v => { try { localStorage.setItem('filters', v ? '0' : '1') } catch { /* storage unavailable */ } })
const levels = computed(() => Object.fromEntries(LEVELS.map(l => [l, props.facets?.levels?.[l] || 0])))
// System/internal keys (correlation ids and friends) sit in a collapsed group so domain fields stay in front.
const SYSTEM_RE = /^(traceid|spanid|parentspanid|requestid|correlationid|eventid|sourcecontext)$/i
const isSystem = (k: string) => SYSTEM_RE.test(k) || profile.keys.correlation.some(c => c.toLowerCase() === k.toLowerCase())
const all = computed(() => Object.entries(props.facets?.properties || {}).filter(([k]) => !RESOURCE_KEYS.includes(k)))
const props2 = computed(() => Object.fromEntries(all.value.filter(([k]) => !isSystem(k))))
const sysProps = computed(() => Object.fromEntries(all.value.filter(([k]) => isSystem(k))))
const typeOf = (values: Record<string, number>) => {
  const keys = Object.keys(values || {})
  return keys.length && keys.every(v => v.trim() !== '' && !Number.isNaN(Number(v))) ? '#' : 'T'
}
</script>

<template>
  <aside class="panel filters" :class="{ collapsed }" aria-label="Filters">
    <div class="ph">
      <span v-if="!collapsed">Filters</span>
      <button class="icon-btn" :aria-label="collapsed ? 'Show filters' : 'Hide filters'" :aria-expanded="!collapsed" @click="collapsed = !collapsed">{{ collapsed ? '»' : '«' }}</button>
    </div>
    <template v-if="!collapsed">
      <FacetList title="Level" :items="levels" :term="l => 'level:' + l.toLowerCase()" :color="l => LEVEL_VAR[l]!" />
      <FacetList title="Resource" :items="resources" :term="r => resourceTerm(resourceKey, r)" :color="resourceColor" />
      <FacetList title="Category" :items="facets?.categories" :term="c => 'category:' + quote(c)" />
      <FacetList v-for="(values, key) in props2" :key="key" :title="String(key)" :type="typeOf(values)" :items="values" :term="v => key + '=' + quote(v)" />
      <details class="sysgroup" v-if="Object.keys(sysProps).length">
        <summary>System fields</summary>
        <FacetList v-for="(values, key) in sysProps" :key="key" :title="String(key)" :type="typeOf(values)" :items="values" :term="v => key + '=' + quote(v)" />
      </details>
    </template>
  </aside>
</template>
