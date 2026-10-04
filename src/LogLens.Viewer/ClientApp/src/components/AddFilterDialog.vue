<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import ModalDialog from './ModalDialog.vue'
import { LEVELS, RESOURCE_KEYS, buildTerm, describeTerm } from '@/lib/query-dsl.js'
import { toggleTerm } from '@/state/query'
import type { Facets } from '@/lib/types'

const props = defineProps<{
  facets?: Facets | null
  resources?: Record<string, number> | null
  resourceKey: string | null
}>()
const emit = defineEmits<{ close: [] }>()

const fields = computed(() => ['Level', 'Resource', 'Category', 'Message', 'Exception', ...Object.keys(props.facets?.properties || {}).filter(k => !RESOURCE_KEYS.includes(k))])
const field = ref('Level')
const ops = computed(() =>
  field.value === 'Level' ? ['is at least', 'is', 'is not']
    : field.value === 'Exception' ? ['is present', 'contains', 'does not contain']
      : field.value === 'Message' ? ['contains', 'does not contain']
        : field.value === 'Resource' ? ['is', 'is not', 'contains']
          : ['is', 'is not', 'contains', 'does not contain', 'is present', '>', '<', '≥', '≤'])
const op = ref('is at least')
const values = computed(() =>
  field.value === 'Level' ? LEVELS
    : field.value === 'Resource' ? Object.keys(props.resources || {})
      : field.value === 'Category' ? Object.keys(props.facets?.categories || {})
        : Object.keys(props.facets?.properties?.[field.value] || {}))
const value = ref('Warning')
watch(field, () => { op.value = ops.value[0]!; value.value = field.value === 'Level' ? 'Warning' : (values.value[0] || '') })
const term = computed(() => (op.value === 'is present' || value.value) ? buildTerm(field.value, op.value, value.value, props.resourceKey) : '')
const preview = computed(() => term.value ? describeTerm(term.value).text : '')
const apply = () => { if (term.value) { toggleTerm(term.value); emit('close') } }
</script>

<template>
  <ModalDialog label="Add a filter" @close="$emit('close')">
    <form @submit.prevent="apply">
      <h3>Add a filter</h3>
      <div class="row">
        <div class="field"><label class="sr" for="f-field">Field</label><select id="f-field" v-model="field" autofocus><option v-for="f in fields" :key="f" :value="f">{{ f }}</option></select></div>
        <div class="field"><label class="sr" for="f-op">Condition</label><select id="f-op" v-model="op"><option v-for="o in ops" :key="o" :value="o">{{ o }}</option></select></div>
        <div class="field" v-if="op !== 'is present'"><label class="sr" for="f-val">Value</label><input id="f-val" v-model="value" list="filter-values" placeholder="value"><datalist id="filter-values"><option v-for="v in values" :key="v" :value="v"></option></datalist></div>
      </div>
      <div class="preview" v-if="term">Adds: <b>{{ preview }}</b> <span class="dim">({{ term }})</span></div>
      <div class="actions"><button type="button" class="btn" @click="$emit('close')">Cancel</button><button type="submit" class="btn primary" :disabled="!term">Add filter</button></div>
    </form>
  </ModalDialog>
</template>
