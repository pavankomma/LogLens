<script setup lang="ts">
import type { Row } from '@/lib/types'

defineProps<{
  row: Row
  selected: boolean
  tabbable: boolean
  cols: Record<string, boolean>
  utc: boolean
}>()
defineEmits<{ select: []; chain: [] }>()
</script>

<template>
  <tr :class="{ selected }" :tabindex="tabbable ? 0 : -1" role="row" :aria-selected="selected" @click="$emit('select')" @keydown.enter.stop.prevent="$emit('select')">
    <td v-if="cols.resource"><span class="res"><span class="swatch" :style="{ background: row.color }"></span><span class="v">{{ row.resource }}</span></span></td>
    <td v-if="cols.level"><span class="lvl" :class="'lvl-' + row.e.level">{{ row.e.level }}</span></td>
    <td v-if="cols.timestamp" class="ts">{{ utc ? row.timeUtc : row.time }}</td>
    <td v-if="cols.category" class="cat c-cat">{{ row.e.category }}</td>
    <td class="msg">{{ row.e.message }}<span v-if="row.e.exception" class="exc-flag">⚠ exception</span></td>
    <td v-if="cols.trace" class="c-trace"><button v-if="row.trace" class="trace" :title="row.trace.key + ' = ' + row.trace.value + ' — show this chain'" @click.stop="$emit('chain')">{{ row.trace.value.length > 9 ? row.trace.value.slice(0, 8) + '…' : row.trace.value }}</button></td>
    <td v-if="cols.details"><button class="view" @click.stop="$emit('select')">View</button></td>
  </tr>
</template>
