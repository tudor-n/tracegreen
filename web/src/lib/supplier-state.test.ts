import { describe, expect, it } from 'vitest'
import type { PortalItem, PortalView } from '@/api/client'
import { DEMO_CERT_IDS, demoDocId } from './demo-samples'
import { EMPTY_EVIDENCE } from './evidence'
import {
  answerBody,
  answerSummary,
  confirmedCount,
  draftFor,
  draftProblems,
  initialSupplierState,
  type SupplierInput,
  type SupplierState,
  supplierFailureFor,
  supplierReducer,
  termsAccepted,
} from './supplier-state'
import { verifyTerms } from './test-fixtures'
import { demoEvidenceInputs } from './verify-state'

const item = (overrides: Partial<PortalItem> = {}): PortalItem => ({
  nodeId: '11111111-1111-4111-8111-000000000003',
  status: 'ESTIMATED',
  invoiceNumber: 'OH-2026-1005',
  issueDate: '2026-10-05',
  itemDescription: 'Wire harness set',
  origin: 'Oradea, RO',
  dest: 'Sibiu, RO',
  modes: ['road'],
  defaultMode: 'road',
  energySources: ['grid', 'renewable', 'unknown'],
  defaultEnergy: 'grid',
  answeredMode: null,
  answeredEnergy: null,
  ...overrides,
})

const TODAY = '2026-11-06'
const N3 = '11111111-1111-4111-8111-000000000003'
const SECOND = '22222222-2222-4222-8222-0000000000aa'

const view = (items: PortalItem[] = [item(), item({ nodeId: SECOND, invoiceNumber: 'OH-2026-ADV1' })]): PortalView => ({
  supplierLabel: 'Oradea Harness SRL',
  buyerName: 'Transilvania Electro-Assembly SRL',
  expiresAt: '2026-11-13T12:00:00Z',
  items,
  terms: verifyTerms,
})

const run = (state: SupplierState, ...inputs: SupplierInput[]) => inputs.reduce(supplierReducer, state)

const ready = (v: PortalView = view()) => run({ phase: 'loading' }, { kind: 'loaded', today: TODAY, view: v })

const tick: SupplierInput = { kind: 'terms', accepted: true }

function readyState(state: SupplierState) {
  if (state.phase !== 'ready') throw new Error(`expected ready, got ${state.phase}`)
  return state
}

describe('supplierFailureFor', () => {
  it.each([
    [401, 'InvalidToken', 'invalid'],
    [410, 'LinkExpired', 'expired'],
    [410, 'InvoiceChanged', 'gone'],
    [409, 'AlreadyConfirmed', 'changed'],
    [409, 'DeliveryChanged', 'changed'],
    [404, 'NodeNotFound', 'changed'],
    [422, 'InvalidAnswer', 'changed'],
    [422, 'InvalidEvidence', 'proofRefused'],
    [422, 'TermsNotAccepted', 'termsChanged'],
    [429, 'RateLimited', 'rateLimited'],
  ] as const)('maps %i %s to %s', (status, code, failure) => {
    expect(supplierFailureFor(status, code)).toBe(failure)
  })

  it.each([
    [401, 'invalid'],
    [404, 'changed'],
    [409, 'changed'],
    [410, 'expired'],
    [429, 'rateLimited'],
    [502, 'offline'],
    [null, 'offline'],
  ] as const)('falls back on the status %s → %s', (status, failure) => {
    expect(supplierFailureFor(status, null)).toBe(failure)
  })
})

describe('initialSupplierState', () => {
  it('loads when there is a token', () => {
    expect(initialSupplierState('p1.abc')).toEqual({ phase: 'loading' })
  })

  it.each([null, '', '  '])('starts on "not valid" without a request for %j', (token) => {
    expect(initialSupplierState(token)).toEqual({ phase: 'failed', failure: 'invalid', retryable: false })
  })
})

describe('loading', () => {
  it('shows the list with nothing open', () => {
    const state = readyState(ready())
    expect(state.draft).toBeNull()
    expect(state.stale).toBe(false)
    expect(state.view.items).toHaveLength(2)
  })

  it.each([
    ['invalid', false],
    ['expired', false],
    ['gone', false],
    ['rateLimited', true],
    ['offline', true],
  ] as const)('a %s list is a page failure (retry: %s)', (failure, retryable) => {
    expect(run({ phase: 'loading' }, { kind: 'loadFailed', failure })).toEqual({ phase: 'failed', failure, retryable })
  })

  it('reads a list that is "changed" (a 404) as a link that no longer fits', () => {
    expect(run({ phase: 'loading' }, { kind: 'loadFailed', failure: 'changed' })).toEqual({
      phase: 'failed',
      failure: 'invalid',
      retryable: false,
    })
  })

  it('retries a retryable failure and ignores a final one', () => {
    const offline = run({ phase: 'loading' }, { kind: 'loadFailed', failure: 'offline' })
    expect(supplierReducer(offline, { kind: 'reload' })).toEqual({ phase: 'loading' })
    const expired = run({ phase: 'loading' }, { kind: 'loadFailed', failure: 'expired' })
    expect(supplierReducer(expired, { kind: 'reload' })).toBe(expired)
  })
})

describe('draftFor', () => {
  it("pre-selects the API's defaults", () => {
    expect(draftFor(item({ defaultEnergy: 'renewable' })).answers).toEqual({ transport: 'road', energy: 'renewable' })
  })

  it('falls back to the first offered option when the default is not offered', () => {
    expect(draftFor(item({ modes: ['sea', 'rail'], defaultMode: 'air' })).answers.transport).toBe('rail')
  })
})

describe('one delivery at a time', () => {
  it('opens a delivery with its pre-selected answers', () => {
    const state = readyState(run(ready(), { kind: 'open', nodeId: N3 }))
    expect(state.draft).toEqual({
      nodeId: N3,
      answers: { transport: 'road', energy: 'grid' },
      evidence: EMPTY_EVIDENCE,
      sending: false,
      failure: null,
      proofRefused: false,
    })
  })

  it('opening another one replaces the first', () => {
    const state = readyState(run(ready(), { kind: 'open', nodeId: N3 }, { kind: 'open', nodeId: SECOND }))
    expect(state.draft?.nodeId).toBe(SECOND)
  })

  it('does not open a confirmed delivery or one not on the list', () => {
    const confirmed = ready(view([item({ status: 'VERIFIED' })]))
    expect(supplierReducer(confirmed, { kind: 'open', nodeId: N3 })).toBe(confirmed)
    const state = ready()
    expect(supplierReducer(state, { kind: 'open', nodeId: 'nope' })).toBe(state)
  })

  it('closes without sending', () => {
    expect(readyState(run(ready(), { kind: 'open', nodeId: N3 }, { kind: 'close' })).draft).toBeNull()
  })
})

describe('answers', () => {
  const open = () => run(ready(), { kind: 'open', nodeId: N3 })

  it('takes an offered answer or a skip, and refuses a mode the lane lacks', () => {
    const changed = readyState(run(open(), { kind: 'energy', value: 'renewable' }, { kind: 'transport', value: null }))
    expect(changed.draft?.answers).toEqual({ transport: null, energy: 'renewable' })
    const state = open()
    expect(supplierReducer(state, { kind: 'transport', value: 'air' })).toBe(state)
  })

  it('does not send two skips', () => {
    const state = run(open(), { kind: 'transport', value: null }, { kind: 'energy', value: null })
    expect(supplierReducer(state, { kind: 'send' })).toBe(state)
  })

  it('marks the draft as sending and ignores taps until the answer comes back', () => {
    const sending = run(open(), tick, { kind: 'send' })
    expect(readyState(sending).draft?.sending).toBe(true)
    for (const input of [
      { kind: 'transport', value: null },
      { kind: 'close' },
      { kind: 'open', nodeId: SECOND },
      { kind: 'send' },
      { kind: 'reload' },
      { kind: 'terms', accepted: false },
    ] as SupplierInput[]) {
      expect(supplierReducer(sending, input)).toBe(sending)
    }
  })

  it('builds the request body with the node, both answers and the accepted terms', () => {
    const { draft } = readyState(run(open(), { kind: 'energy', value: null }))
    expect(answerBody('p1.abc', draft!, verifyTerms.sha256)).toEqual({
      t: 'p1.abc',
      nodeId: N3,
      transportMode: 'road',
      energySource: null,
      acceptedTerms: verifyTerms.sha256,
    })
  })
})

describe('supplier terms', () => {
  const open = () => run(ready(), { kind: 'open', nodeId: N3 })
  const changedTerms = (v: PortalView = view()): PortalView => ({
    ...v,
    terms: { ...verifyTerms, version: '2026-12-01', sha256: 'b'.repeat(64) },
  })

  it('start unticked, and nothing is sent until the supplier ticks them', () => {
    const state = open()
    expect(readyState(state).accepted).toBeNull()
    expect(termsAccepted(readyState(state))).toBe(false)
    expect(supplierReducer(state, { kind: 'send' })).toBe(state)
    const ticked = readyState(supplierReducer(state, tick))
    expect(ticked.accepted).toBe(verifyTerms.sha256)
    expect(readyState(supplierReducer(ticked, { kind: 'send' })).draft?.sending).toBe(true)
  })

  it('untick when the supplier unticks them', () => {
    const state = readyState(run(open(), tick, { kind: 'terms', accepted: false }))
    expect(state.accepted).toBeNull()
    expect(supplierReducer(state, { kind: 'send' })).toBe(state)
  })

  it('are ticked once per page: the next delivery sends without another tick', () => {
    const answered = run(open(), tick, { kind: 'send' }, {
      kind: 'answered',
      nodeId: N3,
      result: { outcome: { nodeId: N3, status: 'VERIFIED', warnings: [] }, view: view() },
    })
    const sending = readyState(run(answered, { kind: 'open', nodeId: SECOND }, { kind: 'send' }))
    expect(sending.draft).toMatchObject({ nodeId: SECOND, sending: true })
  })

  it('read as unticked when a list arrives with other terms', () => {
    const answered = readyState(
      run(open(), tick, { kind: 'send' }, {
        kind: 'answered',
        nodeId: N3,
        result: { outcome: { nodeId: N3, status: 'VERIFIED', warnings: [] }, view: changedTerms() },
      }),
    )
    expect(answered.accepted).toBeNull()
    const reloaded = readyState(run(ready(), tick, { kind: 'reload' }, { kind: 'loaded', today: TODAY, view: changedTerms() }))
    expect(reloaded.accepted).toBeNull()
  })

  it('changed on the server: the list is read again, the box unticked, the answers kept on the delivery', () => {
    const answers = { transport: 'road', energy: 'renewable' } as const
    const failed = readyState(
      run(open(), { kind: 'energy', value: 'renewable' }, tick, { kind: 'send' }, {
        kind: 'answerFailed',
        nodeId: N3,
        failure: 'termsChanged',
      }),
    )
    expect(failed).toMatchObject({ stale: true, accepted: null })
    expect(failed.draft).toMatchObject({ nodeId: N3, answers, sending: false, failure: 'termsChanged' })
    const reloaded = readyState(supplierReducer(failed, { kind: 'loaded', today: TODAY, view: changedTerms() }))
    expect(reloaded.draft).toMatchObject({ nodeId: N3, answers })
    expect(supplierReducer(reloaded, { kind: 'send' })).toBe(reloaded)
    const resent = readyState(run(reloaded, tick, { kind: 'send' }))
    expect(resent.accepted).toBe('b'.repeat(64))
    expect(resent.draft).toMatchObject({ sending: true, failure: null })
  })
})

describe('after a send', () => {
  const sending = () =>
    run(ready(), { kind: 'open', nodeId: N3 }, { kind: 'energy', value: 'renewable' }, tick, { kind: 'send' })
  const confirmedList = view([
    item({ nodeId: SECOND, invoiceNumber: 'OH-2026-ADV1', defaultEnergy: 'renewable' }),
    item({ status: 'VERIFIED', answeredMode: 'road', answeredEnergy: 'renewable' }),
  ])

  it("shows the API's list as it now is and keeps the answer's warnings", () => {
    const warnings = [{ code: 'AIR_HEAVY' as const, detail: 'Air above 2 t.' }]
    const state = readyState(
      supplierReducer(sending(), {
        kind: 'answered',
        nodeId: N3,
        result: { outcome: { nodeId: N3, status: 'VERIFIED', warnings }, view: confirmedList },
      }),
    )
    expect(state.draft).toBeNull()
    expect(state.stale).toBe(false)
    expect(state.view).toBe(confirmedList)
    expect(state.warnings[N3]).toEqual(warnings)
    expect(confirmedCount(state.view)).toEqual({ confirmed: 1, total: 2 })
  })

  it('opens the next delivery with what the API now pre-selects', () => {
    const answered = supplierReducer(sending(), {
      kind: 'answered',
      nodeId: N3,
      result: { outcome: { nodeId: N3, status: 'VERIFIED', warnings: [] }, view: confirmedList },
    })
    const state = readyState(supplierReducer(answered, { kind: 'open', nodeId: SECOND }))
    expect(state.draft?.answers).toEqual({ transport: 'road', energy: 'renewable' })
  })

  it('ignores a list nobody asked for', () => {
    const state = ready()
    expect(supplierReducer(state, { kind: 'loaded', today: TODAY, view: view([]) })).toBe(state)
  })

  it.each(['changed', 'rateLimited', 'offline'] as const)('shows %s on the delivery and lets it send again', (failure) => {
    const failed = readyState(supplierReducer(sending(), { kind: 'answerFailed', nodeId: N3, failure }))
    expect(failed.draft).toMatchObject({ sending: false, failure })
    expect(readyState(supplierReducer(failed, { kind: 'send' })).draft).toMatchObject({ sending: true, failure: null })
  })

  it.each(['invalid', 'expired', 'gone'] as const)('a %s link ends the page', (failure) => {
    expect(supplierReducer(sending(), { kind: 'answerFailed', nodeId: N3, failure })).toEqual({
      phase: 'failed',
      failure,
      retryable: false,
    })
  })

  it('reload closes the delivery, fetches the list again and takes the fresh one', () => {
    const failed = supplierReducer(sending(), { kind: 'answerFailed', nodeId: N3, failure: 'changed' })
    const reloading = readyState(supplierReducer(failed, { kind: 'reload' }))
    expect(reloading.draft).toBeNull()
    expect(reloading.stale).toBe(true)
    const state = readyState(supplierReducer(reloading, { kind: 'loaded', today: TODAY, view: confirmedList }))
    expect(state.view).toBe(confirmedList)
    expect(state.stale).toBe(false)
  })

  it('keeps the list on screen when a reload fails', () => {
    const reloading = supplierReducer(ready(), { kind: 'reload' })
    const state = readyState(supplierReducer(reloading, { kind: 'loadFailed', failure: 'offline' }))
    expect(state.stale).toBe(false)
    expect(state.view.items).toHaveLength(2)
  })

  it('a fresh list keeps a delivery opened meanwhile, unless it is now confirmed', () => {
    const reloading = run(ready(), { kind: 'reload' }, { kind: 'open', nodeId: SECOND })
    expect(readyState(supplierReducer(reloading, { kind: 'loaded', today: TODAY, view: confirmedList })).draft?.nodeId).toBe(SECOND)
    const openN3 = run(ready(), { kind: 'reload' }, { kind: 'open', nodeId: N3 })
    expect(readyState(supplierReducer(openN3, { kind: 'loaded', today: TODAY, view: confirmedList })).draft).toBeNull()
  })

  it("ignores another node's late reply", () => {
    const state = sending()
    expect(
      supplierReducer(state, {
        kind: 'answered',
        nodeId: SECOND,
        result: { outcome: { nodeId: SECOND, status: 'VERIFIED', warnings: [] }, view: confirmedList },
      }),
    ).toBe(state)
  })
})

describe('proof (F7)', () => {
  const open = () => run(ready(), { kind: 'open', nodeId: N3 })
  const cmr = (docId: string): SupplierInput[] => [
    { kind: 'evidence', field: 'docType', value: 'cmr' },
    { kind: 'evidence', field: 'docId', value: docId },
  ]

  it('opens each delivery with no proof, and sends the answers as before when none is added', () => {
    const sending = readyState(run(open(), tick, { kind: 'send' }))
    expect(sending.draft).toMatchObject({ evidence: EMPTY_EVIDENCE, sending: true })
    expect(answerBody('p1.abc', sending.draft!, verifyTerms.sha256)).not.toHaveProperty('evidence')
  })

  it("holds the delivery's answers back while a filled field has a problem", () => {
    const short = readyState(run(open(), tick, ...cmr('AB')))
    expect(draftProblems(short.draft!, item(), TODAY).docId).toBe('shape')
    expect(supplierReducer(short, { kind: 'send' })).toBe(short)
    const expired = readyState(run(open(), tick, { kind: 'evidence', field: 'dispatchDate', value: '2024-01-01' }))
    expect(supplierReducer(expired, { kind: 'send' })).toBe(expired)
  })

  it("sends the proof with that delivery's answers", () => {
    const sending = readyState(
      run(
        open(),
        tick,
        ...cmr('ro 1234/26'),
        { kind: 'evidence', field: 'dispatchDate', value: '2026-10-03' },
        { kind: 'energy', value: 'renewable' },
        { kind: 'evidence', field: 'certType', value: 'guaranteeOfOrigin' },
        { kind: 'evidence', field: 'certId', value: 'GO-RO-2026-0042' },
        { kind: 'send' },
      ),
    )
    expect(sending.draft?.sending).toBe(true)
    expect(answerBody('p1.abc', sending.draft!, verifyTerms.sha256)).toEqual({
      t: 'p1.abc',
      nodeId: N3,
      transportMode: 'road',
      energySource: 'renewable',
      acceptedTerms: verifyTerms.sha256,
      evidence: {
        shippingDocType: 'cmr',
        shippingDocId: 'RO1234/26',
        dispatchDate: '2026-10-03',
        energyCertType: 'guaranteeOfOrigin',
        energyCertId: 'GO-RO-2026-0042',
      },
    })
  })

  it('ignores a document the lane cannot carry, and any change while sending', () => {
    const state = open()
    expect(supplierReducer(state, { kind: 'evidence', field: 'docType', value: 'airWaybill' })).toBe(state)
    const sending = run(state, tick, { kind: 'send' })
    expect(supplierReducer(sending, { kind: 'evidence', field: 'docId', value: 'X' })).toBe(sending)
    expect(supplierReducer(ready(), { kind: 'evidence', field: 'docId', value: 'X' })).toEqual(ready())
  })

  it("starts the next delivery without the last one's proof", () => {
    const given = run(open(), tick, ...cmr('RO12345'), { kind: 'send' }, {
      kind: 'answered',
      nodeId: N3,
      result: { outcome: { nodeId: N3, status: 'VERIFIED', warnings: [] }, view: view() },
    })
    expect(readyState(run(given, { kind: 'open', nodeId: SECOND })).draft?.evidence).toEqual(EMPTY_EVIDENCE)
  })

  it('keeps the delivery open with everything when the API refuses the proof, and sends again once it is fixed', () => {
    const sent = run(open(), tick, ...cmr('RO-1234'), { kind: 'send' })
    const refused = readyState(supplierReducer(sent, { kind: 'answerFailed', nodeId: N3, failure: 'proofRefused' }))
    // Not a changed delivery: no reload, the tick kept, the answers and the proof on the card.
    expect(refused).toMatchObject({ stale: false, accepted: verifyTerms.sha256 })
    expect(refused.draft).toEqual({
      nodeId: N3,
      answers: { transport: 'road', energy: 'grid' },
      evidence: { ...EMPTY_EVIDENCE, docType: 'cmr', docId: 'RO-1234' },
      sending: false,
      failure: null,
      proofRefused: true,
    })

    const fixed = readyState(supplierReducer(refused, { kind: 'evidence', field: 'docId', value: 'RO-5678' }))
    expect(fixed.draft).toMatchObject({ proofRefused: false })
    const resent = readyState(supplierReducer(fixed, { kind: 'send' }))
    expect(resent.draft).toMatchObject({ sending: true, proofRefused: false })
    expect(answerBody('p1.abc', resent.draft!, verifyTerms.sha256).evidence).toMatchObject({ shippingDocId: 'RO-5678' })
    const answered = readyState(
      supplierReducer(resent, {
        kind: 'answered',
        nodeId: N3,
        result: { outcome: { nodeId: N3, status: 'VERIFIED', warnings: [] }, view: view() },
      }),
    )
    expect(answered.draft).toBeNull()
  })

  it('clears the refusal line on a change or a fresh send, and reads a refusal without proof as a changed delivery', () => {
    const refused = run(open(), tick, ...cmr('RO-1234'), { kind: 'send' }, {
      kind: 'answerFailed',
      nodeId: N3,
      failure: 'proofRefused',
    })
    expect(readyState(supplierReducer(refused, { kind: 'energy', value: null })).draft?.proofRefused).toBe(false)
    expect(readyState(supplierReducer(refused, { kind: 'send' })).draft).toMatchObject({ sending: true, proofRefused: false })

    const bare = run(open(), tick, { kind: 'send' }, { kind: 'answerFailed', nodeId: N3, failure: 'proofRefused' })
    expect(readyState(bare).draft).toMatchObject({ sending: false, failure: 'changed', proofRefused: false })
  })

  it("fills a delivery's demo values through the usual inputs, sent with that delivery's answers", () => {
    const opened = readyState(run(open(), tick, { kind: 'energy', value: 'renewable' }))
    const { draft } = opened
    const inputs = demoEvidenceInputs(draft!.evidence, draft!.answers, item().issueDate, TODAY)
    const filled = readyState(run(opened, ...inputs.map((input): SupplierInput => ({ kind: 'evidence', ...input }))))
    expect(draftProblems(filled.draft!, item(), TODAY)).toEqual({ docId: null, dispatchDate: null, certId: null })
    const sending = readyState(supplierReducer(filled, { kind: 'send' }))
    expect(answerBody('p1.abc', sending.draft!, verifyTerms.sha256).evidence).toEqual({
      shippingDocType: 'cmr',
      shippingDocId: demoDocId('cmr'),
      dispatchDate: '2026-10-05',
      energyCertType: 'guaranteeOfOrigin',
      energyCertId: DEMO_CERT_IDS.guaranteeOfOrigin,
    })
  })

  it('keeps a typed container number when the answer moves to another mode that carries containers', () => {
    const twoModes = view([item({ modes: ['road', 'rail'] })])
    const typed = run(
      ready(twoModes),
      { kind: 'open', nodeId: N3 },
      { kind: 'evidence', field: 'docType', value: 'containerNumber' },
      { kind: 'evidence', field: 'docId', value: 'CSQU3054383' },
    )
    expect(readyState(supplierReducer(typed, { kind: 'transport', value: 'rail' })).draft?.evidence).toMatchObject({
      docType: 'containerNumber',
      docId: 'CSQU3054383',
    })
    const cmrTyped = run(ready(twoModes), { kind: 'open', nodeId: N3 }, { kind: 'evidence', field: 'docId', value: 'RO-1234' })
    expect(readyState(supplierReducer(cmrTyped, { kind: 'transport', value: 'rail' })).draft?.evidence).toMatchObject({
      docType: null,
      docId: '',
    })
  })

  it('reads a proof refusal while loading the list as a link that no longer fits', () => {
    expect(run({ phase: 'loading' }, { kind: 'loadFailed', failure: 'proofRefused' })).toEqual({
      phase: 'failed',
      failure: 'invalid',
      retryable: false,
    })
  })

  it('keeps the proof on the delivery when the terms changed on the server', () => {
    const failed = readyState(
      run(open(), tick, ...cmr('RO12345'), { kind: 'send' }, { kind: 'answerFailed', nodeId: N3, failure: 'termsChanged' }),
    )
    expect(failed.draft?.evidence).toMatchObject({ docType: 'cmr', docId: 'RO12345' })
  })
})

describe('answerSummary', () => {
  const labels = {
    transport: { road: 'Road', rail: 'Rail', sea: 'Sea', air: 'Air' },
    energy: { grid: 'Grid', renewable: 'Mostly renewable', unknown: "Don't know" },
  }

  it('names both answers, or the one given', () => {
    expect(answerSummary(item({ answeredMode: 'road', answeredEnergy: 'renewable' }), labels)).toBe('Road · Mostly renewable')
    expect(answerSummary(item({ answeredMode: 'road' }), labels)).toBe('Road')
    expect(answerSummary(item(), labels)).toBe('')
  })
})
