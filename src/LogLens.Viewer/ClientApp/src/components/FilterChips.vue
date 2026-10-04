<script setup lang="ts">
import { computed } from 'vue'
import { RANGES } from '@/lib/constants'
import { fmtTime } from '@/lib/format'
import { chips, query, removeTerm } from '@/state/query'

const timeChip = computed(() => {
  if (!query.from && !query.to) return null
  if (!query.to && query.from.startsWith('-')) return (RANGES.find(r => r[0] === query.from) || [])[1] || query.from
  return `${query.from ? fmtTime(query.from) : '…'} → ${query.to ? fmtTime(query.to) : 'now'}`
})
const clearTime = () => { query.from = ''; query.to = '' }
const clearAll = () => { query.q = ''; clearTime() }
</script>

<template>
  <div class="chips" v-if="chips.length || timeChip">
    <span class="lbl">Showing</span>
    <span class="chip time" v-if="timeChip"><b>Time</b> <span class="op">{{ timeChip }}</span><button aria-label="Clear time range" @click="clearTime">×</button></span>
    <span class="chip" :class="{ neg: c.neg }" v-for="c in chips" :key="c.t" :title="c.t">
      <template v-if="c.field"><b>{{ c.field }}</b> <span class="op">{{ c.op }}</span> {{ c.value }}</template>
      <template v-else><span class="op">{{ c.op }}</span> "{{ c.value }}"</template>
      <button :aria-label="'Remove filter: ' + c.text" @click="removeTerm(c.t)">×</button>
    </span>
    <button class="link" v-if="chips.length + (timeChip ? 1 : 0) > 1" @click="clearAll">Clear filters</button>
  </div>
</template>
