<script setup lang="ts">
defineProps<{ label: string; modelValue: string; options: string[] }>()
defineEmits<{ 'update:modelValue': [value: string] }>()
</script>

<template>
  <div class="field" v-if="options.length > 1">
    <label>{{ label }}</label>
    <select :value="modelValue" @change="$emit('update:modelValue', ($event.target as HTMLSelectElement).value)"><option v-for="o in options" :key="o" :value="o">{{ o }}</option></select>
  </div>
</template>
