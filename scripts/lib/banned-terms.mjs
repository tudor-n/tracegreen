// Terms that must not appear in anything a juror can read (docs/PRIORITY-CHANGES.md §4).
// Shared by the copy-guard hook (.claude/hooks/copy-guard.mjs) and the preflight (C6),
// so both always agree on what is banned and which files are exempt.
//
// Company names and the retired product name are stored only as SHA-256 hashes of the
// exact word: the repository is public and must not list them, not even as banned names.
// To add one, run `node scripts/lib/banned-terms.mjs --hash <word> [<word> ...]` and paste
// the printed hashes below. Names with an everyday lower-case twin store the exact case
// only; the others also store their lower-case, upper-case and capitalised forms.

import { createHash } from 'node:crypto';
import { relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

/** Generic terms, stored in plain text. */
export const PLAIN_TERMS = [
  { term: 'blockchain', pattern: /\bblock-?chains?\b/i, instead: 'say "tamper-evident audit log"' },
  { term: 'verified PCF', pattern: /\bverified\s+PCFs?\b/i, instead: 'say "supplier-confirmed" (ruling 2 and 3)' },
  {
    term: 'primary data',
    pattern: /\bprimary\s+data\b/i,
    instead: 'say "supplier-confirmed share", subtitle "by emissions, not by node count" (ruling 2)',
  },
];

/** Hashed names: `hashes` are SHA-256 hex digests of the exact word. */
export const HASHED_TERMS = [
  {
    kind: 'company name',
    instead: 'leave it out; the seed stays fictional (Transilvania Electro-Assembly SRL and its five suppliers)',
    hashes: [],
  },
  {
    kind: 'retired product name',
    instead: 'say "TraceGreen"',
    hashes: [],
  },
];

/** Repo-relative paths that state the rules and may therefore name the terms. A trailing slash means a folder. */
export const EXEMPT_PATHS = [
  'CLAUDE.md',
  'docs/PRIORITY-CHANGES.md',
  'docs/plan/',
  '.claude/hooks/',
  'scripts/lib/banned-terms.mjs',
];

export function sha256(word) {
  return createHash('sha256').update(word, 'utf8').digest('hex');
}

const hashIndex = new Map(HASHED_TERMS.flatMap((entry) => entry.hashes.map((h) => [h, entry])));

/**
 * Finds banned terms in `text`. Returns `{ term, instead }` per distinct match; for hashed
 * names `term` is the word as written in `text`, so the message points at it.
 */
export function findBannedTerms(text) {
  const found = new Map();
  for (const { term, pattern, instead } of PLAIN_TERMS) {
    if (pattern.test(text)) found.set(term, { term, instead });
  }
  for (const [word] of text.matchAll(/[\p{L}\p{N}]+/gu)) {
    const entry = hashIndex.get(sha256(word));
    if (entry && !found.has(word)) found.set(word, { term: `${word} (${entry.kind})`, instead: entry.instead });
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

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1] && process.argv[2] === '--hash') {
  for (const word of process.argv.slice(3)) console.log(`'${sha256(word)}',`);
}
