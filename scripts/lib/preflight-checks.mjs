// The checks behind scripts/preflight.mjs (C6). Each takes file contents and returns findings
// ({ file, line, message }, line 1-based or null for the whole file), so node:test covers them
// without touching the repository. No dependencies.

import { findBannedTerms } from './banned-terms.mjs';

const PLACEHOLDER = 'PLACEHOLDER';

/** The 1-based line of the first line matching `pattern` at or after `from`, or null. */
function lineOf(lines, pattern, from = 0) {
  for (let i = from; i < lines.length; i++) if (pattern.test(lines[i])) return i + 1;
  return null;
}

const escapeRegExp = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/** Factor rows whose `source` still says PLACEHOLDER (PRIORITY-CHANGES §2). */
export function placeholderFactors(factorsText, file) {
  const lines = factorsText.split('\n');
  return JSON.parse(factorsText)
    .filter((row) => row.source.includes(PLACEHOLDER))
    .map((row) => {
      const idLine = lineOf(lines, new RegExp(`"id":\\s*"${escapeRegExp(row.id)}"`));
      const line = idLine === null ? null : lineOf(lines, /"source":/, idLine - 1);
      return { file, line, message: `${row.id}: the source is still a placeholder` };
    });
}

/** Splits a Markdown table row into trimmed cells; `\|` inside a cell is a literal bar. */
function cellsOf(row) {
  return row
    .replace(/^\|/, '')
    .replace(/\|$/, '')
    .split(/(?<!\\)\|/)
    .map((cell) => cell.trim().replaceAll('\\|', '|'));
}

/** Every body row of every pipe table in a Markdown text, keyed by its table's header, with its line. */
export function markdownTables(markdown) {
  const rows = [];
  let header = null;
  markdown.split('\n').forEach((raw, i) => {
    const line = raw.trim();
    if (!line.startsWith('|')) {
      header = null;
      return;
    }
    const cells = cellsOf(line);
    if (header === null) {
      header = cells;
      return;
    }
    if (cells.every((cell) => /^:?-+:?$/.test(cell))) return;
    rows.push({ line: i + 1, cells: Object.fromEntries(header.map((name, j) => [name, cells[j] ?? ''])) });
  });
  return rows;
}

const FACTOR_FIELDS = [
  ['key', 'key', (v) => v],
  ['value', 'value', Number],
  ['unit', 'unit', (v) => v],
  ['source', 'source', (v) => v],
  ['year', 'year', (v) => Number(String(v).replace(/\s*\(placeholder\)\s*$/, ''))],
  ['rowLabel', 'row label', (v) => v],
];

/** Where docs/factor-sources.md and seed/factors.json disagree: a row missing on either side, or a field that differs. */
export function factorDocMismatches(docText, docFile, factorsText) {
  const factors = JSON.parse(factorsText);
  const docRows = new Map(
    markdownTables(docText)
      .filter((row) => /^`[a-z]+\.[A-Za-z0-9_]+`$/.test(row.cells.id ?? ''))
      .map((row) => [row.cells.id.slice(1, -1), row]),
  );
  const findings = [];
  for (const factor of factors) {
    const row = docRows.get(factor.id);
    if (row === undefined) {
      findings.push({ file: docFile, line: null, message: `${factor.id} is in factors.json but has no row here` });
      continue;
    }
    for (const [jsonField, docColumn, read] of FACTOR_FIELDS) {
      const inDoc = read(row.cells[docColumn] ?? '');
      const inJson = read(factor[jsonField]);
      if (inDoc !== inJson) {
        findings.push({
          file: docFile,
          line: row.line,
          message: `${factor.id}: ${docColumn} is "${row.cells[docColumn] ?? ''}" here but "${factor[jsonField]}" in factors.json`,
        });
      }
    }
  }
  const ids = new Set(factors.map((factor) => factor.id));
  for (const [id, row] of docRows) {
    if (!ids.has(id)) findings.push({ file: docFile, line: row.line, message: `${id} has a row here but is not in factors.json` });
  }
  return findings;
}

export const ETS_NAME = 'ETS_PRICE_EUR_PER_T';

/** The carbon-price row of docs/factor-sources.md: its line, value, source and date set; null when there is none. */
export function carbonPriceRow(docText) {
  const row = markdownTables(docText).find((r) => r.cells.name === `\`${ETS_NAME}\``);
  return row === undefined
    ? null
    : { line: row.line, value: row.cells.value ?? '', source: row.cells.source ?? '', date: row.cells['date set'] ?? '' };
}

/** The price has a real source and the date it was set (docs/factor-sources.md, "Carbon price"). */
export function carbonPriceFindings(docText, docFile) {
  const row = carbonPriceRow(docText);
  if (row === null) return [{ file: docFile, line: null, message: `no row for ${ETS_NAME} in the "Carbon price" table` }];
  const findings = [];
  if (row.source === '' || row.source.includes(PLACEHOLDER)) {
    findings.push({ file: docFile, line: row.line, message: `${ETS_NAME}: no source yet (the ${row.value} is a placeholder)` });
  }
  if (!/^\d{4}-\d{2}-\d{2}$/.test(row.date)) {
    findings.push({ file: docFile, line: row.line, message: `${ETS_NAME}: no date set (write it as YYYY-MM-DD)` });
  }
  return findings;
}

/** The four places that hold the carbon price, and how each spells it. */
export const ETS_PLACES = [
  { file: '.env.demo', pattern: new RegExp(`^${ETS_NAME}=([^\\s#]*)`) },
  { file: '.env.example', pattern: new RegExp(`^${ETS_NAME}=([^\\s#]*)`) },
  { file: 'compose.yml', pattern: new RegExp(`^\\s*${ETS_NAME}:\\s*"?([^"\\s#]*)"?`) },
  { file: 'api/src/TraceGreen.Api/AppConfig.cs', pattern: new RegExp(`"${ETS_NAME}",\\s*([0-9.]+)m\\)`) },
];

/** The price as each place sets it: `{ file, line, value }`, value '' when it is blank and null when it is missing. */
export function carbonPriceValues(textsByFile) {
  return ETS_PLACES.map(({ file, pattern }) => {
    const lines = (textsByFile[file] ?? '').split('\n');
    const index = lines.findIndex((line) => pattern.test(line));
    return index < 0
      ? { file, line: null, value: null }
      : { file, line: index + 1, value: lines[index].match(pattern)[1] };
  });
}

/** Every place sets the price, and all of them and docs/factor-sources.md give the same number. */
export function carbonPriceAgreement(values, docValue, docFile, docLine) {
  const findings = values
    .filter((v) => v.value === null || v.value === '')
    .map((v) => ({ file: v.file, line: v.line, message: `${ETS_NAME} is ${v.value === null ? 'missing' : 'blank'}` }));
  const all = [...values.filter((v) => v.value), { file: docFile, line: docLine, value: docValue }];
  if (new Set(all.map((v) => Number(v.value))).size > 1) {
    for (const v of all) findings.push({ file: v.file, line: v.line, message: `${ETS_NAME} is ${v.value} here` });
  }
  return findings;
}

/** Banned terms in one file's text, by line; a phrase split across two lines is reported for the whole file. */
export function bannedTermFindings(file, text) {
  if (findBannedTerms(text).length === 0) return [];
  const findings = [];
  text.split('\n').forEach((line, i) => {
    for (const { term, instead } of findBannedTerms(line)) findings.push({ file, line: i + 1, message: `${term}: ${instead}` });
  });
  if (findings.length === 0) {
    for (const { term, instead } of findBannedTerms(text)) findings.push({ file, line: null, message: `${term} (across lines): ${instead}` });
  }
  return findings;
}
