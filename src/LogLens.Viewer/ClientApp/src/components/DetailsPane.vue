<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { quote, resourceOf, resourceTerm } from '@/lib/query-dsl.js'
import { fmtTime, highlightTemplate, resourceColor } from '@/lib/format'
import { activeTerms, toggleTerm } from '@/state/query'
import { entityLabel, entityLink, traceOfP } from '@/state/profile'
import type { LogEvent } from '@/lib/types'

const props = defineProps<{ event: LogEvent; index: number; count: number }>()
defineEmits<{ close: []; prev: []; next: []; chain: [key: string, value: string] }>()

const showRaw = ref(false)
const keys = computed(() => Object.keys(props.event.properties || {}).sort())
const trace = computed(() => traceOfP(props.event))
const res = computed(() => resourceOf(props.event))
const utc = computed(() => new Date(props.event.timestamp).toISOString())
const copy = (v: unknown) => navigator.clipboard?.writeText(String(v))
const permalink = () => { const u = new URL(location.href); u.hash = 'seq=' + props.event.seq; copy(u.toString()) }
const heading = ref<HTMLElement | null>(null)
onMounted(() => heading.value?.focus())
</script>

<template>
  <aside class="panel details" aria-label="Log details">
    <div class="dh">
      <h2 ref="heading" tabindex="-1">Log details</h2>
      <span class="dim" v-if="count">{{ index + 1 }} of {{ count }}</span>
      <div class="grp">
        <button class="icon-btn" aria-label="Previous entry" title="Previous (k)" :disabled="index <= 0" @click="$emit('prev')">↑</button>
        <button class="icon-btn" aria-label="Next entry" title="Next (j)" :disabled="index >= count - 1" @click="$emit('next')">↓</button>
        <button class="icon-btn" aria-label="Close details" title="Close (Esc)" @click="$emit('close')">✕</button>
      </div>
    </div>
    <div class="sec">
      <table class="kv">
        <tr><th scope="row">Resource</th><td><span class="res"><span class="swatch" :style="{ background: resourceColor(res.value) }"></span> {{ res.value }}</span><span class="acts"><button @click="toggleTerm(resourceTerm(res.key, res.value))">filter</button></span></td></tr>
        <tr><th scope="row">Level</th><td><span class="lvl" :class="'lvl-' + event.level">{{ event.level }}</span></td></tr>
        <tr><th scope="row">Timestamp</th><td>{{ fmtTime(event.timestamp) }} <span class="dim">local</span><br><span class="dim">{{ utc }}</span></td></tr>
        <tr v-if="event.category"><th scope="row">Category</th><td>{{ event.category }}<span class="acts"><button @click="toggleTerm('category:' + quote(event.category))">filter</button><button @click="copy(event.category)">copy</button></span></td></tr>
        <tr v-if="trace"><th scope="row">{{ trace.key }}</th><td>{{ trace.value }}<span class="acts"><button @click="$emit('chain', trace.key, trace.value)">chain</button><button @click="copy(trace.value)">copy</button></span></td></tr>
        <tr v-if="event.source"><th scope="row">Source</th><td>{{ event.source }}</td></tr>
      </table>
    </div>
    <div class="sec">
      <h3>Message</h3>
      <div class="message">{{ event.message }}</div>
      <div class="tpl" v-if="event.template && event.template !== event.message" v-html="highlightTemplate(event.template)"></div>
    </div>
    <div class="sec" v-if="keys.length">
      <h3>Properties</h3>
      <table class="kv">
        <tr v-for="k in keys" :key="k">
          <th scope="row" :title="k">{{ entityLabel(k) }}</th>
          <td>{{ event.properties![k] }}<a v-if="entityLink(k, event.properties![k]!)" class="entlink" :href="entityLink(k, event.properties![k]!)!" target="_blank" rel="noopener">Open ↗</a><span class="acts">
            <button :aria-label="'Filter ' + k + ' is ' + event.properties![k]" @click="toggleTerm(k + '=' + quote(event.properties![k]))">{{ activeTerms.has(k + '=' + quote(event.properties![k])) ? 'remove' : '=' }}</button>
            <button :aria-label="'Exclude ' + k + ' is ' + event.properties![k]" @click="toggleTerm('-' + k + '=' + quote(event.properties![k]))">≠</button>
            <button aria-label="Copy value" @click="copy(event.properties![k])">copy</button></span></td>
        </tr>
      </table>
    </div>
    <div class="sec" v-if="event.exception"><h3>Exception</h3><pre class="exc">{{ event.exception }}</pre></div>
    <div class="acts-row">
      <button class="link" @click="permalink">Copy link</button>
      <button class="link" @click="copy(JSON.stringify(event, null, 2))">Copy JSON</button>
      <button class="link" @click="showRaw = !showRaw">{{ showRaw ? 'Hide raw' : 'Show raw' }}</button>
    </div>
    <div class="sec" v-if="showRaw"><pre class="raw">{{ JSON.stringify(event, null, 2) }}</pre></div>
  </aside>
</template>
