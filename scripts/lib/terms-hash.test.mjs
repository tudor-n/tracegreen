// Run with `node --test scripts/lib/*.test.mjs`. No dependencies.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { canonicalJson, termsHash } from './terms-hash.mjs';

const seed = (file) => readFileSync(new URL(`../../seed/${file}`, import.meta.url), 'utf8');

test('canonical JSON escapes strings as the C# CanonicalJson does (same probe as CanonicalJsonAndHashingTests)', () => {
  const probe = { z: "<>&'+`\"", a: '\\ \n\t é ș „” § 😀', n: [1, true, null], B: 'x' };
  // The C# test parses this exact text.
  assert.equal(JSON.stringify(probe), '{"z":"<>&\'+`\\"","a":"\\\\ \\n\\t é ș „” § 😀","n":[1,true,null],"B":"x"}');
  assert.equal(
    canonicalJson(probe),
    '{"B":"x","a":"\\\\ \\n\\t \\u00E9 \\u0219 \\u201E\\u201D \\u00A7 \\uD83D\\uDE00","n":[1,true,null],"z":"\\u003C\\u003E\\u0026\\u0027\\u002B\\u0060\\u0022"}',
  );
});

test('the seed terms hash to the SHA-256 their version is pinned to, as the API computes it', () => {
  const { version, sha256 } = termsHash(seed('supplier-terms.json'));
  assert.equal(JSON.parse(seed('supplier-terms-versions.json'))[version], sha256);
});

test('reformatting keeps the hash and a changed word changes it', () => {
  const text = seed('supplier-terms.json');
  const { sha256 } = termsHash(text);
  assert.equal(termsHash(JSON.stringify(JSON.parse(text))).sha256, sha256);
  assert.notEqual(termsHash(text.replace('Romanian law', 'Romanian laws')).sha256, sha256);
});
