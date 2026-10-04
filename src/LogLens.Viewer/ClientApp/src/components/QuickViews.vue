<script setup lang="ts">
import { query } from '@/state/query'
import { profile } from '@/state/profile'
import type { QuickView } from '@/lib/types'

const apply = (v: QuickView) => { query.q = v.query; if (v.from) { query.from = v.from; query.to = '' } }
const active = (v: QuickView) => query.q === v.query
</script>

<template>
  <div class="chips quick" v-if="profile.quickViews.length">
    <span class="lbl">{{ profile.name || 'Views' }}</span>
    <button class="chip qv" :class="{ on: active(v) }" v-for="v in profile.quickViews" :key="v.name" @click="apply(v)">{{ v.name }}</button>
  </div>
</template>
