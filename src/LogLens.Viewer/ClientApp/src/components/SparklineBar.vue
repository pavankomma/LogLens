<script setup lang="ts">
/** Tiny inline bar sparkline. */
defineProps<{ values: number[]; hot?: boolean[] | null }>()
</script>

<template>
  <span class="spark" aria-hidden="true">
    <span v-for="(v, i) in values" :key="i" class="b" :class="{ hot: hot && hot[i] }" :style="{ height: Math.max(8, v * 100) + '%' }"></span>
  </span>
</template>
