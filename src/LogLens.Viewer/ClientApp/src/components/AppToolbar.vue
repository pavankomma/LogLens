<script setup lang="ts">
import { computed } from 'vue'
import SearchBox from './SearchBox.vue'
import { LEVELS, resourceTerm, unq } from '@/lib/query-dsl.js'
import { RANGES } from '@/lib/constants'
import { toLocalInput } from '@/lib/format'
import { query, replaceTermPrefix, terms } from '@/state/query'
import type { Facets } from '@/lib/types'

const props = defineProps<{
  resources?: Record<string, number> | null
  resourceKey: string | null
  facets?: Facets | null
  paused: boolean
  buffered: number
}>()
defineEmits<{ help: []; pause: []; addfilter: [] }>()

const levelValue = computed({
  get: () => (terms.value.find(t => /^level:/i.test(t)) || '').replace(/^level:/i, '').toLowerCase(),
  set: v => replaceTermPrefix(/^level:/i, v ? 'level:' + v : ''),
})
const resourceRe = computed(() => props.resourceKey ? new RegExp('^' + props.resourceKey.replace('.', '\\.') + '=') : /^source:/)
const resourceValue = computed({
  get: () => {
    const t = terms.value.find(x => resourceRe.value.test(x))
    return t ? unq(t.slice(t.indexOf(props.resourceKey ? '=' : ':') + 1)) : ''
  },
  set: v => replaceTermPrefix(resourceRe.value, v ? resourceTerm(props.resourceKey, v) : ''),
})
const isCustom = computed(() => !!query.to || (!!query.from && !RANGES.some(r => r[0] === query.from)))
const rangeValue = computed({
  get: () => isCustom.value ? 'custom' : query.from,
  set: v => {
    if (v === 'custom') { query.from = new Date(Date.now() - 15 * 60000).toISOString(); query.to = new Date().toISOString() }
    else { query.from = v; query.to = '' }
  },
})
const fromLocal = computed({
  get: () => query.from && !query.from.startsWith('-') ? toLocalInput(query.from) : '',
  set: v => { if (v) query.from = new Date(v).toISOString() },
})
const toLocal = computed({
  get: () => query.to ? toLocalInput(query.to) : '',
  set: v => { if (v) query.to = new Date(v).toISOString() },
})
</script>

<template>
  <div class="toolbar">
    <div class="field" v-if="resources && Object.keys(resources).length > 1">
      <label for="resourceSel">Resource</label>
      <select id="resourceSel" v-model="resourceValue"><option value="">All</option><option v-for="r in Object.keys(resources)" :key="r" :value="r">{{ r }}</option></select>
    </div>
    <SearchBox :facets="facets" @help="$emit('help')" />
    <div class="field"><label for="levelSel">Level</label>
      <select id="levelSel" v-model="levelValue"><option value="">All</option>
        <optgroup label="At least"><option v-for="l in LEVELS" :key="l" :value="l.toLowerCase() + '+'">{{ l }} and above</option></optgroup>
        <optgroup label="Exactly"><option v-for="l in LEVELS" :key="'x' + l" :value="l.toLowerCase()">{{ l }} only</option></optgroup>
      </select>
    </div>
    <div class="field"><label for="timeSel">Time</label>
      <select id="timeSel" v-model="rangeValue"><option v-for="[v, label] in RANGES" :key="v" :value="v">{{ label }}</option><option value="custom">Custom…</option></select>
    </div>
    <div class="field" v-if="isCustom"><label for="fromIn">From</label><input id="fromIn" type="datetime-local" v-model="fromLocal"><label for="toIn">to</label><input id="toIn" type="datetime-local" v-model="toLocal"></div>
    <button class="btn" @click="$emit('addfilter')">+ Add filter</button>
    <div class="seg" role="group" aria-label="Mode">
      <button :class="{ on: !query.live }" :aria-pressed="!query.live" @click="query.live = false" title="Browse what is retained; nothing moves">Explore</button>
      <button :class="{ on: query.live, live: query.live && !paused }" :aria-pressed="query.live" @click="query.live = true" :disabled="!!query.to" :title="query.to ? 'Live is unavailable while viewing a past range' : 'Follow new entries as they arrive (l)'"><span class="dot"></span>Live</button>
    </div>
    <button v-if="query.live" class="btn" @click="$emit('pause')">{{ paused ? (buffered ? 'Resume (' + buffered.toLocaleString() + ' new)' : 'Resume') : 'Pause' }}</button>
  </div>
</template>
