import type { AnswerEvidence, EnergyCertType, EnergySource, ShippingDocType, TransportMode } from '@/api/client'
import type { ProofStrings } from '@/copy/verify'

// The optional proof a supplier can give with an answer (F7), pure: which documents fit a transport answer and which one
// the page shows first, the shape and check-digit rules for their numbers, the dispatch-date window, and the request's
// `evidence` field. These mirror api/src/TraceGreen.Domain/Trust/DocumentIds.cs and VerifyService.InvalidEvidence so the
// supplier sees a problem while typing; the API stays the authority. Both supplier pages (/verify and /supplier) use them.
// The page shows the most usual document for the answer and the first certificate type, so the supplier types a number
// and nothing more; a type counts only once its number is typed.

/** The two answers the proof backs: a shipping document goes with the transport answer, a certificate with renewable power. */
export interface EvidenceAnswers {
  transport: TransportMode | null
  energy: EnergySource | null
}

/**
 * The proof as the supplier types it. Empty strings are fields left alone, and nothing starts filled in. A null type
 * means the one the page shows first (shownDocType, shownCertType); a type is set when the supplier picks another.
 */
export interface EvidenceDraft {
  docType: ShippingDocType | null
  docId: string
  /** `YYYY-MM-DD` from the date input, or ''. */
  dispatchDate: string
  certType: EnergyCertType | null
  certId: string
}

export type EvidenceField = keyof EvidenceDraft

export const EMPTY_EVIDENCE: EvidenceDraft = { docType: null, docId: '', dispatchDate: '', certType: null, certId: '' }

/** Why a document number does not pass: its shape, or (container and air waybill numbers) its check digit. */
export type DocIdProblem = 'shape' | 'checkDigit'
export type CertIdProblem = 'shape'
export type DispatchDateProblem = 'invalid' | 'future' | 'tooEarly'

/**
 * What stops the proof from being sent, per field the supplier filled in. An empty field is fine, and a field the page
 * hides (a document while transport is skipped, a certificate unless power is renewable) never counts.
 */
export interface EvidenceProblems {
  docId: DocIdProblem | null
  dispatchDate: DispatchDateProblem | null
  certId: CertIdProblem | null
}

export const NO_EVIDENCE_PROBLEMS: EvidenceProblems = { docId: null, dispatchDate: null, certId: null }

// The documents that fit each mode, the most usual one on top. Containers also travel by rail and on trucks.
const DOC_TYPES: Record<TransportMode, readonly ShippingDocType[]> = {
  sea: ['containerNumber', 'billOfLading'],
  air: ['airWaybill'],
  rail: ['railConsignment', 'containerNumber'],
  road: ['cmr', 'eTransportUit', 'containerNumber'],
}

// Keyed by the generated enum, so a new certificate type fails the typecheck here and in the copy.
const CERT_ORDER: Record<EnergyCertType, number> = {
  guaranteeOfOrigin: 0,
  iRec: 1,
  chinaGec: 2,
  taiwanTRec: 3,
  ppa: 4,
  onSite: 5,
}

/** The certificate types, in a fixed order. */
export const CERT_TYPES = (Object.keys(CERT_ORDER) as EnergyCertType[]).sort((a, b) => CERT_ORDER[a] - CERT_ORDER[b])

/** The shipping documents that fit a transport answer, most usual first; none when transport is skipped. */
export function docTypesFor(mode: TransportMode | null): readonly ShippingDocType[] {
  return mode === null ? [] : DOC_TYPES[mode]
}

/** The chosen document type while it fits the transport answer, else null: a sea document says nothing about air. */
export function fittingDocType(type: ShippingDocType | null, mode: TransportMode | null): ShippingDocType | null {
  return type !== null && docTypesFor(mode).includes(type) ? type : null
}

/**
 * The document the page shows for a transport answer: the one the supplier picked, else the most usual one for the mode;
 * none while transport is skipped. A picked type that does not fit the answer shows nothing (it is never read as another).
 */
export function shownDocType(type: ShippingDocType | null, mode: TransportMode | null): ShippingDocType | null {
  if (mode === null) return null
  return type === null ? (docTypesFor(mode)[0] ?? null) : fittingDocType(type, mode)
}

/** The certificate the page shows for a renewable answer: the one the supplier picked, else the first; none otherwise. */
export function shownCertType(type: EnergyCertType | null, energy: EnergySource | null): EnergyCertType | null {
  return energy === 'renewable' ? (type ?? CERT_TYPES[0] ?? null) : null
}

/**
 * The proof after the transport answer changed. A typed number stays with the document it was typed for while that
 * document fits the new answer (or transport is skipped), and goes with it otherwise, so a container number is never sent
 * as an air waybill. With no number typed, a picked type stays while it fits, else the new answer's usual one is shown.
 */
export function evidenceForMode(evidence: EvidenceDraft, from: TransportMode | null, to: TransportMode | null): EvidenceDraft {
  if (from === to) return evidence
  const fits = (type: ShippingDocType) => to === null || docTypesFor(to).includes(type)
  if (evidence.docId.trim() === '') {
    return evidence.docType === null || fits(evidence.docType) ? evidence : { ...evidence, docType: null }
  }
  // While transport was skipped the type is the one kept from before.
  const typedFor = from === null ? evidence.docType : shownDocType(evidence.docType, from)
  if (typedFor !== null && fits(typedFor)) return typedFor === evidence.docType ? evidence : { ...evidence, docType: typedFor }
  return { ...evidence, docType: null, docId: '' }
}

/** Uppercase without spaces, and for container and air waybill numbers without hyphens and dots, as the API stores it. */
export function normalizeDocId(type: ShippingDocType, id: string): string {
  const compact = id.replace(/\s/g, '').toUpperCase()
  return type === 'containerNumber' || type === 'airWaybill' ? compact.replace(/[-.]/g, '') : compact
}

/** ISO 6346: letters valued from 10 upwards, skipping multiples of 11 (A=10, B=12 … Z=38). */
function letterValue(letter: string): number {
  let value = 10
  for (let c = 'A'.charCodeAt(0); c < letter.charCodeAt(0); c++) {
    value++
    if (value % 11 === 0) value++
  }
  return value
}

/** The ISO 6346 check digit of a container number's first ten characters: weights 2^i, sum mod 11 mod 10. */
export function isoCheckDigit(id: string): number {
  let sum = 0
  for (let i = 0; i < 10; i++) {
    const c = id.charAt(i)
    sum += (c >= '0' && c <= '9' ? Number(c) : letterValue(c)) * 2 ** i
  }
  return (sum % 11) % 10
}

/** The documents without a check digit: letters, digits, '-' and '/', within a length. */
function freeShape(min: number, max: number) {
  return (id: string): DocIdProblem | null => (id.length >= min && id.length <= max && /^[A-Z0-9/-]+$/.test(id) ? null : 'shape')
}

const DOC_RULES: Record<ShippingDocType, (id: string) => DocIdProblem | null> = {
  // Three letters for the owner, U, J or Z for the kind, six digits and the check digit.
  containerNumber: (id) => {
    if (!/^[A-Z]{3}[UJZ][0-9]{7}$/.test(id)) return 'shape'
    return isoCheckDigit(id) === Number(id.charAt(10)) ? null : 'checkDigit'
  },
  // The airline's three digits, a serial of seven and the check digit: the serial mod 7.
  airWaybill: (id) => {
    if (!/^[0-9]{11}$/.test(id)) return 'shape'
    return Number(id.slice(3, 10)) % 7 === Number(id.charAt(10)) ? null : 'checkDigit'
  },
  billOfLading: freeShape(6, 20),
  cmr: freeShape(4, 20),
  eTransportUit: freeShape(8, 20),
  railConsignment: freeShape(4, 20),
}

/** Why a document number is not a valid one of its type, or null when it is. The number is normalised first. */
export function docIdProblem(type: ShippingDocType, id: string): DocIdProblem | null {
  return DOC_RULES[type](normalizeDocId(type, id))
}

/** A certificate or contract id: 4 to 64 letters, digits and `- _ . / :`, trimmed. */
export function certIdProblem(id: string): CertIdProblem | null {
  return /^[A-Za-z0-9_./:-]{4,64}$/.test(id.trim()) ? null : 'shape'
}

const DAY_MS = 86_400_000

/** The day number of a `YYYY-MM-DD` date, or null when it is not a calendar date. */
function dayOf(iso: string): number | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso)
  if (match === null) return null
  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])]
  const date = new Date(Date.UTC(year, month - 1, day))
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) return null
  return date.getTime() / DAY_MS
}

function isoOf(day: number): string {
  return new Date(day * DAY_MS).toISOString().slice(0, 10)
}

/** Today's date as the API counts it (UTC). The API allows one day more, so a supplier east of UTC is not refused. */
export function todayUtc(now: Date): string {
  return now.toISOString().slice(0, 10)
}

/** Why a dispatch date is refused: not a date, after tomorrow, or more than 366 days before the invoice's issue date. */
export function dispatchDateProblem(date: string, issueDate: string, today: string): DispatchDateProblem | null {
  const day = dayOf(date.trim())
  if (day === null) return 'invalid'
  const now = dayOf(today)
  if (now !== null && day > now + 1) return 'future'
  const issued = dayOf(issueDate)
  if (issued !== null && day < issued - 366) return 'tooEarly'
  return null
}

/** The date input's `min` and `max`, the same window as dispatchDateProblem. */
export function dispatchDateBounds(issueDate: string, today: string): { min: string | undefined; max: string | undefined } {
  const issued = dayOf(issueDate)
  const now = dayOf(today)
  return { min: issued === null ? undefined : isoOf(issued - 366), max: now === null ? undefined : isoOf(now + 1) }
}

/** Shipping proof goes with a transport answer, a certificate with renewable power; with neither there is nothing to add. */
export function proofApplies(answers: EvidenceAnswers): boolean {
  return answers.transport !== null || answers.energy === 'renewable'
}

/** True when the supplier has not typed or chosen anything. */
export function isEmptyEvidence(evidence: EvidenceDraft): boolean {
  return (
    evidence.docType === null &&
    evidence.docId.trim() === '' &&
    evidence.dispatchDate.trim() === '' &&
    evidence.certType === null &&
    evidence.certId.trim() === ''
  )
}

/** What stops the proof the supplier can see from being sent. Empty fields are fine: the whole section is optional. */
export function evidenceProblems(
  evidence: EvidenceDraft,
  answers: EvidenceAnswers,
  issueDate: string,
  today: string,
): EvidenceProblems {
  const docType = shownDocType(evidence.docType, answers.transport)
  const docId = evidence.docId.trim()
  const date = evidence.dispatchDate.trim()
  const certType = shownCertType(evidence.certType, answers.energy)
  const certId = evidence.certId.trim()
  return {
    docId: docType === null || docId === '' ? null : docIdProblem(docType, docId),
    dispatchDate: answers.transport === null || date === '' ? null : dispatchDateProblem(date, issueDate, today),
    certId: certType === null || certId === '' ? null : certIdProblem(certId),
  }
}

export function hasEvidenceProblem(problems: EvidenceProblems): boolean {
  return problems.docId !== null || problems.dispatchDate !== null || problems.certId !== null
}

/** Each field's problem as the line the supplier reads under it, in the language shown; null where there is none. */
export function problemLines(
  problems: EvidenceProblems,
  docType: ShippingDocType | null,
  lines: ProofStrings['problems'],
): Record<keyof EvidenceProblems, string | null> {
  const { docId, dispatchDate, certId } = problems
  let docLine: string | null = null
  if (docId === 'checkDigit') docLine = lines.checkDigit
  else if (docId === 'shape' && docType !== null) docLine = lines.docIdShape[docType]
  return {
    docId: docLine,
    dispatchDate: dispatchDate === null ? null : lines.dispatchDate[dispatchDate],
    certId: certId === null ? null : lines.certIdShape,
  }
}

// Own keys only, so a value such as "toString" is never taken for a type.
function isDocType(value: string): value is ShippingDocType {
  return Object.hasOwn(DOC_RULES, value)
}

function isCertType(value: string): value is EnergyCertType {
  return Object.hasOwn(CERT_ORDER, value)
}

/**
 * The proof after one field changed. A select's '' clears its type; a document type that does not fit the transport
 * answer, or an unknown certificate type, leaves the proof unchanged (the same object), as the pages never offer one.
 */
export function setEvidence(
  evidence: EvidenceDraft,
  field: EvidenceField,
  value: string,
  answers: EvidenceAnswers,
): EvidenceDraft {
  switch (field) {
    case 'docType':
      if (value === '') return { ...evidence, docType: null }
      return isDocType(value) && docTypesFor(answers.transport).includes(value) ? { ...evidence, docType: value } : evidence
    case 'certType':
      if (value === '') return { ...evidence, certType: null }
      return isCertType(value) ? { ...evidence, certType: value } : evidence
    case 'docId':
    case 'dispatchDate':
    case 'certId':
      return { ...evidence, [field]: value }
  }
}

/**
 * The request's `evidence`: null when the supplier gave nothing the answers can carry. A document goes with the type the
 * page shows once its number is typed; one whose picked type no longer fits the transport answer is dropped, and so is
 * any shipping proof (the dispatch date too) while transport is skipped, and a certificate unless power is renewable.
 * Numbers go normalised and trimmed. The pages send it only when evidenceProblems found nothing.
 */
export function evidenceBody(evidence: EvidenceDraft, answers: EvidenceAnswers): AnswerEvidence | null {
  const docType = shownDocType(evidence.docType, answers.transport)
  const docId = evidence.docId.trim()
  const doc = docType !== null && docId !== '' ? { type: docType, id: normalizeDocId(docType, docId) } : null
  const date = answers.transport === null ? '' : evidence.dispatchDate.trim()
  const certType = shownCertType(evidence.certType, answers.energy)
  const certId = evidence.certId.trim()
  const cert = certType !== null && certId !== '' ? { type: certType, id: certId } : null
  if (doc === null && date === '' && cert === null) return null
  return {
    shippingDocType: doc?.type ?? null,
    shippingDocId: doc?.id ?? null,
    dispatchDate: date === '' ? null : date,
    energyCertType: cert?.type ?? null,
    energyCertId: cert?.id ?? null,
  }
}
