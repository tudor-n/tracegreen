// Terms that must not appear in anything a juror can read (docs/PRIORITY-CHANGES.md §4).
// Shared by the copy-guard hook (.claude/hooks/copy-guard.mjs) and the preflight (C6),
// so both always agree on what is banned and which files are exempt.
//
// Company names and the retired product name are stored only as SHA-256 hashes: the
// repository is public and must not list them, not even as banned names. Two kinds:
// - `contains`: the hash of the lower-case name and its length. It matches anywhere inside a
//   word, in any case, so identifiers like useNameStore are caught too.
// - `exact`: the hash of the exact word, matched as a whole word in that case only. For names
//   with an everyday lower-case twin, so ordinary words don't trip the check.
// To add one, run `node scripts/lib/banned-terms.mjs --hash <name> ...` (contains) or
// `--exact <name> ...` and paste the printed lines into the right list below.

import { createHash } from 'node:crypto';
import { relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

/** Generic terms, stored in plain text. */
export const PLAIN_TERMS = [
  { term: 'blockchain', pattern: /\bblock-?chains?\b/i, instead: 'say "tamper-evident audit log"' },
  // Ruling 2's tooltip disclaims the term ("…, not externally verified PCFs"), so that exact negation is allowed.
  {
    term: 'verified PCF',
    pattern: /(?<!\bnot\s+externally\s+)\bverified\s+PCFs?\b/i,
    instead: 'say "supplier-confirmed" (ruling 2 and 3)',
  },
  {
    term: 'primary data',
    pattern: /\bprimary\s+data\b/i,
    instead: 'say "supplier-confirmed share", subtitle "by emissions, not by node count" (ruling 2)',
  },
];

/** Hashed names: `contains` holds `{ length, sha256 }` of the lower-case name, `exact` the SHA-256 of the exact word. */
export const HASHED_TERMS = [
  {
    kind: 'company name',
    instead: 'leave it out; the seed stays fictional (Transilvania Electro-Assembly SRL and its five suppliers)',
    contains: [
      { length: 9, sha256: '8146c43229fc8e29987913e2ada59a0cd737e3798faccc5310c9aa200899b277' },
      { length: 7, sha256: 'c27e13bdfeef173ea6328ab1f72810f8a4a8fd6aa7608786526681596d89c907' },
      { length: 5, sha256: '0600c79833cc723f51a26a1c39b46fcc6bb909a6db9b8c9a3885469f8aaa3fc2' },
      { length: 10, sha256: '3ce50b4077295def9dd7c77c0d63bb11d584e5d21ffc0495ac2dbb5c3cf23612' },
      { length: 9, sha256: 'fa4ad48a5a6f26fe43445091e0d9ebf705b66edf3a1f1998dd49374f817fc8a3' },
    ],
    // The two names with an everyday lower-case twin, matched as whole words in that case only.
    exact: [
      '02fb3680cd781f8fa918bdb75abe6a38d9165b849b54bdf6f71a174d69c617b2',
      'd6693a421ebd3bd40ed01f429f4cd9d45f31352982d7565a5a0ead7ec64dc373',
    ],
  },
  {
    kind: 'retired product name',
    instead: 'say "TraceGreen"',
    contains: [{ length: 11, sha256: '91f44966f6e3520fac9f4c663847cad71c8da45c5f5ea1d0cf33b591ba1155de' }],
    exact: [],
  },
];

/** Repo-relative paths that state the rules and may therefore name the terms. A trailing slash means a folder. */
export const EXEMPT_PATHS = [
  'CLAUDE.md',
  'docs/PRIORITY-CHANGES.md',
  'docs/plan/',
  '.claude/hooks/',
  'scripts/lib/banned-terms.mjs',
  'scripts/lib/banned-terms.test.mjs',
  'scripts/lib/preflight-checks.test.mjs',
];

export function sha256(word) {
  return createHash('sha256').update(word, 'utf8').digest('hex');
}

const exactIndex = new Map(HASHED_TERMS.flatMap((entry) => entry.exact.map((h) => [h, entry])));
const containsIndex = new Map(HASHED_TERMS.flatMap((entry) => entry.contains.map((c) => [c.sha256, entry])));
const containsLengths = [...new Set(HASHED_TERMS.flatMap((entry) => entry.contains.map((c) => c.length)))];

/** The hashed entry a word is or contains, if any. */
function hashedMatch(word) {
  const exact = exactIndex.get(sha256(word));
  if (exact) return exact;
  const lower = word.toLowerCase();
  for (const length of containsLengths) {
    for (let i = 0; i + length <= lower.length; i++) {
      const entry = containsIndex.get(sha256(lower.slice(i, i + length)));
      if (entry) return entry;
    }
  }
  return undefined;
}

/**
 * Finds banned terms in `text`. Returns `{ term, instead }` per distinct match; for hashed
 * names `term` is the word as written in `text`, so the message points at it.
 */
export function findBannedTerms(text) {
  const found = new Map();
  for (const { term, pattern, instead } of PLAIN_TERMS) {
    if (pattern.test(text)) found.set(term, { term, instead });
  }
  for (const word of new Set(Array.from(text.matchAll(/[\p{L}\p{N}]+/gu), (m) => m[0]))) {
    const entry = hashedMatch(word);
    if (entry) found.set(word, { term: `${word} (${entry.kind})`, instead: entry.instead });
  }
  return [...found.values()];
}

/**
 * Turns an absolute or relative file path into a repo-relative path with forward slashes.
 * Returns null for a path outside the project. A path inside a Claude Code worktree
 * (.claude/worktrees/<name>/...) is made relative to that worktree.
 */
export function toRepoPath(filePath, projectDir) {
  const rel = relative(projectDir, filePath.replaceAll('\\', sep)).split(sep).join('/');
  if (rel === '' || rel.startsWith('../') || rel === '..' || /^[A-Za-z]:/.test(rel) || rel.startsWith('/')) return null;
  return rel.replace(/^\.claude\/worktrees\/[^/]+\//, '');
}

export function isExempt(repoPath) {
  return EXEMPT_PATHS.some((p) => (p.endsWith('/') ? repoPath.startsWith(p) : repoPath === p));
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const [flag, ...words] = process.argv.slice(2);
  for (const word of words) {
    if (flag === '--hash') console.log(`{ length: ${word.length}, sha256: '${sha256(word.toLowerCase())}' },`);
    if (flag === '--exact') console.log(`'${sha256(word)}',`);
  }
}
