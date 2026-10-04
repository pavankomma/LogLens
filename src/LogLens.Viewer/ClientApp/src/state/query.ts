import { computed, reactive } from 'vue'
import { describeTerm, join, tokenize } from '@/lib/query-dsl.js'

export type ViewName = 'logs' | 'overview' | 'users' | 'requests' | 'chain'

/** Shared query state — the URL hash is the source of truth. */
export const query = reactive({
  q: '',
  from: '',
  to: '',
  live: false,
  seq: null as number | null,
  view: 'logs' as ViewName,
  ck: '',
  cv: '',
})

export function readHash(): void {
  const p = new URLSearchParams(location.hash.slice(1))
  query.q = p.get('q') || ''
  query.from = p.get('from') || ''
  query.to = p.get('to') || ''
  query.live = p.get('live') === '1'
  query.seq = p.get('seq') ? Number(p.get('seq')) : null
  const v = p.get('view')
  query.view = v === 'logs' || v === 'overview' || v === 'users' || v === 'requests' || v === 'chain' ? v : 'logs'
  query.ck = p.get('ck') || ''
  query.cv = p.get('cv') || ''
}

export function writeHash(push = false): void {
  const p = new URLSearchParams()
  if (query.view !== 'logs') p.set('view', query.view)
  if (query.view === 'chain') { p.set('ck', query.ck); p.set('cv', query.cv) }
  if (query.q) p.set('q', query.q)
  if (query.from) p.set('from', query.from)
  if (query.to) p.set('to', query.to)
  if (query.live) p.set('live', '1')
  const h = p.toString()
  const url = h ? '#' + h : location.pathname + location.search
  if (url === location.hash || (!h && !location.hash)) return
  ;(push ? history.pushState : history.replaceState).call(history, null, '', url)
}

export const terms = computed(() => tokenize(query.q))
export const activeTerms = computed(() => new Set(terms.value))
export const setTerms = (list: string[]): void => { query.q = join(list) }
export const toggleTerm = (term: string): void => {
  const t = [...terms.value]
  const i = t.indexOf(term)
  if (i >= 0) t.splice(i, 1); else t.push(term)
  setTerms(t)
}
export const removeTerm = (term: string): void => setTerms(terms.value.filter(t => t !== term))
export const replaceTermPrefix = (re: RegExp, term: string): void => {
  const t = terms.value.filter(x => !re.test(x))
  if (term) t.push(term)
  setTerms(t)
}
export const chips = computed(() => terms.value.map(t => ({ t, ...describeTerm(t) })))
