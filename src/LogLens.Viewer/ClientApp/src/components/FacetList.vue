<script setup lang="ts">
import { computed } from 'vue'
import { activeTerms, toggleTerm } from '@/state/query'

const props = defineProps<{
  title: string
  items?: Record<string, number> | null
  term: (label: string) => string
  color?: ((label: string) => string) | null
  type?: string
}>()

const entries = computed(() => Object.entries(props.items || {}).filter(([, n]) => n > 0))
const max = computed(() => Math.max(1, ...entries.value.map(([, n]) => n)))
</script>

<template>
  <section class="facet" v-if="entries.length">
    <h4><span class="ftype" v-if="type" :title="type === '#' ? 'numeric' : 'text'" aria-hidden="true">{{ type }}</span>{{ title }}</h4>
    <ul>
      <li v-for="[label, n] in entries" :key="label">
        <span class="bar" :style="{ width: (n / max * 100) + '%' }"></span>
        <button class="f" :aria-pressed="activeTerms.has(term(label))" :title="term(label)" @click="toggleTerm(term(label))">
          <span v-if="color" class="swatch" :style="{ background: color(label) }"></span><span class="v">{{ label }}</span>
        </button>
        <button class="x" :aria-label="'Exclude ' + label" title="Exclude" @click="toggleTerm('-' + term(label))">≠</button>
        <span class="n">{{ n.toLocaleString() }}</span>
      </li>
    </ul>
  </section>
</template>
