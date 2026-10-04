<script setup lang="ts">
import { query, type ViewName } from '@/state/query'

const go = (v: ViewName) => query.view = v
</script>

<template>
  <nav class="nav" aria-label="Sections">
    <div class="logo" title="LogLens" aria-hidden="true">LOG</div>
    <button :class="{ active: query.view === 'overview' }" :aria-current="query.view === 'overview' ? 'page' : undefined" aria-label="Overview" title="Overview" @click="go('overview')"><svg viewBox="0 0 24 24" aria-hidden="true"><rect x="4" y="4" width="7" height="7" rx="1"/><rect x="13" y="4" width="7" height="7" rx="1"/><rect x="4" y="13" width="7" height="7" rx="1"/><rect x="13" y="13" width="7" height="7" rx="1"/></svg></button>
    <button :class="{ active: query.view === 'logs' }" :aria-current="query.view === 'logs' ? 'page' : undefined" aria-label="Structured logs" title="Structured logs" @click="go('logs')"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 6h16M4 12h10M4 18h13"/></svg></button>
    <button :class="{ active: query.view === 'users' }" :aria-current="query.view === 'users' ? 'page' : undefined" aria-label="Users" title="Users" @click="go('users')"><svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="9" cy="8" r="3.5"/><path d="M3.5 19c.6-3 2.8-5 5.5-5s4.9 2 5.5 5M16 5.5a3 3 0 0 1 0 5.5M17.5 14c2 .6 3.4 2.3 3.9 5"/></svg></button>
    <button :class="{ active: query.view === 'requests' }" :aria-current="query.view === 'requests' ? 'page' : undefined" aria-label="Requests" title="Requests — durations and status" @click="go('requests')"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 19V9m5.5 10V5M15 19v-7m5 7V11"/></svg></button>
    <button class="bottom" disabled aria-label="Settings (coming soon)" title="Settings — coming soon"><svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/></svg></button>
  </nav>
</template>
