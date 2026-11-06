// Scores the AI service's models on the Romanian test set (F6; docs/plan/ai-f6.md, "Terminal B").
// Usage: node scripts/ai-eval.mjs [--url http://localhost:8000] [--runs 1] [--write]
// It reads GET /health for the ready models, then sends every line of ai/eval/ro-lines.json in one call to
// POST /classify?model=<id> for each model (the dev route: start the service with AI_DEV_ROUTES=1) and times each
// call. A service without /health.models (the stub before F6) or without a ready model is scored once on its default
// chain, POST /classify. It prints a Markdown report; --write also saves it to docs/ai-eval.md. --runs repeats each
// call for steadier times. Exits 1 when the test set is invalid, the service does not answer or nothing could be
// scored; low scores are flagged in the report, not in the exit code. Node 24+, no dependencies.
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  answerProblem,
  categoryLineCounts,
  classifyPath,
  markdownReport,
  parseArgs,
  planTargets,
  scoreAnswers,
  summarizeTarget,
  targetName,
  testSetProblems,
  toClassifyRequest,
} from './lib/ai-eval.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const LINES_FILE = 'ai/eval/ro-lines.json';
const REPORT_FILE = 'docs/ai-eval.md';
const HEALTH_TIMEOUT_MS = 5000;
// The service caps one /classify call at AI_TIMEOUT_S (8 s by default, raised for slow local models when scoring);
// this only stops the script hanging on a stuck service.
const CLASSIFY_TIMEOUT_MS = 120000;

const options = parseArgs(process.argv.slice(2));
if (options.error !== undefined) {
  console.error(`${options.error}\nUsage: node scripts/ai-eval.mjs [--url http://localhost:8000] [--runs 1] [--write]`);
  process.exit(1);
}
if (options.help) {
  console.log('Usage: node scripts/ai-eval.mjs [--url http://localhost:8000] [--runs 1] [--write]');
  process.exit(0);
}

const readJson = (file) => JSON.parse(readFileSync(join(root, file), 'utf8'));
const lines = readJson(LINES_FILE);
const order = readJson('seed/categories.json').map((category) => category.id);
const problems = testSetProblems(lines, order);
if (problems.length > 0) {
  console.error(`${LINES_FILE} cannot be scored:\n${problems.map((p) => `- ${p}`).join('\n')}`);
  process.exit(1);
}

const unreachable = (what, error) =>
  `No answer from ${what} (${error?.cause?.code ?? error?.name ?? 'error'}). Is the AI service running at ${options.url}?`;

async function postJson(path, body) {
  const started = performance.now();
  const res = await fetch(`${options.url}${path}`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body),
    signal: AbortSignal.timeout(CLASSIFY_TIMEOUT_MS),
  });
  let json = null;
  try {
    json = await res.json();
  } catch {
    // Not JSON; answerProblem reports the status.
  }
  return { status: res.status, body: json, durationMs: performance.now() - started };
}

function fail(message) {
  console.error(message);
  process.exit(1);
}

let health;
try {
  const res = await fetch(`${options.url}/health`, { signal: AbortSignal.timeout(HEALTH_TIMEOUT_MS) });
  if (!res.ok) fail(`${options.url}/health answered HTTP ${res.status}.`);
  health = await res.json();
} catch (error) {
  fail(error instanceof SyntaxError ? `${options.url}/health did not answer JSON.` : unreachable(`${options.url}/health`, error));
}

const { targets, skipped, note } = planTargets(health);
const request = toClassifyRequest(lines);
const summaries = [];
for (const target of targets) {
  const runs = [];
  let error = null;
  for (let run = 0; run < options.runs && error === null; run++) {
    try {
      const answer = await postJson(classifyPath(target), request);
      error = answerProblem(target, answer.status, answer.body);
      if (error === null) {
        const scored = scoreAnswers(lines, answer.body.classifications, { modelRun: target.id !== null });
        runs.push({ durationMs: answer.durationMs, scored });
      }
    } catch (caught) {
      error = caught?.name === 'TimeoutError' ? `no answer within ${CLASSIFY_TIMEOUT_MS / 1000} s` : unreachable('/classify', caught);
    }
  }
  console.error(`${targetName(target)}: ${error ?? `${runs.length} ${runs.length === 1 ? 'call' : 'calls'} scored`}`);
  summaries.push(summarizeTarget(target, runs, error, order));
}

const now = new Date();
const date = [now.getFullYear(), now.getMonth() + 1, now.getDate()].map((n) => String(n).padStart(2, '0')).join('-');
const report = markdownReport({
  date,
  url: options.url,
  linesFile: LINES_FILE,
  lineCount: lines.length,
  runs: options.runs,
  note,
  skipped,
  summaries,
  lineCounts: categoryLineCounts(lines, order),
});
console.log(report);
if (options.write) {
  writeFileSync(join(root, REPORT_FILE), report);
  console.error(`Saved ${REPORT_FILE}.`);
}
process.exitCode = summaries.some((s) => s.runs > 0) ? 0 : 1;
