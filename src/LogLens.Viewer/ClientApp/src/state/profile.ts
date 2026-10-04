import { reactive } from 'vue'
import { traceOf, resourceOf } from '@/lib/query-dsl.js'
import { fmtTime, fmtTimeUtc, resourceColor } from '@/lib/format'
import type { Facets, LogEvent, Profile, Row } from '@/lib/types'

/** Deployment profile served by /api/profile: display names, quick views, key hints. */
export const profile: Profile = reactive({
  name: null,
  entities: {},
  quickViews: [],
  redact: [],
  keys: { identity: [], duration: [], route: [], correlation: [] },
})

const DURATION_KEYS_DEFAULT = ['Elapsed', 'ElapsedMs', 'ElapsedMilliseconds', 'Duration', 'DurationMs', 'TookMs']
const IDENTITY_KEYS_DEFAULT = ['User', 'UserId', 'UserName', 'Username', 'Customer', 'CustomerId', 'TenantId', 'ClientId', 'Email']
const ROUTE_KEYS_DEFAULT = ['Route', 'Path', 'RequestPath', 'Endpoint', 'Operation', 'Url']

/** The effective key lists: the profile's when configured, sensible defaults otherwise. */
export const durationKeys = (): string[] => profile.keys.duration.length ? profile.keys.duration : DURATION_KEYS_DEFAULT
export const identityKeys = (): string[] => profile.keys.identity.length ? profile.keys.identity : IDENTITY_KEYS_DEFAULT
export const routeKeys = (): string[] => profile.keys.route.length ? profile.keys.route : ROUTE_KEYS_DEFAULT

const entityOf = (key: string) =>
  profile.entities[key] || Object.entries(profile.entities).find(([k]) => k.toLowerCase() === key.toLowerCase())?.[1] || null
export const entityLabel = (key: string): string => entityOf(key)?.label || key
export const entityLink = (key: string, value: string): string | null => {
  const l = entityOf(key)?.link
  return l ? l.replace('{value}', encodeURIComponent(value)) : null
}

export const detectKey = (facets: Facets | null | undefined, candidates: string[]): string | null => {
  for (const k of candidates) if (facets?.properties?.[k]) return k
  return null
}

/** Correlation key, letting profile-configured keys take precedence over the built-in list. */
export const traceOfP = (e: LogEvent): { key: string; value: string } | null => {
  for (const k of profile.keys.correlation) if (e.properties && e.properties[k]) return { key: k, value: e.properties[k] }
  return traceOf(e)
}

/** Build the per-row view model once so the table doesn't recompute helpers on every render. */
export const toRow = (e: LogEvent): Row => {
  const r = resourceOf(e)
  const t = traceOfP(e)
  return { e, seq: e.seq, resource: r.value, resourceKey: r.key, color: resourceColor(r.value), trace: t, time: fmtTime(e.timestamp), timeUtc: fmtTimeUtc(e.timestamp) }
}
