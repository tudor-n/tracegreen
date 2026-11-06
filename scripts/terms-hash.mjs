// Prints the version and SHA-256 fingerprint of the Supplier Terms, and whether seed/supplier-terms-versions.json pins
// that version to that hash (the API refuses to start otherwise). Use it to add a new version's entry, or to check a
// fingerprint from the audit log against a copy of the terms.
// Usage: node scripts/terms-hash.mjs [path/to/supplier-terms.json]   (default: the repository's seed). No dependencies.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { termsHash } from './lib/terms-hash.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const file = process.argv[2] ?? join(root, 'seed/supplier-terms.json');
const { version, sha256 } = termsHash(readFileSync(file, 'utf8'));
console.log(`${version} ${sha256}`);

const published = JSON.parse(readFileSync(join(root, 'seed/supplier-terms-versions.json'), 'utf8'));
if (published[version] === sha256) {
  console.log(`seed/supplier-terms-versions.json pins ${version} to this hash.`);
} else if (published[version] === undefined) {
  console.log(`seed/supplier-terms-versions.json has no entry for ${version}; add "${version}": "${sha256}" once the text is final.`);
  process.exitCode = 1;
} else {
  console.log(`seed/supplier-terms-versions.json pins ${version} to ${published[version]}: this text has changed, so give it a new version.`);
  process.exitCode = 1;
}
