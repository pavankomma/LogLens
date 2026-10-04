export const PAGE = 200                  // rows per fetch
export const MAX_ROWS = 2000             // rows kept in the DOM
export const ROW_FLUSH_MS = 250          // live: how often queued events enter the grid
export const SUMMARY_MS = 2000           // live: facets/histogram/count refresh
export const STATS_MS = 2000
export const LIVE_BATCH_MAX = 50

export const RANGES: [string, string][] = [
  ['-15m', 'Last 15 minutes'],
  ['-1h', 'Last hour'],
  ['-6h', 'Last 6 hours'],
  ['-24h', 'Last 24 hours'],
  ['-7d', 'Last 7 days'],
  ['', 'All time'],
]

export const LEVEL_VAR: Record<string, string> = {
  Trace: 'var(--lvl-trace)', Debug: 'var(--lvl-debug)', Information: 'var(--lvl-info)',
  Warning: 'var(--lvl-warn)', Error: 'var(--lvl-error)', Fatal: 'var(--lvl-fatal)',
}
