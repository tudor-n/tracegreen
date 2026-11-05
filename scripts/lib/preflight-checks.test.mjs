// Run with `node --test scripts/lib/`. No dependencies.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  bannedTermFindings,
  carbonPriceAgreement,
  carbonPriceFindings,
  carbonPriceValues,
  factorDocMismatches,
  markdownTables,
  placeholderFactors,
} from './preflight-checks.mjs';

const factorsJson = JSON.stringify(
  [
    { id: 'transport.sea', kind: 'transport', key: 'sea', value: 0.016, unit: 'kgCO2e/t.km', source: 'PLACEHOLDER - replace at T4', year: 2026, rowLabel: 'Sea (placeholder)' },
    { id: 'spend.pcb_bare', kind: 'spend', key: 'pcb_bare', value: 0.098, unit: 'kgCO2e/USD', source: 'US EPA v1.3 | with margins', year: 2024, rowLabel: '334412 Bare PCB' },
  ],
  null,
  2,
);

const doc = [
  '# Factor sources',
  '',
  '| id | key | value | unit | source | year | row label |',
  '|---|---|---|---|---|---|---|',
  '| `transport.sea` | sea | 0.016 | kgCO2e/t.km | PLACEHOLDER - replace at T4 | 2026 (placeholder) | Sea (placeholder) |',
  '| `spend.pcb_bare` | pcb_bare | 0.0980 | kgCO2e/USD | US EPA v1.3 \\| with margins | 2024 | 334412 Bare PCB |',
  '',
  '| name | value | unit | benchmark | source | date set |',
  '|---|---|---|---|---|---|',
  '| `ETS_PRICE_EUR_PER_T` | 70 | EUR/tCO2e | front-December EUA | PLACEHOLDER |  |',
].join('\n');

test('a placeholder source is reported at its line in factors.json', () => {
  assert.deepEqual(placeholderFactors(factorsJson, 'seed/factors.json'), [
    { file: 'seed/factors.json', line: 8, message: 'transport.sea: the source is still a placeholder' },
  ]);
});

test('markdown tables keep their header, skip the separator and unescape a bar', () => {
  const rows = markdownTables(doc);
  assert.equal(rows.length, 3);
  assert.equal(rows[1].line, 6);
  assert.equal(rows[1].cells.source, 'US EPA v1.3 | with margins');
});

test('the doc matches factors.json field by field, values as numbers, the placeholder year marker ignored', () => {
  assert.deepEqual(factorDocMismatches(doc, 'docs/factor-sources.md', factorsJson), []);
});

test('a changed field and a row missing on either side are reported', () => {
  const changed = doc.replace('| 0.016 |', '| 0.02 |').replace(/\| `spend\.pcb_bare`.*\n/, '| `spend.extra` | x | 1 | u | s | 2024 | r |\n');
  const messages = factorDocMismatches(changed, 'docs/factor-sources.md', factorsJson).map((f) => `${f.line}: ${f.message}`);
  assert.deepEqual(messages, [
    '5: transport.sea: value is "0.02" here but "0.016" in factors.json',
    'null: spend.pcb_bare is in factors.json but has no row here',
    '6: spend.extra has a row here but is not in factors.json',
  ]);
});

test('the carbon price needs a source and a date', () => {
  assert.deepEqual(
    carbonPriceFindings(doc, 'd.md').map((f) => f.message),
    [
      'ETS_PRICE_EUR_PER_T: no source yet (the 70 is a placeholder)',
      'ETS_PRICE_EUR_PER_T: no date set (write it as YYYY-MM-DD)',
    ],
  );
  const set = doc.replace('| PLACEHOLDER |  |', '| ICE Endex settlement, Dec-26 contract | 2026-11-05 |');
  assert.deepEqual(carbonPriceFindings(set, 'd.md'), []);
});

const places = (demo, example, compose, appConfig) => ({
  '.env.demo': `LINK_SECRET=x\nETS_PRICE_EUR_PER_T=${demo}\n`,
  '.env.example': `ETS_PRICE_EUR_PER_T=${example}          # placeholder benchmark\n`,
  'compose.yml': `services:\n  api:\n    environment:\n      ETS_PRICE_EUR_PER_T: "${compose}"\n`,
  'api/src/TraceGreen.Api/AppConfig.cs': `EtsPriceEurPerT: GetDecimal(env, "ETS_PRICE_EUR_PER_T", ${appConfig}m),`,
});

test('the price is read from all four places, with their lines', () => {
  assert.deepEqual(carbonPriceValues(places('70', '70', '70', '70')), [
    { file: '.env.demo', line: 2, value: '70' },
    { file: '.env.example', line: 1, value: '70' },
    { file: 'compose.yml', line: 4, value: '70' },
    { file: 'api/src/TraceGreen.Api/AppConfig.cs', line: 1, value: '70' },
  ]);
});

test('the price agrees everywhere, or every place is listed', () => {
  assert.deepEqual(carbonPriceAgreement(carbonPriceValues(places('70', '70', '70', '70')), '70.0', 'd.md', 9), []);

  const blank = carbonPriceAgreement(carbonPriceValues(places('', '70', '70', '70')), '70', 'd.md', 9);
  assert.deepEqual(blank.map((f) => `${f.file}: ${f.message}`), ['.env.demo: ETS_PRICE_EUR_PER_T is blank']);

  const drift = carbonPriceAgreement(carbonPriceValues(places('82.4', '70', '70', '70')), '82.4', 'd.md', 9);
  assert.equal(drift.length, 5);
  assert.ok(drift.some((f) => f.file === 'compose.yml' && f.message === 'ETS_PRICE_EUR_PER_T is 70 here'));
});

test('banned terms are reported by line, and a phrase split across lines for the whole file', () => {
  assert.deepEqual(bannedTermFindings('a.md', 'fine\nour blockchain ledger\n'), [
    { file: 'a.md', line: 2, message: 'blockchain: say "tamper-evident audit log"' },
  ]);
  const split = bannedTermFindings('b.md', 'no primary\ndata here');
  assert.equal(split.length, 1);
  assert.equal(split[0].line, null);
  assert.deepEqual(bannedTermFindings('c.md', 'a tamper-evident audit log'), []);
});
