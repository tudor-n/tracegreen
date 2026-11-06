// Scores the AI service's models on the Romanian test set (F6; the results and the rules are in docs/ai-eval.md).
// Usage: node scripts/ai-eval.mjs [--url http://localhost:8000] [--runs 1] [--batch <lines per call>]
//        [--note <text>] [--write [--force]]
// It reads GET /health for the ready models, then sends every line of ai/eval/ro-lines.json once per run to
// POST /classify?model=<id> for each model (the dev route: start the service with AI_DEV_ROUTES=1), in a seeded new
// order per run and `--batch` lines per call (all of them in one call by default), and times each call. A service
// without /health.models (the stub before F6) or without a ready model is scored on its default chain,
// POST /classify. It prints a Markdown report; --write also saves it to docs/ai-eval.md, but keeps the saved report
// when a model did not finish or left lines to the stub, unless --force. --note adds a line to the report, e.g. how
// the service was set up. Exits 1 when the test set is invalid, the service does not answer, nothing could be scored
// or the report was not saved; low scores are flagged in the report, not in the exit code. Node 24+, no dependencies.
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  USAGE,
  answerProblem,
  callPlan,
  categoryLineCounts,
  classifyPath,
  commandLine,
  markdownReport,
  parseArgs,
  planTargets,
  scoreAnswers,
  summarizeTarget,
  targetName,
  testSetProblems,
  toClassifyRequest,
  writeProblem,
} from './lib/ai-eval.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const LINES_FILE = 'ai/eval/ro-lines.json';
const REPORT_FILE = 'docs/ai-eval.md';
const HEALTH_TIMEOUT_MS = 5000;
// The service caps one /classify call at AI_TIMEOUT_S (8 s by default, raised for slow local models when scoring);
// this only stops the script hanging on a stuck service.
const CLASSIFY_TIMEOUT_MS = 120000;

function fail(message) {
  console.error(message);
  process.exit(1);
}

const options = parseArgs(process.argv.slice(2));
if (options.error !== undefined) fail(`${options.error}\n${USAGE}`);
if (options.help) {
  console.log(USAGE);
  process.exit(0);
}

const readJson = (file) => JSON.parse(readFileSync(join(root, file), 'utf8'));
const lines = readJson(LINES_FILE);
const order = readJson('seed/categories.json').map((category) => category.id);
const problems = testSetProblems(lines, order);
if (problems.length > 0) fail(`${LINES_FILE} cannot be scored:\n${problems.map((p) => `- ${p}`).join('\n')}`);

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

let health;
try {
  const res = await fetch(`${options.url}/health`, { signal: AbortSignal.timeout(HEALTH_TIMEOUT_MS) });
  if (!res.ok) fail(`${options.url}/health answered HTTP ${res.status}.`);
  health = await res.json();
} catch (error) {
  fail(error instanceof SyntaxError ? `${options.url}/health did not answer JSON.` : unreachable(`${options.url}/health`, error));
}

const { targets, skipped, note } = planTargets(health);
const plan = callPlan(lines, options.runs, options.batch);
const summaries = [];
for (const target of targets) {
  const runs = [];
  let error = null;
  for (const calls of plan) {
    const run = { durationsMs: [], scored: [] };
    for (const callLines of calls) {
      try {
        const answer = await postJson(classifyPath(target), toClassifyRequest(callLines));
        error = answerProblem(target, answer.status, answer.body);
        if (error !== null) break;
        run.durationsMs.push(answer.durationMs);
        run.scored.push(...scoreAnswers(callLines, answer.body.classifications, { modelRun: target.id !== null }));
      } catch (caught) {
        error = caught?.name === 'TimeoutError' ? `no answer within ${CLASSIFY_TIMEOUT_MS / 1000} s` : unreachable('/classify', caught);
        break;
      }
    }
    if (error !== null) break;
    runs.push(run);
  }
  const calls = runs.reduce((sum, run) => sum + run.durationsMs.length, 0);
  console.error(`${targetName(target)}: ${runs.length} of ${plan.length} runs scored (${calls} ${calls === 1 ? 'call' : 'calls'})${error === null ? '' : `; ${error}`}`);
  summaries.push(summarizeTarget(target, runs, error, order));
}

const now = new Date();
const date = [now.getFullYear(), now.getMonth() + 1, now.getDate()].map((n) => String(n).padStart(2, '0')).join('-');
const report = markdownReport({
  date,
  url: options.url,
  command: commandLine(options),
  linesFile: LINES_FILE,
  lineCount: lines.length,
  runs: options.runs,
  batch: options.batch,
  notes: [note, options.note],
  skipped,
  summaries,
  lineCounts: categoryLineCounts(lines, order),
});
console.log(report);
let saved = true;
if (options.write) {
  const problem = writeProblem(summaries);
  if (problem !== null && !options.force) {
    saved = false;
    console.error(`${REPORT_FILE} was kept as it was: ${problem} Pass --force to save this report anyway.`);
  } else {
    writeFileSync(join(root, REPORT_FILE), report);
    console.error(`Saved ${REPORT_FILE}.`);
  }
}
process.exitCode = summaries.some((s) => s.runs > 0) && saved ? 0 : 1;
