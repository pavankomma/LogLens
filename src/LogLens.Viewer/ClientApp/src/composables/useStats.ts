import { onBeforeUnmount, onMounted, ref, type Ref } from 'vue'
import { api } from '@/lib/api'
import { STATS_MS } from '@/lib/constants'
import type { Stats } from '@/lib/types'

export function useStats(): { stats: Ref<Stats | null>; rate: Ref<number>; poll(): Promise<void> } {
  const stats = ref<Stats | null>(null)
  const rate = ref(0)
  let sample: { ingested: number; at: number } | null = null
  let timer: ReturnType<typeof setInterval> | undefined
  async function poll() {
    try {
      const s = await api<Stats>('/api/stats')
      const now = performance.now()
      if (sample) rate.value = Math.max(0, (s.ingested - sample.ingested) / ((now - sample.at) / 1000))
      sample = { ingested: s.ingested, at: now }
      stats.value = s
    } catch { /* stats are cosmetic; retry next tick */ }
  }
  onMounted(() => { poll(); timer = setInterval(poll, STATS_MS) })
  onBeforeUnmount(() => clearInterval(timer))
  return { stats, rate, poll }
}
