const RESOURCE_PALETTE = ['#2B7BB0', '#29A0A0', '#5B3FAD', '#956400', '#346538', '#9F2F2D', '#1F6C9F', '#B5651D', '#5E5E5E', '#8A2D6E']

export const pad = (n: number): string => String(n).padStart(2, '0')

export const fmtTime = (iso: string): string => {
  const d = new Date(iso), sameDay = d.toDateString() === new Date().toDateString()
  const t = `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}.${String(d.getMilliseconds()).padStart(3, '0')}`
  return sameDay ? t : `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${t}`
}

export const fmtTimeUtc = (iso: string): string => {
  const d = new Date(iso), now = new Date()
  const sameDay = d.getUTCFullYear() === now.getUTCFullYear() && d.getUTCMonth() === now.getUTCMonth() && d.getUTCDate() === now.getUTCDate()
  const t = `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}:${pad(d.getUTCSeconds())}.${String(d.getUTCMilliseconds()).padStart(3, '0')}`
  return sameDay ? t : `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())} ${t}`
}

export const fmtSpan = (ms: number): string =>
  ms < 1000 ? Math.round(ms) + ' ms'
    : ms < 60000 ? +(ms / 1000).toFixed(ms < 10000 ? 1 : 0) + ' s'
      : ms < 3600000 ? Math.round(ms / 60000) + ' min'
        : ms < 86400000 ? +(ms / 3600000).toFixed(1) + ' h'
          : +(ms / 86400000).toFixed(1) + ' d'

export const fmtAgo = (iso: string): string => {
  const s = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000)
  return s < 60 ? Math.round(s) + ' s ago' : s < 3600 ? Math.round(s / 60) + ' min ago' : s < 86400 ? Math.round(s / 3600) + ' h ago' : Math.round(s / 86400) + ' d ago'
}

export const fmtMs = (v: number | null | undefined): string =>
  v == null ? '—' : v >= 1000 ? (v / 1000).toFixed(2) + ' s' : Math.round(v) + ' ms'

export const toLocalInput = (iso: string): string => {
  const d = new Date(iso)
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

const colorCache = new Map<string, string>()
export const resourceColor = (name: string): string => {
  if (!colorCache.has(name)) colorCache.set(name, RESOURCE_PALETTE[colorCache.size % RESOURCE_PALETTE.length]!)
  return colorCache.get(name)!
}

export const esc = (s: unknown): string => String(s).replace(/[&<>]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' })[c]!)

/** Safe for v-html: input is escaped first, then only <b> tags are introduced around template holes. */
export const highlightTemplate = (tpl: string): string =>
  esc(tpl).replace(/\{\{|\}\}|\{[@$]?[A-Za-z0-9_]+(?:,-?\d+)?(?::[^}]*)?\}/g, m => m.length > 2 ? `<b>${m}</b>` : m)
