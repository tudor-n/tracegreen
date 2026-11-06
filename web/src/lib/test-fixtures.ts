import type { SupplierTerms, TermsAcceptance, VerifyTerms } from '@/api/client'

// Fixtures shared by the supplier-page tests. Typechecked with the app, since it is not a *.test.ts file, so a change in
// the generated schema fails here once; imported by tests alone, so it never reaches the bundle.

export const acceptance: TermsAcceptance = { before: 'I accept the ', link: 'Supplier Terms', after: '.' }

const text = {
  title: 'Supplier Terms',
  keyPoints: ['Your answers are your responsibility.'],
  acceptance,
  sections: [{ number: 1, heading: 'Parties', paragraphs: ['The operator and the supplier.'] }],
}

/** What GET /api/terms/supplier serves. */
export const supplierTerms: SupplierTerms = { version: '2026-11-06', sha256: 'a'.repeat(64), ro: text, en: text }

/** What the verify context carries: the checkbox statement and the fingerprint. */
export const verifyTerms: VerifyTerms = {
  version: supplierTerms.version,
  sha256: supplierTerms.sha256,
  ro: acceptance,
  en: acceptance,
}
