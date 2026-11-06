import { describe, expect, it } from 'vitest'
import type { ProblemCode, SupplierTerms, VerifyContext } from '@/api/client'
import { verifyCopy } from '@/copy/verify'
import {
  canSend,
  canSubmit,
  failureFor,
  formatIssueDate,
  initialVerifyState,
  offeredEnergy,
  offeredModes,
  reconfirmLines,
  resolveLang,
  submitBody,
  verifyReducer,
  type VerifyState,
} from './verify-state'

const termsText = {
  title: 'Supplier Terms',
  keyPoints: ['Your answers are your responsibility.'],
  acceptance: 'I accept the Supplier Terms.',
  sections: [{ number: 1, heading: 'Parties', paragraphs: ['The operator and the supplier.'] }],
}
const terms: SupplierTerms = { version: '2026-11-06', sha256: 'a'.repeat(64), ro: termsText, en: termsText }

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
  terms,
  ...overrides,
})

const ready = (ctx = context()): VerifyState => verifyReducer({ phase: 'loading' }, { kind: 'contextLoaded', context: ctx })

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
    expect(verifyReducer(accepted, { kind: 'submit' })).toMatchObject({ phase: 'submitting', accepted: true })

    const unticked = verifyReducer(accepted, { kind: 'terms', accepted: false })
    expect(verifyReducer(unticked, { kind: 'submit' })).toBe(unticked)
  })

  it('keeps the tick when an answer changes', () => {
    expect(verifyReducer(ticked(), { kind: 'transport', value: 'sea' })).toMatchObject({ accepted: true })
  })

  it('reads changed terms again before anything is sent', () => {
    const sending = verifyReducer(ticked(), { kind: 'submit' })
    const changed = verifyReducer(sending, { kind: 'failed', failure: 'termsChanged' })
    expect(changed).toEqual({ phase: 'failed', failure: 'termsChanged', retry: { phase: 'loading' } })

    const reloading = verifyReducer(changed, { kind: 'retry' })
    expect(reloading).toEqual({ phase: 'loading' })
    expect(verifyReducer(reloading, { kind: 'contextLoaded', context: context() })).toMatchObject({ accepted: false })
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
    expect(verifyReducer(state, { kind: 'contextLoaded', context: context() })).toBe(state)
    expect(verifyReducer(state, { kind: 'failed', failure: 'offline' })).toBe(state)
  })
})

describe('canSend', () => {
  it('needs an answer and the tick', () => {
    expect(canSend({ transport: 'sea', energy: null }, true)).toBe(true)
    expect(canSend({ transport: 'sea', energy: null }, false)).toBe(false)
    expect(canSend({ transport: null, energy: null }, true)).toBe(false)
  })
})

describe('submitBody', () => {
  it('sends a skipped answer as null, leaves the mass to the invoice and names the accepted terms', () => {
    expect(submitBody('v1.token', { transport: 'sea', energy: null }, terms.sha256)).toEqual({
      t: 'v1.token',
      transportMode: 'sea',
      energySource: null,
      acceptedTerms: terms.sha256,
    })
  })
})

describe('reconfirmLines', () => {
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
