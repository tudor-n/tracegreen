// Release gate before tagging v0.9-prehack (roadmap T22; docs/plan/dev-c.md C6). Prints PASS or FAIL per check, with the
// file and line of every finding, and exits 1 while any check fails. It is meant to fail until T4 is done.
// Usage: node scripts/preflight.mjs   (from anywhere in the repository; needs git). No dependencies.
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { isExempt } from './lib/banned-terms.mjs';
import {
  bannedTermFindings,
  carbonPriceAgreement,
  carbonPriceFindings,
  carbonPriceRow,
  carbonPriceValues,
  ETS_PLACES,
  factorDocMismatches,
  placeholderFactors,
} from './lib/preflight-checks.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const read = (file) => readFileSync(join(root, file), 'utf8');
const FACTORS = 'seed/factors.json';
const DOC = 'docs/factor-sources.md';

/** Tracked text files outside the documents that state the rules, and how many were left out. */
function trackedTextFiles() {
  const files = execFileSync('git', ['ls-files', '-z'], { cwd: root, encoding: 'utf8' }).split('\0').filter(Boolean);
  const texts = [];
  for (const file of files) {
    if (isExempt(file)) continue;
    const bytes = readFileSync(join(root, file));
    if (bytes.subarray(0, 8000).includes(0)) continue; // binary
    texts.push({ file, text: bytes.toString('utf8') });
  }
  return { texts, skipped: files.length - texts.length };
}

const checks = [
  {
    title: `Every factor row names a real source (${FACTORS})`,
    run: () => ({ findings: placeholderFactors(read(FACTORS), FACTORS) }),
  },
  {
    title: `${DOC} matches ${FACTORS}`,
    run: () => ({ findings: factorDocMismatches(read(DOC), DOC, read(FACTORS)) }),
  },
  {
    title: `The carbon price has a source and the date it was set (${DOC})`,
    run: () => ({ findings: carbonPriceFindings(read(DOC), DOC) }),
  },
  {
    title: 'The carbon price is the same everywhere it is set',
    run: () => {
      const row = carbonPriceRow(read(DOC));
      const values = carbonPriceValues(Object.fromEntries(ETS_PLACES.map(({ file }) => [file, read(file)])));
      const findings = carbonPriceAgreement(values, row?.value ?? '', DOC, row?.line ?? null);
      return { findings, note: findings.length === 0 ? `${values[0].value} in ${values.length} files and ${DOC}` : '' };
    },
  },
  {
    title: 'No banned term in a tracked file (PRIORITY-CHANGES.md §4)',
    run: () => {
      const { texts, skipped } = trackedTextFiles();
      return {
        findings: texts.flatMap(({ file, text }) => bannedTermFindings(file, text)),
        note: `${texts.length} files read; ${skipped} rule documents or binary files skipped`,
      };
    },
  },
];

console.log(`TraceGreen preflight, ${new Date().toISOString().slice(0, 10)}\n`);
let failed = 0;
for (const { title, run } of checks) {
  let result;
  try {
    result = run();
  } catch (error) {
    result = { findings: [{ file: null, line: null, message: `the check could not run: ${error.message}` }] };
  }
  const ok = result.findings.length === 0;
  if (!ok) failed++;
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${title}${ok && result.note ? `  (${result.note})` : ''}`);
  for (const { file, line, message } of result.findings) {
    const where = file === null ? '' : `${file}${line === null ? '' : `:${line}`}  `;
    console.log(`      ${where}${message}`);
  }
}

console.log(failed === 0 ? '\nAll checks pass.' : `\n${failed} of ${checks.length} checks failed. v0.9-prehack cannot be tagged yet.`);
process.exitCode = failed === 0 ? 0 : 1;
