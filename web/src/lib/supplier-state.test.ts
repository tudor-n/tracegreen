import { describe, expect, it } from 'vitest'
import type { PortalItem, PortalView } from '@/api/client'
import {
  answerBody,
  answerSummary,
  confirmedCount,
  draftFor,
  initialSupplierState,
  type SupplierInput,
  type SupplierState,
  supplierFailureFor,
  supplierReducer,
} from './supplier-state'

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

const N3 = '11111111-1111-4111-8111-000000000003'
const SECOND = '22222222-2222-4222-8222-0000000000aa'

const view = (items: PortalItem[] = [item(), item({ nodeId: SECOND, invoiceNumber: 'OH-2026-ADV1' })]): PortalView => ({
  supplierLabel: 'Oradea Harness SRL',
  buyerName: 'Transilvania Electro-Assembly SRL',
  expiresAt: '2026-11-13T12:00:00Z',
  items,
})

const run = (state: SupplierState, ...inputs: SupplierInput[]) => inputs.reduce(supplierReducer, state)

const ready = (v: PortalView = view()) => run({ phase: 'loading' }, { kind: 'loaded', view: v })

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
    [404, 'NodeNotFound', 'changed'],
    [422, 'InvalidAnswer', 'changed'],
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
    expect(state.draft).toEqual({ nodeId: N3, answers: { transport: 'road', energy: 'grid' }, sending: false, failure: null })
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
    const sending = run(open(), { kind: 'send' })
    expect(readyState(sending).draft?.sending).toBe(true)
    for (const input of [
      { kind: 'transport', value: null },
      { kind: 'close' },
      { kind: 'open', nodeId: SECOND },
      { kind: 'send' },
      { kind: 'reload' },
    ] as SupplierInput[]) {
      expect(supplierReducer(sending, input)).toBe(sending)
    }
  })

  it('builds the request body with the node and both answers', () => {
    const { draft } = readyState(run(open(), { kind: 'energy', value: null }))
    expect(answerBody('p1.abc', draft!)).toEqual({ t: 'p1.abc', nodeId: N3, transportMode: 'road', energySource: null })
  })
})

describe('after a send', () => {
  const sending = () => run(ready(), { kind: 'open', nodeId: N3 }, { kind: 'energy', value: 'renewable' }, { kind: 'send' })

  it("shows the API's status and the answers on the card, keeps the warnings, and asks for a fresh list", () => {
    const warnings = [{ code: 'AIR_HEAVY' as const, detail: 'Air above 2 t.' }]
    const state = readyState(
      supplierReducer(sending(), { kind: 'answered', nodeId: N3, outcome: { nodeId: N3, status: 'VERIFIED', warnings } }),
    )
    expect(state.draft).toBeNull()
    expect(state.stale).toBe(true)
    expect(state.warnings[N3]).toEqual(warnings)
    expect(state.view.items[0]).toMatchObject({ status: 'VERIFIED', answeredMode: 'road', answeredEnergy: 'renewable' })
    expect(confirmedCount(state.view)).toEqual({ confirmed: 1, total: 2 })
  })

  it("replaces the list with the fresh one, keeping a delivery opened meanwhile", () => {
    const answered = supplierReducer(sending(), {
      kind: 'answered',
      nodeId: N3,
      outcome: { nodeId: N3, status: 'VERIFIED', warnings: [] },
    })
    const reopened = supplierReducer(answered, { kind: 'open', nodeId: SECOND })
    const fresh = view([item({ nodeId: SECOND, defaultEnergy: 'renewable' }), item({ status: 'VERIFIED' })])
    const state = readyState(supplierReducer(reopened, { kind: 'loaded', view: fresh }))
    expect(state.view).toBe(fresh)
    expect(state.stale).toBe(false)
    expect(state.draft?.nodeId).toBe(SECOND)
  })

  it('keeps the list on screen when the refresh fails', () => {
    const answered = supplierReducer(sending(), {
      kind: 'answered',
      nodeId: N3,
      outcome: { nodeId: N3, status: 'PARTIAL', warnings: [] },
    })
    const state = readyState(supplierReducer(answered, { kind: 'loadFailed', failure: 'offline' }))
    expect(state.stale).toBe(false)
    expect(state.view.items[0].status).toBe('PARTIAL')
  })

  it('ignores a list nobody asked for', () => {
    const state = ready()
    expect(supplierReducer(state, { kind: 'loaded', view: view([]) })).toBe(state)
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

  it('reload closes the delivery and fetches the list again', () => {
    const failed = supplierReducer(sending(), { kind: 'answerFailed', nodeId: N3, failure: 'changed' })
    const state = readyState(supplierReducer(failed, { kind: 'reload' }))
    expect(state.draft).toBeNull()
    expect(state.stale).toBe(true)
  })

  it("ignores another node's late reply", () => {
    const state = sending()
    expect(
      supplierReducer(state, { kind: 'answered', nodeId: SECOND, outcome: { nodeId: SECOND, status: 'VERIFIED', warnings: [] } }),
    ).toBe(state)
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
