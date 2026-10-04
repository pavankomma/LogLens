// Hand-written surface for query-dsl.js, which stays plain JS so the node:test suite in tests/js
// can import it without a TypeScript build (parity contract with QueryParser.cs).
import type { LogEvent } from './types'

export const LEVELS: string[]
export const LEVEL_NAME: Record<string, string>
export const RESOURCE_KEYS: string[]
export const TRACE_KEYS: string[]

export function quote(v: unknown): string
export function unq(v: unknown): string
export function tokenize(query: string | null | undefined): string[]

export interface TermDescription {
  neg: boolean
  field: string
  op: string
  value: string
  text: string
}
export function describeTerm(raw: string): TermDescription
export function buildTerm(field: string, op: string, value: string, resourceKey: string | null): string
export function resourceTerm(key: string | null, value: string): string
export function resourceOf(e: LogEvent): { key: string | null; value: string }
export function traceOf(e: LogEvent): { key: string; value: string } | null
export function join(terms: string[]): string
