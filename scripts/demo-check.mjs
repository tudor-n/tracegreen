// Demo-laptop check (B7; docs/RUNBOOK.md, "Demo laptop" step 5): can a phone on the hotspot reach this laptop, and
// will the QR links point at it? Prints PASS, FAIL or SKIP per check and exits 1 unless every check passes.
// Usage: node scripts/demo-check.mjs [url]   (url defaults to PUBLIC_BASE_URL in .env.demo; pass the LAN address the
// dashboard will be opened at when PUBLIC_BASE_URL is left empty, RUNBOOK "Demo laptop" step 2). Node 24+, no dependencies.
// Run it from the laptop with the demo stack up. It reads the URL from the laptop's side, so it proves the stack
// answers on that address; a phone still needs to be on the same hotspot, and port 3000 open (scripts/firewall.ps1).
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  baseUrlProblem,
  envValue,
  healthProblem,
  linkNote,
  linkProblem,
  loopbackProblem,
  normalizeUrl,
  streamProblem,
} from './lib/demo-check.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const TIMEOUT_MS = 5000;

/** The URL to check, where it came from, and whether .env.demo gives PUBLIC_BASE_URL a value the API must use. */
function configuredUrl() {
  let fromFile;
  try {
    fromFile = envValue(readFileSync(join(root, '.env.demo'), 'utf8'), 'PUBLIC_BASE_URL')?.trim();
  } catch {
    fromFile = undefined;
  }
  const configured = fromFile !== undefined && fromFile !== '';
  const fromArg = process.argv[2];
  if (fromArg !== undefined) return { value: fromArg, from: 'the command line', configured };
  return { value: fromFile, from: '.env.demo', configured };
}

async function getJson(url) {
  const res = await fetch(url, { signal: AbortSignal.timeout(TIMEOUT_MS) });
  let body = null;
  try {
    body = await res.json();
  } catch {
    // Not the API's JSON: a proxy page or nothing at all. healthProblem reports it.
  }
  return { status: res.status, body };
}

/** Opens the event stream and reads until the first event or the time limit, then closes it. */
async function firstStreamChunk(url) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
  let text = '';
  try {
    const res = await fetch(url, { signal: controller.signal, headers: { accept: 'text/event-stream' } });
    const contentType = res.headers.get('content-type');
    if (res.ok && res.body !== null) {
      const reader = res.body.getReader();
      const decoder = new TextDecoder();
      try {
        while (!/^event: /m.test(text)) {
          const { done, value } = await reader.read();
          if (done) break;
          text += decoder.decode(value, { stream: true });
        }
      } catch (error) {
        if (!controller.signal.aborted) throw error;
      }
    }
    return { status: res.status, contentType, text };
  } finally {
    clearTimeout(timer);
    controller.abort();
  }
}

const unreachable = (error) =>
  `No answer (${error?.cause?.code ?? error?.name ?? 'error'}). Is the demo stack up, and is this the laptop's current IP?`;

const { value, from, configured } = configuredUrl();
const base = value === undefined ? '' : normalizeUrl(value.trim());
let health = null;

const checks = [
  { title: `The URL phones open is set (from ${from})`, run: () => baseUrlProblem(value) },
  { title: 'It is not a loopback address', needs: 0, run: () => loopbackProblem(base) },
  {
    title: `${base}/api/health answers`,
    needs: 1,
    run: async () => {
      try {
        health = await getJson(`${base}/api/health`);
      } catch (error) {
        return unreachable(error);
      }
      return healthProblem(health.status, health.body);
    },
  },
  {
    title: 'QR links use this address',
    needs: 2,
    run: () => linkProblem(health.body, base, configured),
    note: () => linkNote(health.body, base),
  },
  {
    title: `${base}/api/events opens and sends its first event`,
    needs: 2,
    run: async () => {
      try {
        const stream = await firstStreamChunk(`${base}/api/events`);
        return streamProblem(stream.status, stream.contentType, stream.text);
      } catch (error) {
        return unreachable(error);
      }
    },
  },
];

console.log(`TraceGreen demo check, ${base || '(no URL)'}`);
const passed = [];
for (const [i, check] of checks.entries()) {
  if (check.needs !== undefined && !passed[check.needs]) {
    passed[i] = false;
    console.log(`SKIP  ${check.title}`);
    continue;
  }
  const problem = await check.run();
  passed[i] = problem === null;
  console.log(`${problem === null ? 'PASS' : 'FAIL'}  ${check.title}`);
  const detail = problem ?? check.note?.() ?? null;
  if (detail !== null) console.log(`      ${detail}`);
}
const failed = passed.filter((ok) => !ok).length;
console.log(failed === 0 ? 'Phones on the same network can open this laptop\'s links.' : `${failed} of ${checks.length} checks did not pass.`);
process.exitCode = failed === 0 ? 0 : 1;
