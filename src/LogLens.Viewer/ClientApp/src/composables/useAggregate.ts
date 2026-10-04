import { ref, type Ref } from 'vue'
import { api } from '@/lib/api'
import { query } from '@/state/query'
import type { AggregateResponse } from '@/lib/types'

export interface AggregateStore {
  data: Ref<AggregateResponse | null>
  error: Ref<string>
  loading: Ref<boolean>
  load(by: string | null, duration?: string | null, extraQ?: string): Promise<void>
}

/** Aggregate fetch shared by the dashboard pages. */
export function useAggregate(): AggregateStore {
  const data = ref<AggregateResponse | null>(null)
  const error = ref('')
  const loading = ref(false)
  let ctrl: AbortController | null = null
  async function load(by: string | null, duration?: string | null, extraQ = '') {
    if (!by) { data.value = null; return }
    ctrl?.abort()
    ctrl = new AbortController()
    loading.value = true
    const p = new URLSearchParams({ by, q: [query.q, extraQ].filter(Boolean).join(' '), limit: '100', buckets: '12' })
    if (duration) p.set('duration', duration)
    if (query.from) p.set('from', query.from)
    if (query.to) p.set('to', query.to)
    try {
      data.value = await api<AggregateResponse>('/api/aggregate?' + p, { signal: ctrl.signal })
      error.value = ''
    } catch (e) {
      if ((e as Error).name !== 'AbortError') error.value = (e as Error).message
    } finally {
      loading.value = false
    }
  }
  return { data, error, loading, load }
}
