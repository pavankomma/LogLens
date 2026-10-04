<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { LEVELS, describeTerm, quote, tokenize, unq } from '@/lib/query-dsl.js'
import { query } from '@/state/query'
import type { Facets } from '@/lib/types'

const props = defineProps<{ facets?: Facets | null }>()
defineEmits<{ help: [] }>()

interface Suggestion { k: string; d: string; head: string; recent?: boolean; partial?: boolean }

/** Recent searches (client-side, most recent first). */
const loadRecent = (): string[] => {
  try {
    const l = JSON.parse(localStorage.getItem('recentQ') || 'null') as unknown
    return Array.isArray(l) ? l.filter((x): x is string => typeof x === 'string') : []
  } catch { return [] }
}
const rememberRecent = (q: string): string[] => {
  const list = [q, ...loadRecent().filter(x => x !== q)].slice(0, 8)
  try { localStorage.setItem('recentQ', JSON.stringify(list)) } catch { /* storage unavailable */ }
  return list
}

const draft = ref(query.q)
watch(() => query.q, v => draft.value = v)
const open = ref(false)
const cursor = ref(0)
const recent = ref(loadRecent())
const fieldNames = computed(() => ['Level', 'Category', ...Object.keys(props.facets?.properties || {})])
const valuesFor = (key: string): string[] =>
  key === 'Level' ? LEVELS : key === 'Category' ? Object.keys(props.facets?.categories || {}) : Object.keys(props.facets?.properties?.[key] || {})
const suggestions = computed<Suggestion[]>(() => {
  const toks = tokenize(draft.value)
  if (!toks.length || /\s$/.test(draft.value)) return draft.value.trim() ? [] : recent.value.filter(q => q !== query.q).map(q => ({ k: q, d: 'recent', head: '', recent: true }))
  const last = toks[toks.length - 1]!
  const head = draft.value.slice(0, draft.value.length - last.length)
  const out: Suggestion[] = []
  let m: RegExpExecArray | null
  if ((m = /^(-?)([A-Za-z_][\w.]*)(=|!=|~|:)("?)(.*)$/.exec(last))) {
    const keyRaw = m[2]!, lk = keyRaw.toLowerCase()
    const key = lk === 'level' ? 'Level' : lk === 'category' ? 'Category' : keyRaw
    const typed = unq(m[5]!).toLowerCase()
    for (const v of valuesFor(key).filter(v => v.toLowerCase().startsWith(typed)).slice(0, 8)) {
      const term = key === 'Level' ? `${m[1]}level:${v.toLowerCase()}` : key === 'Category' ? `${m[1]}category:${quote(v)}` : `${m[1]}${keyRaw}${m[3]}${quote(v)}`
      out.push({ k: term, d: describeTerm(term).text, head })
    }
  } else {
    const neg = last[0] === '-' ? '-' : ''
    const q = last.replace(/^-/, '').toLowerCase()
    if (q.length) {
      for (const f of fieldNames.value.filter(f => f.toLowerCase().startsWith(q)).slice(0, 5)) {
        const dsl = f === 'Level' ? 'level:' : f === 'Category' ? 'category:' : f + '='
        out.push({ k: neg + dsl, d: `filter by ${f}…`, head, partial: true })
      }
      for (const l of LEVELS.filter(l => l.toLowerCase().startsWith(q))) {
        const term = neg + 'level:' + l.toLowerCase() + '+'
        out.push({ k: term, d: describeTerm(term).text, head })
      }
    }
  }
  return out
})
watch(suggestions, () => cursor.value = 0)
const input = ref<HTMLInputElement | null>(null)
function accept(sg: Suggestion) {
  if (sg.recent) { draft.value = sg.k; query.q = sg.k; recent.value = rememberRecent(sg.k); open.value = false; return }
  draft.value = sg.head + sg.k + (sg.partial ? '' : ' ')
  if (!sg.partial) query.q = draft.value.trim()
  open.value = !!sg.partial
  nextTick(() => input.value?.focus())
}
function onKey(ev: KeyboardEvent) {
  if (!open.value || !suggestions.value.length) return
  if (ev.key === 'ArrowDown') { ev.preventDefault(); cursor.value = (cursor.value + 1) % suggestions.value.length }
  else if (ev.key === 'ArrowUp') { ev.preventDefault(); cursor.value = (cursor.value - 1 + suggestions.value.length) % suggestions.value.length }
  else if (ev.key === 'Tab' || ev.key === 'Enter') { ev.preventDefault(); accept(suggestions.value[cursor.value]!) }
}
const onBlur = (ev: FocusEvent) => {
  const rel = ev.relatedTarget as Element | null
  if (!rel || !rel.closest?.('.suggest')) open.value = false
}
const submit = () => { open.value = false; query.q = draft.value.trim(); if (query.q) recent.value = rememberRecent(query.q) }
const onEsc = (ev: Event) => { open.value = false; (ev.target as HTMLElement).blur() }
</script>

<template>
  <form class="field search" autocomplete="off" role="search" @submit.prevent="submit">
    <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>
    <input ref="input" id="q" v-model="draft" type="text" spellcheck="false" aria-label="Search log entries"
           role="combobox" :aria-expanded="open && suggestions.length > 0" aria-controls="suggest-list" aria-autocomplete="list"
           :aria-activedescendant="open && suggestions.length ? 'sg-' + cursor : undefined"
           placeholder="Search messages, or start typing a field name…"
           @focus="open = true" @input="open = true" @blur="onBlur" @keydown="onKey" @keydown.esc="onEsc">
    <button type="button" class="help" title="Search tips" aria-label="Search tips" @click="$emit('help')">?</button>
    <ul id="suggest-list" class="suggest" v-if="open && suggestions.length" role="listbox" tabindex="-1">
      <li class="hdr" v-if="suggestions[0]!.recent" aria-hidden="true">Recent searches</li>
      <li v-for="(sg, i) in suggestions" :key="i + sg.k" :id="'sg-' + i" :class="{ on: i === cursor }" role="option" :aria-selected="i === cursor" @mousedown.prevent="accept(sg)"><span class="k">{{ sg.k }}</span><span class="d">{{ sg.d }}</span></li>
    </ul>
  </form>
</template>
