// Sends every seed invoice to ANAF's public e-Factura validator and prints OK or ANAF's exact messages (slice 2, T5).
// Contract (no authentication, production host only): POST the XML as the body with Content-Type text/plain; the answer is
// { "stare": "ok" | "nok", "Messages": [{ "message": "..." }], "trace_id": "..." }.
// Source: https://anafpy.readthedocs.io/en/latest/anaf-reference/efactura/api/ section 5 (ANAF's own page was unreachable).
// Usage: node scripts/validate-seed-anaf.mjs [file.xml ...]   Default: every seed/invoices/*.xml. Exits 1 if any file fails.
import { readdir, readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const ENDPOINT = 'https://webservicesp.anaf.ro/prod/FCTEL/rest/validare/FACT1';
const TIMEOUT_MS = 30_000;
const seedDir = fileURLToPath(new URL('../seed/invoices/', import.meta.url));

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
  return { ok: response.ok && json?.stare === 'ok', summary, messages, raw };
}

let failed = 0;
for (const { path, label } of files) {
  const result = await validate(path);
  if (!result.ok) failed++;
  console.log(result.ok ? `OK    ${label}` : `FAIL  ${label}  (${result.summary})`);
  for (const message of result.messages) console.log(`      ${message}`);
  if (!result.ok && result.raw !== undefined) console.log(`      raw: ${result.raw}`);
}

if (files.length === 0) console.log(`No invoices found in ${seedDir}`);
console.log(`\n${files.length - failed}/${files.length} passed`);
process.exitCode = failed > 0 || files.length === 0 ? 1 : 0;
