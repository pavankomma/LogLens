<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import KeyPicker from '@/components/KeyPicker.vue'
import { useAggregate } from '@/composables/useAggregate'
import { fmtAgo } from '@/lib/format'
import { query } from '@/state/query'
import { detectKey, durationKeys, identityKeys } from '@/state/profile'
import type { Facets } from '@/lib/types'

/** Users dashboard: who is doing what, who is having a bad time. */
const props = defineProps<{ facets?: Facets | null }>()
defineEmits<{ drill: [term: string] }>()

const agg = useAggregate()
const allOptions = computed(() => {
  const found = identityKeys().filter(k => props.facets?.properties?.[k])
  return found.length ? found : identityKeys().slice(0, 1)
})
const key = ref('')
watch(allOptions, o => { if (!key.value || !o.includes(key.value)) key.value = o[0] || '' }, { immediate: true })
const reload = () => agg.load(key.value, detectKey(props.facets, durationKeys()) || durationKeys()[0])
watch([key, () => query.q, () => query.from, () => query.to], reload, { immediate: true })
const maxCount = computed(() => Math.max(1, ...(agg.data.value?.groups || []).map(g => g.count)))
</script>

<template>
  <section class="panel dash" aria-label="Users">
    <div class="dash-head">
      <h2>Users</h2>
      <span class="dim" v-if="agg.data.value">{{ agg.data.value.distinctKeys.toLocaleString() }} distinct · {{ agg.data.value.total.toLocaleString() }} entries<template v-if="agg.data.value.unattributed"> · {{ agg.data.value.unattributed.toLocaleString() }} without {{ key }}</template></span>
      <span class="spacer"></span>
      <KeyPicker label="Identify by" v-model="key" :options="allOptions" />
    </div>
    <div class="banner error" v-if="agg.error.value" role="alert"><span>{{ agg.error.value }}</span><button class="link" @click="reload">Retry</button></div>
    <div class="grid">
      <table class="dg" aria-label="Users">
        <colgroup><col style="width:180px"><col style="width:110px"><col style="width:100px"><col style="width:110px"><col><col style="width:170px"></colgroup>
        <thead><tr><th scope="col">{{ key }}</th><th scope="col">Entries</th><th scope="col">Errors</th><th scope="col">Last seen</th><th scope="col">Top activity</th><th scope="col">Error rate</th></tr></thead>
        <tbody>
          <tr v-for="g in (agg.data.value?.groups || [])" :key="g.key" tabindex="0" @click="$emit('drill', key + '=' + (g.key.includes(' ') ? '&quot;' + g.key + '&quot;' : g.key))" @keydown.enter.prevent="$emit('drill', key + '=' + g.key)">
            <td><span class="res"><span class="swatch" :style="{ background: 'var(--brand)' }"></span><span class="v">{{ g.key }}</span></span></td>
            <td class="num">{{ g.count.toLocaleString() }}<span class="minibar" :style="{ width: (g.count / maxCount * 60) + 'px' }"></span></td>
            <td class="num" :class="{ bad: g.errors }">{{ g.errors.toLocaleString() }}</td>
            <td class="ts">{{ fmtAgo(g.last) }}</td>
            <td class="cat" :title="g.top.join(' · ')">{{ g.top.join(' · ') }}</td>
            <td><span class="ratebar" :class="g.count && g.errors / g.count > .03 ? 'bad' : g.errors ? 'warn' : 'ok'"><span :style="{ width: Math.min(100, g.count ? g.errors / g.count * 1000 : 0) + '%' }"></span></span><span class="num dim"> {{ g.count ? (g.errors / g.count * 100).toFixed(1) : '0.0' }} %</span></td>
          </tr>
        </tbody>
      </table>
      <div class="empty" v-if="agg.data.value && !agg.data.value.groups.length"><h3>No entries carry a {{ key }} property</h3><p class="dim">Add one with a scope: <code>Log.BeginScope("{{ key }}", value)</code></p></div>
    </div>
  </section>
</template>
