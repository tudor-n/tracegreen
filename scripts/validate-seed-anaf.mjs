// Sends every seed invoice to ANAF's public e-Factura validator and prints OK or ANAF's exact messages (slice 2, T5).
// Contract (no authentication, production host only): POST the XML as the body with Content-Type text/plain; the answer is
// { "stare": "ok" | "nok", "Messages": [{ "message": "..." }], "trace_id": "..." }.
// Source: https://anafpy.readthedocs.io/en/latest/anaf-reference/efactura/api/ section 5 (ANAF's own page was unreachable).
// Usage: node scripts/validate-seed-anaf.mjs [file.xml ...]   Default: every seed/invoices/*.xml. Exits 1 if any file fails.
import { readdir, readFile } from 'node:fs/promises';
import { basename } from 'node:path';
import { fileURLToPath } from 'node:url';

const ENDPOINT = 'https://webservicesp.anaf.ro/prod/FCTEL/rest/validare/FACT1';
const TIMEOUT_MS = 30_000;
const seedDir = fileURLToPath(new URL('../seed/invoices/', import.meta.url));

// Imports from suppliers outside Romania without a Romanian VAT registration (decision of 2026-10-23, docs/NEXT.md).
// ANAF identifies the seller by a Romanian CUI, so it cannot accept these; every other rule must still pass, so this
// message must be the only one. A file listed here fails if ANAF accepts it, so the list cannot go stale unnoticed.
const SELLER_NOT_IDENTIFIED = 'tipAssert=E; codEroare=ERRIdentif; textEroare=Nu a fost identificat cui vanzator';
const EXPECTED_FAILURES = new Map([
  ['N1-shenzhen.xml', SELLER_NOT_IDENTIFIED],
  ['N2-taiwan.xml', SELLER_NOT_IDENTIFIED],
  ['N5-hamburg.xml', SELLER_NOT_IDENTIFIED],
]);

const files = process.argv.length > 2
  ? process.argv.slice(2).map((path) => ({ path, label: path }))
  : (await readdir(seedDir)).filter((name) => name.endsWith('.xml')).sort().map((name) => ({ path: seedDir + name, label: name }));

async function validate(path) {
  let response;
  try {
    const body = await readFile(path);
    response = await fetch(ENDPOINT, {
      method: 'POST',
      headers: { 'Content-Type': 'text/plain' },
      body,
      signal: AbortSignal.timeout(TIMEOUT_MS),
    });
  } catch (error) {
    return { ok: false, summary: 'no response', messages: [`${error.message}${error.cause ? `: ${error.cause.message ?? error.cause}` : ''}`] };
  }
  const raw = await response.text();
  let json;
  try {
    json = JSON.parse(raw);
  } catch {
    json = undefined;
  }
  const messages = Array.isArray(json?.Messages) ? json.Messages.map((m) => m?.message ?? JSON.stringify(m)) : [];
  const summary = [`HTTP ${response.status}`, json?.stare && `stare ${json.stare}`, json?.trace_id && `trace_id ${json.trace_id}`]
    .filter(Boolean).join(', ');
  const valid = response.ok && json?.stare === 'ok';
  const rejected = response.ok && json?.stare === 'nok';
  return { ok: valid, valid, rejected, summary, messages, raw };
}

// A listed file passes when ANAF rejects it with exactly the expected message and nothing else.
function judge(result, expected) {
  if (!expected) return result;
  if (result.valid) return { ...result, ok: false, summary: `${result.summary}; ANAF accepted it, so remove it from EXPECTED_FAILURES` };
  const asExpected = result.rejected && result.messages.length === 1 && result.messages[0] === expected;
  return asExpected
    ? { ...result, ok: true, note: 'expected: seller has no Romanian CUI' }
    : { ...result, ok: false, summary: `${result.summary}; expected only: ${expected}` };
}

let failed = 0;
let expectedCount = 0;
for (const { path, label } of files) {
  const result = judge(await validate(path), EXPECTED_FAILURES.get(basename(path)));
  if (!result.ok) failed++;
  if (result.ok && result.note) expectedCount++;
  console.log(result.ok ? `OK    ${label}${result.note ? `  (${result.note})` : ''}` : `FAIL  ${label}  (${result.summary})`);
  for (const message of result.messages) console.log(`      ${message}`);
  if (!result.ok && result.raw !== undefined) console.log(`      raw: ${result.raw}`);
}

if (files.length === 0) console.log(`No invoices found in ${seedDir}`);
console.log(`\n${files.length - failed}/${files.length} passed${expectedCount ? ` (${expectedCount} with the expected seller-identification error)` : ''}`);
process.exitCode = failed > 0 || files.length === 0 ? 1 : 0;
