import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import type { LogEvent, Row } from '@/lib/types'

export interface Selection {
  selectedSeq: Ref<number | null>
  open: Ref<boolean>
  index: ComputedRef<number>
  current: ComputedRef<LogEvent | null>
}

/** Selection tracked by seq so live inserts never move it. */
export function useSelection(rows: Ref<Row[]>): Selection {
  const selectedSeq = ref<number | null>(null)
  const open = ref(false)
  const index = computed(() => selectedSeq.value == null ? -1 : rows.value.findIndex(r => r.seq === selectedSeq.value))
  const current = computed(() => index.value >= 0 ? rows.value[index.value]!.e : null)
  watch(rows, () => { if (selectedSeq.value != null && index.value < 0) { selectedSeq.value = null; open.value = false } })
  return { selectedSeq, open, index, current }
}
