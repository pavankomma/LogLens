<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

/** Native <dialog> wrapper: modality, Esc, focus trap; restores focus on close. */
defineProps<{ label?: string }>()
const emit = defineEmits<{ close: [] }>()

const el = ref<HTMLDialogElement | null>(null)
let opener: Element | null = null
onMounted(() => {
  opener = document.activeElement
  el.value!.showModal()
  el.value!.querySelector<HTMLElement>('[autofocus], input, select, button')?.focus()
})
onBeforeUnmount(() => {
  try { el.value?.close() } catch { /* already closed */ }
  ;(opener as HTMLElement | null)?.focus?.()
})
const onCancel = (e: Event) => { e.preventDefault(); emit('close') }
const onClick = (e: MouseEvent) => { if (e.target === el.value) emit('close') }
</script>

<template>
  <dialog ref="el" class="dialog" :aria-label="label" @cancel="onCancel" @click="onClick"><div class="dialog-body"><slot></slot></div></dialog>
</template>
