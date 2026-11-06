// The Supplier Terms' fingerprint, recomputed without .NET, so anyone holding a copy of seed/supplier-terms.json can
// check the SHA-256 the audit log recorded (terms section 9). It is the SHA-256 (hex) of the file's canonical JSON as
// api/src/TraceGreen.Domain/Security/CanonicalJson.cs writes it:
//   - object keys sorted by UTF-16 code unit at every level, no whitespace;
//   - strings escaped as System.Text.Json's default encoder does: \\ and the short forms \b \f \n \r \t, and \uXXXX
//     (upper-case hex, one per UTF-16 code unit) for every other control character, everything outside printable ASCII,
//     and the characters " & ' + < > `;
//   - numbers as JSON writes them (the terms file holds integers alone).
// The parity with C# is pinned by tests on both sides against seed/supplier-terms-versions.json. No dependencies.

import { createHash } from 'node:crypto';

const SHORT = { '\\': '\\\\', '\b': '\\b', '\f': '\\f', '\n': '\\n', '\r': '\\r', '\t': '\\t' };
const ALWAYS_ESCAPED = new Set(['"', '&', "'", '+', '<', '>', '`']);

function escapeString(text) {
  let out = '"';
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    const code = text.charCodeAt(i);
    if (SHORT[ch] !== undefined) out += SHORT[ch];
    else if (code < 0x20 || code > 0x7e || ALWAYS_ESCAPED.has(ch)) out += `\\u${code.toString(16).toUpperCase().padStart(4, '0')}`;
    else out += ch;
  }
  return `${out}"`;
}

/** The canonical JSON of a parsed JSON value. */
export function canonicalJson(value) {
  if (value === null || typeof value === 'boolean' || typeof value === 'number') return JSON.stringify(value);
  if (typeof value === 'string') return escapeString(value);
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(',')}]`;
  const keys = Object.keys(value).sort();
  return `{${keys.map((key) => `${escapeString(key)}:${canonicalJson(value[key])}`).join(',')}}`;
}

/** `{ version, sha256 }` of a supplier-terms.json text. */
export function termsHash(fileText) {
  const doc = JSON.parse(fileText);
  return { version: doc.version, sha256: createHash('sha256').update(canonicalJson(doc), 'utf8').digest('hex') };
}
