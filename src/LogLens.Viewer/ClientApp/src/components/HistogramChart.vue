<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { fmtTime } from '@/lib/format'
import { query } from '@/state/query'
import type { HistogramBucket, TimeRange } from '@/lib/types'

interface HistogramGroup { key: string; color: string; trend: number[]; count: number }
interface BarSeg { y: number; h: number; cls?: string; fill?: string }
interface Bar { x: number; w: number; segs: BarSeg[]; title: string }

const props = defineProps<{
  buckets?: HistogramBucket[] | null
  range?: TimeRange | null
  groups?: HistogramGroup[] | null
}>()

const W = 1000, H = 50
const bars = computed<Bar[]>(() => {
  // Grouped mode: stack per-bucket counts of each group (from /api/aggregate trends).
  if (props.groups?.length) {
    const groups = props.groups
    const n = Math.max(...groups.map(g => g.trend.length))
    const totals = Array.from({ length: n }, (_, i) => groups.reduce((a, g) => a + (g.trend[i] || 0), 0))
    const max = Math.max(1, ...totals)
    const bw = W / Math.max(1, n)
    const from = props.range ? new Date(props.range.from).getTime() : 0
    const span = props.range ? new Date(props.range.to).getTime() - new Date(props.range.from).getTime() : 0
    return totals.map((tot, i) => {
      const h = tot ? Math.max(2, (tot / max) * (H - 4)) : 0
      let y = H
      const segs = groups.filter(g => g.trend[i]).map(g => { const sh = h * (g.trend[i]! / tot); y -= sh; return { y, h: sh, fill: g.color } })
      const top = groups.map(g => [g.key, g.trend[i] || 0] as const).filter(([, c]) => c).sort((a, b) => b[1] - a[1]).slice(0, 3).map(([k, c]) => `${k} ${c}`).join(', ')
      return { x: i * bw + 0.5, w: Math.max(1, bw - 1), segs, title: `${props.range ? fmtTime(new Date(from + (i + 0.5) * span / n).toISOString()) + ' — ' : ''}${tot}${top ? ` (${top})` : ''}` }
    })
  }
  const b = props.buckets || []
  const max = Math.max(1, ...b.map(x => x.count))
  const bw = W / Math.max(1, b.length)
  const scale = (c: number) => c ? Math.max(2, (c / max) * (H - 4)) : 0
  return b.map((x, i) => {
    const err = x.errors || 0, warn = x.warnings || 0
    const base = scale(x.count - err - warn), wh = scale(warn), eh = scale(err)
    return {
      x: i * bw + 0.5, w: Math.max(1, bw - 1), title: `${fmtTime(x.t)} — ${x.count}${err ? `, ${err} error` : ''}${warn ? `, ${warn} warning` : ''}`,
      segs: [{ y: H - base, h: base, cls: 'f-base' }, { y: H - base - wh, h: wh, cls: 'f-warn' }, { y: H - base - wh - eh, h: eh, cls: 'f-err' }],
    }
  })
})
const svg = ref<SVGSVGElement | null>(null)
const brush = reactive<{ start: number | null; x: number; w: number }>({ start: null, x: 0, w: 0 })
const px = (ev: PointerEvent) => { const r = svg.value!.getBoundingClientRect(); return Math.min(W, Math.max(0, ((ev.clientX - r.left) / r.width) * W)) }
const x2t = (x: number) => new Date(new Date(props.range!.from).getTime() + (x / W) * (new Date(props.range!.to).getTime() - new Date(props.range!.from).getTime()))
const down = (ev: PointerEvent) => { svg.value!.setPointerCapture(ev.pointerId); brush.start = px(ev); brush.x = brush.start; brush.w = 0 }
const move = (ev: PointerEvent) => { if (brush.start === null) return; const x = px(ev); brush.x = Math.min(brush.start, x); brush.w = Math.abs(x - brush.start) }
const up = (ev: PointerEvent) => {
  if (brush.start === null) return
  const x = px(ev)
  if (Math.abs(x - brush.start) > 5 && props.range) {
    query.from = x2t(Math.min(brush.start, x)).toISOString()
    query.to = x2t(Math.max(brush.start, x)).toISOString()
  }
  brush.start = null
}
const label = computed(() => props.range ? `Entries over time from ${fmtTime(props.range.from)} to ${fmtTime(props.range.to)}; drag to zoom` : 'Entries over time')
</script>

<template>
  <div class="histwrap">
    <svg ref="svg" class="histogram" role="img" :aria-label="label" preserveAspectRatio="none" :viewBox="'0 0 ' + W + ' ' + H" @pointerdown="down" @pointermove="move" @pointerup="up" @pointercancel="brush.start = null">
      <g v-for="(b, i) in bars" :key="i"><title>{{ b.title }}</title>
        <rect v-for="(s, j) in b.segs" :key="j" :class="s.cls" :style="s.fill ? { fill: s.fill, opacity: .85 } : undefined" :x="b.x" :y="s.y" :width="b.w" :height="s.h"></rect>
      </g>
      <rect v-if="brush.start !== null" class="brush" :x="brush.x" y="0" :width="brush.w" :height="H"></rect>
    </svg>
  </div>
</template>
