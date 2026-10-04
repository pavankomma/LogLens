/* Query DSL helpers shared by the UI. Pure functions, no DOM — unit-tested with `node --test tests/js`.
   Must stay in parity with src/LogLens.Viewer/Query/QueryParser.cs (Tokenize / term grammar). */

export const LEVELS = ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Fatal'];
export const LEVEL_NAME = { trace: 'Trace', trc: 'Trace', verbose: 'Trace', vrb: 'Trace', debug: 'Debug', dbg: 'Debug', information: 'Information', info: 'Information', inf: 'Information', warning: 'Warning', warn: 'Warning', wrn: 'Warning', error: 'Error', err: 'Error', fatal: 'Fatal', critical: 'Fatal', ftl: 'Fatal' };
export const RESOURCE_KEYS = ['App', 'Application', 'Service', 'ServiceName', 'service.name', 'Resource'];
export const TRACE_KEYS = ['TraceId', 'trace_id', 'CorrelationId', 'correlation_id', 'DocumentId', 'RequestId'];

/** Quote a value for the DSL when it contains whitespace or quotes (quotes inside are dropped, as the server does). */
export const quote = v => { const s = String(v); return /[\s"]/.test(s) ? `"${s.replace(/"/g, '')}"` : s; };
/** Remove every double quote (server tokenizer semantics). */
export const unq = v => String(v).replace(/"/g, '');

/** Split on whitespace honouring quotes anywhere in a token (`Name="a b"`), like QueryParser.Tokenize. Quotes are kept. */
export function tokenize(query) {
  const out = [];
  let cur = '', inQuote = false;
  for (const ch of query || '') {
    if (ch === '"') { inQuote = !inQuote; cur += ch; continue; }
    if (/\s/.test(ch) && !inQuote) { if (cur) { out.push(cur); cur = ''; } continue; }
    cur += ch;
  }
  if (cur) out.push(cur);
  return out;
}

/** Describe a raw DSL term in plain English: { neg, field, op, value, text }. */
export function describeTerm(raw) {
  const neg = raw.length > 1 && raw[0] === '-';
  const t = neg ? raw.slice(1) : raw;
  let m, d;
  if ((m = /^level:(\w+)(\+?)$/i.exec(t))) d = { field: 'Level', op: m[2] ? 'is at least' : 'is', value: LEVEL_NAME[m[1].toLowerCase()] || m[1] };
  else if ((m = /^category:(.+)$/i.exec(t))) d = { field: 'Category', op: /\*/.test(m[1]) ? 'matches' : 'is', value: unq(m[1]) };
  else if ((m = /^has:(.+)$/i.exec(t))) d = { field: unq(m[1]).toLowerCase() === 'exception' ? 'Exception' : unq(m[1]), op: 'is present', value: '' };
  else if ((m = /^source:(.+)$/i.exec(t))) d = { field: 'Resource', op: /\*/.test(m[1]) ? 'matches' : 'is', value: unq(m[1]) };
  else if ((m = /^(message|exception):(.+)$/i.exec(t))) d = { field: m[1][0].toUpperCase() + m[1].slice(1).toLowerCase(), op: 'contains', value: unq(m[2]) };
  else if ((m = /^([A-Za-z_][\w.]*)(!=|>=|<=|=|~|>|<)([\s\S]*)$/.exec(t))) {
    const op = { '=': 'is', '!=': 'is not', '~': 'contains', '>': '>', '<': '<', '>=': '≥', '<=': '≤' }[m[2]];
    d = { field: RESOURCE_KEYS.includes(m[1]) ? 'Resource' : m[1], op, value: unq(m[3]) };
  } else d = { field: '', op: 'contains', value: unq(t) };

  // fold negation into the wording once, so chips and previews agree
  if (neg) {
    if (d.op === 'is') d.op = 'is not';
    else if (d.op === 'is not') d.op = 'is';
    else if (d.op === 'is present') d.op = 'is absent';
    else if (d.op === 'contains') d.op = 'does not contain';
    else d.op = 'not ' + d.op;
  }
  d.neg = neg;
  d.text = d.field ? `${d.field} ${d.op}${d.value ? ' ' + d.value : ''}` : `${d.op} "${d.value}"`;
  return d;
}

/** Build a DSL term from dialog choices. `resourceKey` is the property that identifies the resource (or null → source:). */
export function buildTerm(field, op, value, resourceKey) {
  const v = quote(value);
  if (field === 'Level') return op === 'is at least' ? 'level:' + value.toLowerCase() + '+' : (op === 'is not' ? '-' : '') + 'level:' + value.toLowerCase();
  if (field === 'Category') return (op === 'is not' ? '-' : '') + 'category:' + (op === 'contains' ? '*' + unq(value) + '*' : v);
  if (field === 'Message') return (op === 'does not contain' ? '-' : '') + 'message:' + v;
  if (field === 'Exception') return op === 'is present' ? 'has:exception' : (op === 'does not contain' ? '-' : '') + 'exception:' + v;
  if (field === 'Resource' && !resourceKey) return (op === 'is not' ? '-' : '') + 'source:' + (op === 'contains' ? '*' + unq(value) + '*' : v);
  const key = field === 'Resource' ? resourceKey : field;
  switch (op) {
    case 'is': return key + '=' + v;
    case 'is not': return key + '!=' + v;
    case 'contains': return key + '~' + v;
    case 'does not contain': return '-' + key + '~' + v;
    case 'is present': return 'has:' + key;
    case '>': case '<': case '≥': case '≤': return key + { '>': '>', '<': '<', '≥': '>=', '≤': '<=' }[op] + unq(value);
  }
  return v;
}

/** Term for "resource is X" given the key the events use. */
export const resourceTerm = (key, value) => key ? key + '=' + quote(value) : 'source:' + quote(value);

/** Which property (if any) identifies the resource on this event. */
export function resourceOf(e) {
  for (const k of RESOURCE_KEYS) if (e.properties && e.properties[k]) return { key: k, value: e.properties[k] };
  return { key: null, value: e.source || '' };
}
/** The correlation key on this event, by precedence. */
export function traceOf(e) {
  for (const k of TRACE_KEYS) if (e.properties && e.properties[k]) return { key: k, value: e.properties[k] };
  return null;
}

/** Parse a query into terms, and give the term list back as a string. */
export const join = terms => terms.join(' ');
