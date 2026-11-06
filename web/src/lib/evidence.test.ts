import { describe, expect, it } from 'vitest'
import type { ShippingDocType, TransportMode } from '@/api/client'
import { verifyCopy } from '@/copy/verify'
import {
  CERT_TYPES,
  certIdProblem,
  dispatchDateBounds,
  dispatchDateProblem,
  docIdProblem,
  docTypesFor,
  EMPTY_EVIDENCE,
  type EvidenceDraft,
  evidenceBody,
  evidenceForMode,
  evidenceProblems,
  fittingDocType,
  hasEvidenceProblem,
  isEmptyEvidence,
  isoCheckDigit,
  NO_EVIDENCE_PROBLEMS,
  normalizeDocId,
  problemLines,
  proofApplies,
  setEvidence,
  shownCertType,
  shownDocType,
  todayUtc,
} from './evidence'

const draft = (overrides: Partial<EvidenceDraft> = {}): EvidenceDraft => ({ ...EMPTY_EVIDENCE, ...overrides })
const sea = { transport: 'sea', energy: 'grid' } as const
const ISSUE = '2026-10-28'
const TODAY = '2026-11-06'

describe('docTypesFor', () => {
  it('offers the documents that fit each mode, most usual first', () => {
    expect(docTypesFor('sea')).toEqual(['containerNumber', 'billOfLading'])
    expect(docTypesFor('air')).toEqual(['airWaybill'])
    expect(docTypesFor('rail')).toEqual(['railConsignment', 'containerNumber'])
    expect(docTypesFor('road')).toEqual(['cmr', 'eTransportUit', 'containerNumber'])
    expect(docTypesFor(null)).toEqual([])
  })

  // The API's DocumentIds.Fits, row by row.
  it.each<[ShippingDocType, TransportMode[]]>([
    ['containerNumber', ['sea', 'rail', 'road']],
    ['airWaybill', ['air']],
    ['billOfLading', ['sea']],
    ['cmr', ['road']],
    ['eTransportUit', ['road']],
    ['railConsignment', ['rail']],
  ])('fits %s to %j alone', (type, modes) => {
    for (const mode of ['road', 'rail', 'sea', 'air'] as const) {
      expect(fittingDocType(type, mode)).toBe(modes.includes(mode) ? type : null)
    }
    expect(fittingDocType(type, null)).toBeNull()
  })
})

describe('the type shown', () => {
  it("shows the mode's usual document until the supplier picks another, and nothing without transport", () => {
    expect(shownDocType(null, 'sea')).toBe('containerNumber')
    expect(shownDocType(null, 'air')).toBe('airWaybill')
    expect(shownDocType(null, 'rail')).toBe('railConsignment')
    expect(shownDocType(null, 'road')).toBe('cmr')
    expect(shownDocType('billOfLading', 'sea')).toBe('billOfLading')
    expect(shownDocType('billOfLading', 'air')).toBeNull()
    expect(shownDocType('containerNumber', null)).toBeNull()
  })

  it('shows the first certificate type, or the one picked, for a renewable answer alone', () => {
    expect(shownCertType(null, 'renewable')).toBe('guaranteeOfOrigin')
    expect(shownCertType('iRec', 'renewable')).toBe('iRec')
    expect(shownCertType('iRec', 'grid')).toBeNull()
    expect(shownCertType(null, null)).toBeNull()
  })
})

describe('evidenceForMode', () => {
  const typed = draft({ docId: 'CSQU3054383', dispatchDate: '2026-10-20' })

  it('keeps a typed number with its document while it fits the new answer', () => {
    // Typed under sea as the usual container number; rail and road carry containers too.
    expect(evidenceForMode(typed, 'sea', 'rail')).toEqual({ ...typed, docType: 'containerNumber' })
    expect(evidenceForMode(typed, 'sea', 'road')).toEqual({ ...typed, docType: 'containerNumber' })
    expect(evidenceForMode(typed, 'sea', 'sea')).toBe(typed)
  })

  it('drops a typed number whose document does not fit, rather than read it as another', () => {
    expect(evidenceForMode(typed, 'sea', 'air')).toEqual({ ...typed, docType: null, docId: '' })
    const bill = draft({ docType: 'billOfLading', docId: 'MAEU123456' })
    expect(evidenceForMode(bill, 'sea', 'road')).toEqual(draft())
  })

  it('remembers the document while transport is skipped', () => {
    const skipped = evidenceForMode(typed, 'sea', null)
    expect(skipped).toEqual({ ...typed, docType: 'containerNumber' })
    expect(evidenceForMode(skipped, null, 'sea')).toBe(skipped)
    expect(evidenceForMode(skipped, null, 'air')).toEqual({ ...typed, docType: null, docId: '' })
  })

  it("with no number typed, keeps a picked type while it fits, else shows the new answer's usual one", () => {
    const picked = draft({ docType: 'containerNumber' })
    expect(evidenceForMode(picked, 'road', 'sea')).toBe(picked)
    expect(evidenceForMode(picked, 'road', 'air')).toEqual(draft())
    expect(evidenceForMode(EMPTY_EVIDENCE, 'sea', 'air')).toBe(EMPTY_EVIDENCE)
  })
})

describe('normalizeDocId', () => {
  it('uppercases and drops spaces, and for containers and air waybills hyphens and dots too', () => {
    expect(normalizeDocId('containerNumber', ' csqu 305438-3 ')).toBe('CSQU3054383')
    expect(normalizeDocId('airWaybill', '020-1234.5675')).toBe('02012345675')
    expect(normalizeDocId('billOfLading', 'mae-12 34/56')).toBe('MAE-1234/56')
  })
})

describe('docIdProblem', () => {
  it('passes a container number whose ISO 6346 check digit matches', () => {
    expect(isoCheckDigit('CSQU3054383')).toBe(3)
    expect(docIdProblem('containerNumber', 'CSQU3054383')).toBeNull()
    expect(docIdProblem('containerNumber', 'csqu 305438 3')).toBeNull()
  })

  it('refuses a container number with the wrong check digit or shape', () => {
    expect(docIdProblem('containerNumber', 'CSQU3054384')).toBe('checkDigit')
    expect(docIdProblem('containerNumber', 'CSQA3054383')).toBe('shape')
    expect(docIdProblem('containerNumber', 'CSQU305438')).toBe('shape')
    expect(docIdProblem('containerNumber', '')).toBe('shape')
  })

  it('passes an air waybill whose serial mod 7 is its last digit', () => {
    expect(docIdProblem('airWaybill', '02012345675')).toBeNull()
    expect(docIdProblem('airWaybill', '020-1234 5675')).toBeNull()
  })

  it('refuses an air waybill with the wrong check digit or shape', () => {
    expect(docIdProblem('airWaybill', '02012345676')).toBe('checkDigit')
    expect(docIdProblem('airWaybill', '0201234567')).toBe('shape')
    expect(docIdProblem('airWaybill', '0201234567A')).toBe('shape')
  })

  it.each<[ShippingDocType, number, number]>([
    ['billOfLading', 6, 20],
    ['cmr', 4, 20],
    ['eTransportUit', 8, 20],
    ['railConsignment', 4, 20],
  ])('checks the length and characters of a %s (%i to %i)', (type, min, max) => {
    expect(docIdProblem(type, 'A'.repeat(min))).toBeNull()
    expect(docIdProblem(type, '1'.repeat(max))).toBeNull()
    expect(docIdProblem(type, 'AB-12/34'.padEnd(min, '9'))).toBeNull()
    expect(docIdProblem(type, 'A'.repeat(min - 1))).toBe('shape')
    expect(docIdProblem(type, 'A'.repeat(max + 1))).toBe('shape')
    expect(docIdProblem(type, 'AB.12345')).toBe('shape')
  })
})

describe('certIdProblem', () => {
  it('takes 4 to 64 letters, digits and - _ . / :, trimmed', () => {
    expect(certIdProblem(' GO-2026/RO:0001_a.b ')).toBeNull()
    expect(certIdProblem('abcd')).toBeNull()
    expect(certIdProblem('x'.repeat(64))).toBeNull()
    expect(certIdProblem('abc')).toBe('shape')
    expect(certIdProblem('x'.repeat(65))).toBe('shape')
    expect(certIdProblem('GO 2026')).toBe('shape')
  })
})

describe('dispatch date', () => {
  it('takes a date up to tomorrow and back to 366 days before the invoice', () => {
    expect(dispatchDateProblem('2026-11-07', ISSUE, TODAY)).toBeNull()
    expect(dispatchDateProblem('2025-10-27', ISSUE, TODAY)).toBeNull()
    expect(dispatchDateProblem('2026-11-08', ISSUE, TODAY)).toBe('future')
    expect(dispatchDateProblem('2025-10-26', ISSUE, TODAY)).toBe('tooEarly')
  })

  it('refuses what is not a calendar date', () => {
    expect(dispatchDateProblem('2026-02-30', ISSUE, TODAY)).toBe('invalid')
    expect(dispatchDateProblem('28.10.2026', ISSUE, TODAY)).toBe('invalid')
  })

  it("gives the date input the same window, and today's date in UTC", () => {
    expect(dispatchDateBounds(ISSUE, TODAY)).toEqual({ min: '2025-10-27', max: '2026-11-07' })
    expect(dispatchDateBounds('nope', TODAY)).toEqual({ min: undefined, max: '2026-11-07' })
    expect(todayUtc(new Date('2026-11-06T23:30:00-05:00'))).toBe('2026-11-07')
  })
})

describe('setEvidence', () => {
  it('sets a fitting document type and clears it with an empty choice', () => {
    const set = setEvidence(EMPTY_EVIDENCE, 'docType', 'billOfLading', sea)
    expect(set.docType).toBe('billOfLading')
    expect(setEvidence(set, 'docType', '', sea).docType).toBeNull()
  })

  it('leaves the proof unchanged for a type that does not fit or does not exist', () => {
    expect(setEvidence(EMPTY_EVIDENCE, 'docType', 'airWaybill', sea)).toBe(EMPTY_EVIDENCE)
    expect(setEvidence(EMPTY_EVIDENCE, 'docType', 'containerNumber', { transport: null, energy: 'grid' })).toBe(EMPTY_EVIDENCE)
    expect(setEvidence(EMPTY_EVIDENCE, 'certType', 'toString', sea)).toBe(EMPTY_EVIDENCE)
  })

  it('keeps the typed text as typed', () => {
    const typed = setEvidence(EMPTY_EVIDENCE, 'docId', ' csqu ', sea)
    expect(typed).toEqual(draft({ docId: ' csqu ' }))
    expect(setEvidence(typed, 'certType', 'iRec', sea).certType).toBe('iRec')
  })
})

describe('evidenceProblems', () => {
  const problems = (d: EvidenceDraft, answers: Parameters<typeof evidenceProblems>[1] = sea) =>
    evidenceProblems(d, answers, ISSUE, TODAY)

  it('finds nothing in an untouched section', () => {
    expect(problems(EMPTY_EVIDENCE)).toEqual(NO_EVIDENCE_PROBLEMS)
    expect(hasEvidenceProblem(problems(EMPTY_EVIDENCE))).toBe(false)
    expect(isEmptyEvidence(EMPTY_EVIDENCE)).toBe(true)
    expect(isEmptyEvidence(draft({ dispatchDate: '2026-10-20' }))).toBe(false)
  })

  it('checks a typed number as the document shown, and leaves an empty one alone', () => {
    expect(problems(draft({ docType: 'billOfLading' }))).toEqual(NO_EVIDENCE_PROBLEMS)
    // The usual document for sea is a container.
    expect(problems(draft({ docId: 'CSQU3054384' })).docId).toBe('checkDigit')
    expect(problems(draft({ docId: 'CSQU3054383' }))).toEqual(NO_EVIDENCE_PROBLEMS)
    expect(problems(draft({ docType: 'billOfLading', docId: 'MAEU1234' }))).toEqual(NO_EVIDENCE_PROBLEMS)
  })

  it('ignores what the page hides: a document that no longer fits, any shipping proof without transport', () => {
    const bad = draft({ docType: 'containerNumber', docId: 'nope', dispatchDate: '2030-01-01' })
    expect(problems(bad, { transport: 'air', energy: 'grid' })).toMatchObject({ docId: null, dispatchDate: 'future' })
    expect(problems(bad, { transport: null, energy: 'grid' })).toEqual(NO_EVIDENCE_PROBLEMS)
    expect(problems(draft({ docId: 'CSQU3054383' }), { transport: null, energy: 'grid' })).toEqual(NO_EVIDENCE_PROBLEMS)
  })

  it('checks a certificate only with renewable power', () => {
    const cert = draft({ certId: 'ab' })
    expect(problems(cert, { transport: 'sea', energy: 'renewable' }).certId).toBe('shape')
    expect(problems(draft({ certType: 'ppa' }), { transport: null, energy: 'renewable' })).toEqual(NO_EVIDENCE_PROBLEMS)
    expect(problems(cert, sea)).toEqual(NO_EVIDENCE_PROBLEMS)
  })
})

describe('evidenceBody', () => {
  it('is null when the supplier gave nothing', () => {
    expect(evidenceBody(EMPTY_EVIDENCE, sea)).toBeNull()
    expect(evidenceBody(draft({ docId: '  ', certId: ' ' }), { transport: 'sea', energy: 'renewable' })).toBeNull()
  })

  it('sends the document normalised, the date and the trimmed certificate', () => {
    const full = draft({
      docType: 'containerNumber',
      docId: 'csqu 305438-3',
      dispatchDate: '2026-10-01',
      certType: 'iRec',
      certId: ' IREC-2026-0001 ',
    })
    expect(evidenceBody(full, { transport: 'sea', energy: 'renewable' })).toEqual({
      shippingDocType: 'containerNumber',
      shippingDocId: 'CSQU3054383',
      dispatchDate: '2026-10-01',
      energyCertType: 'iRec',
      energyCertId: 'IREC-2026-0001',
    })
  })

  it('drops a document that no longer fits the mode, and the certificate unless power is renewable', () => {
    const full = draft({ docType: 'billOfLading', docId: 'MAEU123456', dispatchDate: '2026-10-01', certType: 'ppa', certId: 'PPA-1' })
    expect(evidenceBody(full, { transport: 'road', energy: 'grid' })).toEqual({
      shippingDocType: null,
      shippingDocId: null,
      dispatchDate: '2026-10-01',
      energyCertType: null,
      energyCertId: null,
    })
    expect(evidenceBody(full, { transport: null, energy: 'unknown' })).toBeNull()
  })

  it('drops a type without its number', () => {
    expect(evidenceBody(draft({ docType: 'billOfLading' }), sea)).toBeNull()
  })

  it('sends a number typed under the type shown first', () => {
    expect(evidenceBody(draft({ docId: '00012345675', certId: 'GO-1' }), { transport: 'air', energy: 'renewable' })).toEqual({
      shippingDocType: 'airWaybill',
      shippingDocId: '00012345675',
      dispatchDate: null,
      energyCertType: 'guaranteeOfOrigin',
      energyCertId: 'GO-1',
    })
  })
})

describe('proofApplies', () => {
  it('needs a transport answer or renewable power', () => {
    expect(proofApplies({ transport: 'road', energy: null })).toBe(true)
    expect(proofApplies({ transport: null, energy: 'renewable' })).toBe(true)
    expect(proofApplies({ transport: null, energy: 'grid' })).toBe(false)
  })

  it('lists every certificate type once', () => {
    expect(CERT_TYPES).toEqual(['guaranteeOfOrigin', 'iRec', 'chinaGec', 'taiwanTRec', 'ppa', 'onSite'])
  })
})

describe('problemLines', () => {
  it("names each field's problem in the language shown, and nothing where there is none", () => {
    for (const lang of ['ro', 'en'] as const) {
      const lines = verifyCopy[lang].proof.problems
      expect(problemLines(NO_EVIDENCE_PROBLEMS, null, lines)).toEqual({ docId: null, dispatchDate: null, certId: null })
      expect(problemLines({ docId: 'shape', dispatchDate: 'future', certId: 'shape' }, 'airWaybill', lines)).toEqual({
        docId: lines.docIdShape.airWaybill,
        dispatchDate: lines.dispatchDate.future,
        certId: lines.certIdShape,
      })
      expect(problemLines({ docId: 'checkDigit', dispatchDate: null, certId: null }, 'containerNumber', lines)).toEqual({
        docId: lines.checkDigit,
        dispatchDate: null,
        certId: null,
      })
    }
  })
})
