import { describe, expect, it } from 'vitest'
import type { ProblemCode, VerifyContext } from '@/api/client'
import { verifyCopy } from '@/copy/verify'
import { DEMO_CERT_IDS, DEMO_CONTAINER, demoDocId } from './demo-samples'
import { EMPTY_EVIDENCE } from './evidence'
import {
  canSend,
  canSubmit,
  demoEvidenceInputs,
  demoTags,
  failureFor,
  formatIssueDate,
  initialVerifyState,
  offeredEnergy,
  offeredModes,
  proofProblems,
  reconfirmLines,
  resolveLang,
  submitBody,
  verifyReducer,
  type VerifyInput,
  type VerifyState,
} from './verify-state'
import { verifyTerms } from './test-fixtures'

const TODAY = '2026-11-06'

const context = (overrides: Partial<VerifyContext> = {}): VerifyContext => ({
  nodeId: '11111111-1111-4111-8111-000000000001',
  supplierLabel: 'Shenzhen Diecast Co.',
  buyerName: 'Transilvania Electro-Assembly SRL',
  invoiceNumber: 'SDC-2026-1028',
  issueDate: '2026-10-28',
  itemDescription: 'Zinc die-cast sensor housings, type ZH-220',
  origin: 'Shenzhen, CN',
  dest: 'Sibiu, RO',
  modes: ['air', 'sea', 'rail'],
  defaultMode: 'air',
  energySources: ['unknown', 'grid', 'renewable'],
  defaultEnergy: 'grid',
  massT: 2.3,
  expiresAt: '2026-11-05T12:00:00Z',
  terms: verifyTerms,
  ...overrides,
})

const loaded = (ctx = context()): VerifyInput => ({ kind: 'contextLoaded', context: ctx, today: TODAY })

const ready = (ctx = context()): VerifyState => verifyReducer({ phase: 'loading' }, loaded(ctx))

/** Ready, with the supplier's tick on the terms. */
const ticked = (ctx = context()): VerifyState => verifyReducer(ready(ctx), { kind: 'terms', accepted: true })

describe('resolveLang', () => {
  it('shows Romanian unless the link asks for English', () => {
    expect(resolveLang(null)).toBe('ro')
    expect(resolveLang('')).toBe('ro')
    expect(resolveLang('ro')).toBe('ro')
    expect(resolveLang('de')).toBe('ro')
    expect(resolveLang('en')).toBe('en')
    expect(resolveLang(' EN ')).toBe('en')
  })
})

describe('failureFor', () => {
  it.each<[number | null, ProblemCode | null, string]>([
    [401, 'InvalidToken', 'invalid'],
    [409, 'AlreadyUsed', 'alreadyUsed'],
    [410, 'LinkExpired', 'expired'],
    [410, 'InvoiceChanged', 'invoiceChanged'],
    [429, 'RateLimited', 'rateLimited'],
    [422, 'InvalidAnswer', 'invalid'],
    [422, 'InvalidEvidence', 'proofRefused'],
    [422, 'TermsNotAccepted', 'termsChanged'],
    [404, 'NodeNotFound', 'invalid'],
  ])('maps %s %s to %s', (status, code, failure) => {
    expect(failureFor(status, code)).toBe(failure)
  })

  it('falls back to the status when the body carries no code', () => {
    expect(failureFor(401, null)).toBe('invalid')
    expect(failureFor(409, null)).toBe('alreadyUsed')
    expect(failureFor(410, null)).toBe('expired')
    expect(failureFor(429, null)).toBe('rateLimited')
  })

  it('treats no answer, a server error or a proxy error as no connection', () => {
    expect(failureFor(null, null)).toBe('offline')
    expect(failureFor(500, null)).toBe('offline')
    expect(failureFor(502, null)).toBe('offline')
  })
})

describe('offered answers', () => {
  it("offers the lane's modes alone, each once, in a fixed order", () => {
    expect(offeredModes(context({ modes: ['air', 'sea', 'rail', 'sea'] }))).toEqual(['rail', 'sea', 'air'])
    expect(offeredModes(context({ modes: ['road'], defaultMode: 'road' }))).toEqual(['road'])
  })

  it('offers the energy answers in a fixed order', () => {
    expect(offeredEnergy(context())).toEqual(['grid', 'renewable', 'unknown'])
  })

  it("pre-selects the lane's default mode and grid", () => {
    expect(ready()).toMatchObject({ phase: 'ready', answers: { transport: 'air', energy: 'grid' } })
  })

  it('pre-selects the first option when the default is not offered', () => {
    const state = ready(context({ modes: ['sea', 'rail'], defaultMode: 'air', energySources: ['renewable'] }))
    expect(state).toMatchObject({ answers: { transport: 'rail', energy: 'renewable' } })
  })
})

describe('verifyReducer', () => {
  it('starts on "link not valid" without a token, and loads otherwise', () => {
    expect(initialVerifyState(null)).toEqual({ phase: 'failed', failure: 'invalid', retry: null })
    expect(initialVerifyState('  ')).toEqual({ phase: 'failed', failure: 'invalid', retry: null })
    expect(initialVerifyState('v1.token')).toEqual({ phase: 'loading' })
  })

  it('changes and skips answers, and ignores a mode the lane does not have', () => {
    let state = ready()
    state = verifyReducer(state, { kind: 'transport', value: 'sea' })
    state = verifyReducer(state, { kind: 'energy', value: null })
    expect(state).toMatchObject({ answers: { transport: 'sea', energy: null } })
    expect(verifyReducer(state, { kind: 'transport', value: 'road' })).toBe(state)
  })

  it('submits one answer (PARTIAL) but not two skips', () => {
    const one = verifyReducer(ticked(), { kind: 'energy', value: null })
    expect(verifyReducer(one, { kind: 'submit' })).toMatchObject({ phase: 'submitting' })

    const none = verifyReducer(one, { kind: 'transport', value: null })
    expect(canSubmit({ transport: null, energy: null })).toBe(false)
    expect(verifyReducer(none, { kind: 'submit' })).toBe(none)
  })

  it('ignores taps, the checkbox and a second submit while sending', () => {
    const sending = verifyReducer(ticked(), { kind: 'submit' })
    expect(verifyReducer(sending, { kind: 'transport', value: 'sea' })).toBe(sending)
    expect(verifyReducer(sending, { kind: 'terms', accepted: false })).toBe(sending)
    expect(verifyReducer(sending, { kind: 'submit' })).toBe(sending)
  })

  it('starts with the terms unticked and sends nothing until the supplier ticks them', () => {
    const state = ready()
    expect(state).toMatchObject({ phase: 'ready', accepted: false })
    expect(verifyReducer(state, { kind: 'submit' })).toBe(state)

    const accepted = verifyReducer(state, { kind: 'terms', accepted: true })
    expect(verifyReducer(accepted, { kind: 'submit' })).toEqual({
      phase: 'submitting',
      context: context(),
      answers: { transport: 'air', energy: 'grid' },
      evidence: EMPTY_EVIDENCE,
      today: TODAY,
    })

    const unticked = verifyReducer(accepted, { kind: 'terms', accepted: false })
    expect(verifyReducer(unticked, { kind: 'submit' })).toBe(unticked)
  })

  it('keeps the tick when an answer changes', () => {
    expect(verifyReducer(ticked(), { kind: 'transport', value: 'sea' })).toMatchObject({ accepted: true })
  })

  it('reads changed terms again before anything is sent, keeping the answers given', () => {
    let state = verifyReducer(ticked(), { kind: 'transport', value: 'sea' })
    state = verifyReducer(state, { kind: 'energy', value: null })
    const sending = verifyReducer(state, { kind: 'submit' })
    const changed = verifyReducer(sending, { kind: 'failed', failure: 'termsChanged' })
    const previous = { transport: 'sea', energy: null }
    const kept = { phase: 'loading', previous, evidence: EMPTY_EVIDENCE }
    expect(changed).toEqual({ phase: 'failed', failure: 'termsChanged', retry: kept })

    const reloading = verifyReducer(changed, { kind: 'retry' })
    expect(reloading).toEqual(kept)
    // The supplier's answers, not the defaults (air, grid), and the box unticked for the new text.
    expect(verifyReducer(reloading, loaded())).toMatchObject({
      phase: 'ready',
      answers: previous,
      accepted: false,
    })
  })

  it('falls back to the pre-selection when the reloaded link no longer offers a kept answer', () => {
    const reloading: VerifyState = { phase: 'loading', previous: { transport: 'sea', energy: 'renewable' } }
    const state = verifyReducer(reloading, loaded(context({ modes: ['air'] })))
    expect(state).toMatchObject({ answers: { transport: 'air', energy: 'renewable' } })
  })

  it('keeps the answers when the reload itself is lost and retried', () => {
    const reloading: VerifyState = { phase: 'loading', previous: { transport: 'sea', energy: null } }
    const lost = verifyReducer(reloading, { kind: 'failed', failure: 'offline' })
    expect(verifyReducer(lost, { kind: 'retry' })).toEqual(reloading)
  })

  it('ends on the outcome with its warnings', () => {
    const sending = verifyReducer(ticked(), { kind: 'submit' })
    const warnings = [{ code: 'AIR_HEAVY' as const, detail: 'Air freight above 2 t' }]
    const done = verifyReducer(sending, {
      kind: 'submitted',
      outcome: { nodeId: context().nodeId, status: 'VERIFIED', warnings },
    })
    expect(done).toEqual({ phase: 'done', status: 'VERIFIED', warnings })
  })

  it('retries a lost context request by loading again', () => {
    const lost = verifyReducer({ phase: 'loading' }, { kind: 'failed', failure: 'offline' })
    expect(lost).toEqual({ phase: 'failed', failure: 'offline', retry: { phase: 'loading' } })
    expect(verifyReducer(lost, { kind: 'retry' })).toEqual({ phase: 'loading' })
  })

  it('retries a lost submission with the same answers', () => {
    const sending = verifyReducer(verifyReducer(ticked(), { kind: 'transport', value: 'sea' }), { kind: 'submit' })
    const lost = verifyReducer(sending, { kind: 'failed', failure: 'rateLimited' })
    expect(verifyReducer(lost, { kind: 'retry' })).toEqual(sending)
  })

  it('offers no retry for a used, expired, changed or invalid link', () => {
    for (const failure of ['alreadyUsed', 'expired', 'invoiceChanged', 'invalid'] as const) {
      const state = verifyReducer({ phase: 'loading' }, { kind: 'failed', failure })
      expect(state).toEqual({ phase: 'failed', failure, retry: null })
      expect(verifyReducer(state, { kind: 'retry' })).toBe(state)
    }
  })

  it('ignores a reply that arrives in the wrong state', () => {
    const state = ready()
    expect(verifyReducer(state, loaded())).toBe(state)
    expect(verifyReducer(state, { kind: 'failed', failure: 'offline' })).toBe(state)
  })
})

describe('proof (F7)', () => {
  const proof = (state: VerifyState, ...inputs: VerifyInput[]) => inputs.reduce(verifyReducer, state)
  const container = (docId: string): VerifyInput[] => [
    { kind: 'transport', value: 'sea' },
    { kind: 'evidence', field: 'docType', value: 'containerNumber' },
    { kind: 'evidence', field: 'docId', value: docId },
  ]

  it('starts empty and leaves the two-tap answer as it was', () => {
    const state = ticked()
    expect(state).toMatchObject({ phase: 'ready', evidence: EMPTY_EVIDENCE, today: TODAY })
    const sending = verifyReducer(state, { kind: 'submit' })
    expect(sending).toMatchObject({ phase: 'submitting', evidence: EMPTY_EVIDENCE })
    if (sending.phase !== 'submitting') throw new Error('expected submitting')
    expect(submitBody('v1.token', sending.answers, verifyTerms.sha256, sending.evidence)).not.toHaveProperty('evidence')
  })

  it('holds the answers back while a filled field has a problem, and sends once it is fixed', () => {
    const typo = proof(ticked(), ...container('CSQU3054384'))
    if (typo.phase !== 'ready') throw new Error('expected ready')
    expect(proofProblems(typo).docId).toBe('checkDigit')
    expect(verifyReducer(typo, { kind: 'submit' })).toBe(typo)

    const fixed = verifyReducer(typo, { kind: 'evidence', field: 'docId', value: 'CSQU3054383' })
    const sending = verifyReducer(fixed, { kind: 'submit' })
    if (sending.phase !== 'submitting') throw new Error('expected submitting')
    expect(submitBody('v1.token', sending.answers, verifyTerms.sha256, sending.evidence)).toEqual({
      t: 'v1.token',
      transportMode: 'sea',
      energySource: 'grid',
      acceptedTerms: verifyTerms.sha256,
      evidence: {
        shippingDocType: 'containerNumber',
        shippingDocId: 'CSQU3054383',
        dispatchDate: null,
        energyCertType: null,
        energyCertId: null,
      },
    })
  })

  it('stops counting a document once the transport answer no longer fits it', () => {
    const typo = proof(ticked(), ...container('nope'))
    const air = verifyReducer(typo, { kind: 'transport', value: 'air' })
    expect(verifyReducer(air, { kind: 'submit' })).toMatchObject({ phase: 'submitting' })
  })

  it('refuses a dispatch date after tomorrow', () => {
    const future = verifyReducer(ticked(), { kind: 'evidence', field: 'dispatchDate', value: '2026-11-08' })
    expect(verifyReducer(future, { kind: 'submit' })).toBe(future)
  })

  it('ignores a document type the answer does not fit, and any change while sending', () => {
    const state = ticked()
    expect(verifyReducer(state, { kind: 'evidence', field: 'docType', value: 'billOfLading' })).toBe(state)
    const sending = verifyReducer(state, { kind: 'submit' })
    expect(verifyReducer(sending, { kind: 'evidence', field: 'docId', value: 'X' })).toBe(sending)
  })

  it('goes back to the questions when the API refuses the proof, keeping everything, and sends again once it is fixed', () => {
    // The page's own check passes (a well-formed CMR number); the API refuses it all the same.
    const road = context({ modes: ['road'], defaultMode: 'road' })
    const given = proof(
      ticked(road),
      { kind: 'evidence', field: 'docType', value: 'cmr' },
      { kind: 'evidence', field: 'docId', value: 'RO-1234' },
      { kind: 'evidence', field: 'dispatchDate', value: '2026-10-20' },
    )
    const refused = proof(given, { kind: 'submit' }, { kind: 'failed', failure: 'proofRefused' })
    expect(refused).toEqual({
      phase: 'ready',
      context: road,
      answers: { transport: 'road', energy: 'grid' },
      evidence: { ...EMPTY_EVIDENCE, docType: 'cmr', docId: 'RO-1234', dispatchDate: '2026-10-20' },
      today: TODAY,
      accepted: true,
      proofRefused: true,
    })

    const fixed = verifyReducer(refused, { kind: 'evidence', field: 'docId', value: 'RO-5678' })
    expect(fixed).toMatchObject({ phase: 'ready', proofRefused: false, accepted: true })
    const sending = verifyReducer(fixed, { kind: 'submit' })
    if (sending.phase !== 'submitting') throw new Error('expected submitting')
    expect(submitBody('v1.token', sending.answers, verifyTerms.sha256, sending.evidence).evidence).toMatchObject({
      shippingDocType: 'cmr',
      shippingDocId: 'RO-5678',
    })
    expect(
      verifyReducer(sending, { kind: 'submitted', outcome: { nodeId: road.nodeId, status: 'VERIFIED', warnings: [] } }),
    ).toEqual({ phase: 'done', status: 'VERIFIED', warnings: [] })
  })

  it('clears the refusal line when the supplier clears the proof or changes an answer', () => {
    const refused = proof(ticked(), ...container('CSQU3054383'), { kind: 'submit' }, { kind: 'failed', failure: 'proofRefused' })
    expect(refused).toMatchObject({ phase: 'ready', proofRefused: true })
    const cleared = proof(refused, { kind: 'evidence', field: 'docId', value: '' })
    expect(cleared).toMatchObject({ proofRefused: false })
    const sending = verifyReducer(cleared, { kind: 'submit' })
    if (sending.phase !== 'submitting') throw new Error('expected submitting')
    expect(submitBody('v1.token', sending.answers, verifyTerms.sha256, sending.evidence)).not.toHaveProperty('evidence')
    expect(verifyReducer(refused, { kind: 'energy', value: null })).toMatchObject({ proofRefused: false })
  })

  it('ends the link on a proof refusal when no proof was sent, or while loading', () => {
    const sending = verifyReducer(ticked(), { kind: 'submit' })
    expect(verifyReducer(sending, { kind: 'failed', failure: 'proofRefused' })).toEqual({
      phase: 'failed',
      failure: 'invalid',
      retry: null,
    })
    expect(verifyReducer({ phase: 'loading' }, { kind: 'failed', failure: 'proofRefused' })).toEqual({
      phase: 'failed',
      failure: 'invalid',
      retry: null,
    })
  })

  it('types the number alone under the usual document, and drops it when the new mode cannot carry it', () => {
    const typed = proof(
      ticked(),
      { kind: 'transport', value: 'sea' },
      { kind: 'evidence', field: 'docId', value: 'CSQU3054383' },
    )
    expect(proofProblems(typed as Extract<VerifyState, { phase: 'ready' }>)).toEqual({
      docId: null,
      dispatchDate: null,
      certId: null,
    })
    // Rail carries containers: the number stays a container number.
    expect(verifyReducer(typed, { kind: 'transport', value: 'rail' })).toMatchObject({
      evidence: { docType: 'containerNumber', docId: 'CSQU3054383' },
    })
    // Air does not: the number goes, so it is never sent as an air waybill.
    expect(verifyReducer(typed, { kind: 'transport', value: 'air' })).toMatchObject({ evidence: { docType: null, docId: '' } })
  })

  it('keeps the proof when changed terms are read again', () => {
    const given = proof(ticked(), ...container('CSQU3054383'))
    const changed = proof(given, { kind: 'submit' }, { kind: 'failed', failure: 'termsChanged' }, { kind: 'retry' })
    expect(verifyReducer(changed, loaded())).toMatchObject({
      phase: 'ready',
      accepted: false,
      evidence: { docType: 'containerNumber', docId: 'CSQU3054383' },
    })
  })
})

describe('canSend', () => {
  it('needs an answer and the tick', () => {
    expect(canSend({ transport: 'sea', energy: null }, true)).toBe(true)
    expect(canSend({ transport: 'sea', energy: null }, false)).toBe(false)
    expect(canSend({ transport: null, energy: null }, true)).toBe(false)
  })

  it('waits while the proof has a problem', () => {
    const problems = { docId: 'shape', dispatchDate: null, certId: null } as const
    expect(canSend({ transport: 'sea', energy: null }, true, problems)).toBe(false)
  })
})

describe('submitBody', () => {
  it('sends a skipped answer as null, leaves the mass to the invoice and names the accepted terms', () => {
    expect(submitBody('v1.token', { transport: 'sea', energy: null }, verifyTerms.sha256)).toEqual({
      t: 'v1.token',
      transportMode: 'sea',
      energySource: null,
      acceptedTerms: verifyTerms.sha256,
    })
  })
})

describe('reconfirmLines', () => {
  it('has a line for every warning code in both languages', () => {
    for (const lang of ['ro', 'en'] as const) {
      const lines = reconfirmLines(
        [
          { code: 'AIR_HEAVY', detail: '' },
          { code: 'SEA_TOO_FAST', detail: '' },
          { code: 'MASS_BELOW_INVOICE', detail: '' },
        ],
        verifyCopy[lang].reconfirm.warnings,
      )
      expect(lines).toHaveLength(3)
      for (const line of lines) expect(line.length).toBeGreaterThan(0)
    }
  })

  it('gives one line per warning code, in the language shown', () => {
    const warnings = [
      { code: 'AIR_HEAVY' as const, detail: 'a' },
      { code: 'AIR_HEAVY' as const, detail: 'b' },
    ]
    expect(reconfirmLines(warnings, verifyCopy.ro.reconfirm.warnings)).toEqual([
      verifyCopy.ro.reconfirm.warnings.AIR_HEAVY,
    ])
    expect(reconfirmLines([], verifyCopy.en.reconfirm.warnings)).toEqual([])
  })
})

describe('formatIssueDate', () => {
  it('writes the date out in the language shown', () => {
    expect(formatIssueDate('2026-10-28', 'ro')).toBe('28 octombrie 2026')
    expect(formatIssueDate('2026-10-28', 'en')).toBe('28 October 2026')
  })

  it('shows anything unreadable as it came', () => {
    expect(formatIssueDate('not a date', 'en')).toBe('not a date')
  })
})

describe('demo values (demo tools)', () => {
  const proof = (state: VerifyState, ...inputs: VerifyInput[]) => inputs.reduce(verifyReducer, state)
  const answers = { transport: 'sea', energy: 'renewable' } as const

  it('types the shown document and certificate with their samples, and the invoice date as the dispatch date', () => {
    expect(demoEvidenceInputs(EMPTY_EVIDENCE, answers, '2026-10-28', TODAY)).toEqual([
      { field: 'docType', value: 'containerNumber' },
      { field: 'docId', value: DEMO_CONTAINER },
      { field: 'dispatchDate', value: '2026-10-28' },
      { field: 'certType', value: 'guaranteeOfOrigin' },
      { field: 'certId', value: DEMO_CERT_IDS.guaranteeOfOrigin },
    ])
  })

  it('follows the type the presenter picked, keeps the date by today, and fills nothing the page hides', () => {
    const picked = { ...EMPTY_EVIDENCE, docType: 'billOfLading' as const }
    expect(demoEvidenceInputs(picked, { transport: 'sea', energy: 'grid' }, '2026-11-20', TODAY)).toEqual([
      { field: 'docType', value: 'billOfLading' },
      { field: 'docId', value: demoDocId('billOfLading') },
      { field: 'dispatchDate', value: TODAY },
    ])
    expect(demoEvidenceInputs(EMPTY_EVIDENCE, { transport: null, energy: 'grid' }, '2026-10-28', TODAY)).toEqual([])
  })

  it('goes through the usual inputs and checks, and is tagged until someone types over it', () => {
    let state = proof(ticked(), { kind: 'transport', value: 'sea' }, { kind: 'energy', value: 'renewable' })
    if (state.phase !== 'ready') throw new Error('expected ready')
    const inputs = demoEvidenceInputs(state.evidence, state.answers, state.context.issueDate, state.today)
    state = proof(state, ...inputs.map((input): VerifyInput => ({ kind: 'evidence', ...input })))
    if (state.phase !== 'ready') throw new Error('expected ready')
    expect(proofProblems(state)).toEqual({ docId: null, dispatchDate: null, certId: null })
    expect(demoTags(state.evidence, state.answers)).toEqual({ docId: true, certId: true })
    const sending = verifyReducer(state, { kind: 'submit' })
    if (sending.phase !== 'submitting') throw new Error('expected submitting')
    expect(submitBody('v1.token', sending.answers, verifyTerms.sha256, sending.evidence).evidence).toEqual({
      shippingDocType: 'containerNumber',
      shippingDocId: DEMO_CONTAINER,
      dispatchDate: '2026-10-28',
      energyCertType: 'guaranteeOfOrigin',
      energyCertId: DEMO_CERT_IDS.guaranteeOfOrigin,
    })
    const typedOver = verifyReducer(state, { kind: 'evidence', field: 'docId', value: 'CSQU3054383' })
    if (typedOver.phase !== 'ready') throw new Error('expected ready')
    expect(demoTags(typedOver.evidence, typedOver.answers)).toEqual({ docId: false, certId: true })
  })
})
