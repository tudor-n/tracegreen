// Run with `node --test scripts/lib/`. No dependencies.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { findBannedTerms, isExempt } from './banned-terms.mjs';

const terms = (text) => findBannedTerms(text).map((f) => f.term);

test("ruling 2's disclaimer passes word for word", () => {
  const tooltip =
    'share of total kgCO₂e from nodes the supplier confirmed — activity data from official factors, not externally verified PCFs';
  assert.deepEqual(terms(tooltip), []);
  assert.deepEqual(terms('Not  Externally\nverified PCF'), []);
});

test('every other use of verified PCF stays banned', () => {
  for (const text of ['verified PCF', 'Verified PCFs on every node', 'externally verified PCFs', 'not verified PCFs']) {
    assert.deepEqual(terms(text), ['verified PCF'], text);
  }
  assert.deepEqual(terms('not externally verified PCFs, unlike verified PCFs'), ['verified PCF']);
});

test('the other plain terms are still caught', () => {
  assert.deepEqual(terms('our block-chain'), ['blockchain']);
  assert.deepEqual(terms('Primary  data share'), ['primary data']);
});

test('the rule files are exempt, the copy module is not', () => {
  assert.ok(isExempt('scripts/lib/banned-terms.test.mjs'));
  assert.ok(isExempt('docs/plan/dev-c.md'));
  assert.ok(!isExempt('web/src/copy/labels.ts'));
});
