/** Minimal fetch wrapper: throws on non-2xx, returns parsed JSON (null on 204). */
export async function api<T>(path: string, opts?: RequestInit): Promise<T> {
  const res = await fetch(path, opts)
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
  return res.status === 204 ? (null as T) : (res.json() as Promise<T>)
}
