import { describe, expect, it } from 'vitest'
import type { ClaimAssessment, TrustAssessment } from '@/api/client'
import { node } from '@/store/testFixtures'
import {
  ANSWERS_LIST_PATH,
  answerCheckLabel,
  answersBody,
  answersToCheck,
  atRiskLine,
  canDecide,
  claimValueLabel,
  refetchAfterTrust,
  trustFailureFor,
  trustFailureMessage,
  trustStamp,
} from './trust-display'

const claim = (overrides: Partial<ClaimAssessment> = {}): ClaimAssessment => ({
  kind: 'mode',
  value: 'sea',
  level: 'unproven',
  reduces: true,
  impactKg: 8571.7,
  review: null,
  reasons: [{ code: 'NO_EVIDENCE', detail: 'Nothing backs this answer yet.' }],
  ...overrides,
})

const trust = (overrides: Partial<TrustAssessment> = {}): TrustAssessment => ({
  score: 38,
  band: 'review',
  kgAtRisk: 8571.7,
  needsReview: true,
  claims: [claim()],
  signals: [],
  ...overrides,
})

describe('answers to check', () => {
  it('flags a node the API marks for review, and only that', () => {
    expect(answerCheckLabel(node('a', 1, { trust: trust() }))).toBe('Check answers')
    expect(answerCheckLabel(node('b', 1, { trust: trust({ needsReview: false }) }))).toBeNull()
    expect(answerCheckLabel(node('c', 1))).toBeNull()
  })

  it('lists them with the most kilograms at risk first, then the largest figure', () => {
    const nodes = [
      node('small', 100, { trust: trust({ kgAtRisk: 0 }) }),
      node('big', 900, { trust: trust({ kgAtRisk: 0 }) }),
      node('risky', 50, { trust: trust({ kgAtRisk: 3000 }) }),
      node('fine', 5000, { trust: trust({ needsReview: false }) }),
    ]
    expect(answersToCheck(nodes).map((n) => n.id)).toEqual(['risky', 'big', 'small'])
  })

  it('says how many tonnes are at stake, or why they are listed when nothing is', () => {
    expect(answersBody([node('a', 1, { trust: trust({ kgAtRisk: 3000 }) }), node('b', 1, { trust: trust({ kgAtRisk: 1100 }) })])).toContain(
      '4.10 tCO₂e',
    )
    expect(answersBody([node('a', 1, { trust: trust({ kgAtRisk: 0 }) })])).toContain('contradiction')
    expect(ANSWERS_LIST_PATH).toBe('/suppliers?filter=answers')
  })
})

describe('claims', () => {
  it('names the value as a reader says it', () => {
    expect(claimValueLabel(claim())).toBe('Sea')
    expect(claimValueLabel(claim({ kind: 'energy', value: 'renewable' }))).toBe('Renewable electricity')
    expect(claimValueLabel(claim({ kind: 'mass', value: '2.0' }))).toBe('2.0 t')
  })

  it('offers a decision for a reduction or a contradiction, never for an answer with nothing to back', () => {
    expect(canDecide(claim())).toBe(true)
    expect(canDecide(claim({ reduces: false, level: 'contradicted' }))).toBe(true)
    expect(canDecide(claim({ reduces: false, level: 'notNeeded' }))).toBe(false)
  })

  it('offers none on a partial answer, whose figure is the estimate whatever is decided', () => {
    expect(canDecide(claim(), 'PARTIAL')).toBe(false)
    expect(atRiskLine(trust({ kgAtRisk: 0 }), 'PARTIAL')).toBe('Partial answer: the spend estimate counts until both questions are answered.')
  })

  it('sums up the score and what is at risk', () => {
    expect(atRiskLine(trust())).toBe('8.57 tCO₂e of claimed reductions not backed yet')
    // Beside the tiles, the gap is the difference of the tiles as shown.
    expect(atRiskLine(trust({ kgAtRisk: 2140.29 }), 'VERIFIED', { countedKg: 5386.24, claimedKg: 3245.95 })).toBe(
      '2.14 tCO₂e of claimed reductions not backed yet',
    )
    expect(atRiskLine(trust({ kgAtRisk: 12 }), 'VERIFIED', { countedKg: 1006, claimedKg: 994 })).toBe(
      '0.02 tCO₂e of claimed reductions not backed yet',
    )
    expect(atRiskLine(trust({ kgAtRisk: 0 }))).toBe('Every claimed reduction is backed.')
  })

  it('moves the stamp when a level, the evidence or the target changes', () => {
    const before = node('a', 1, { trust: trust() })
    expect(trustStamp(before)).toBe(trustStamp(node('a', 2, { trust: trust({ score: 90 }) })))
    expect(trustStamp(before)).not.toBe(trustStamp(node('a', 1, { trust: trust({ claims: [claim({ level: 'reviewed', review: 'accepted' })] }) })))
    expect(trustStamp(before)).not.toBe(trustStamp({ ...before, evidence: { ...before.evidence, receivedOn: '2026-11-01' } }))
  })
})

describe('failures', () => {
  it.each([
    [409, 'ClaimChanged', 'changed'],
    [404, 'NodeNotFound', 'gone'],
    [422, 'InvalidRecord', 'invalid'],
    [422, 'ModeNotAvailable', 'invalid'],
  ] as const)('maps %i %s to %s', (status, code, kind) => {
    expect(trustFailureFor(status, code)).toEqual({ kind })
  })

  it('maps no answer to offline and anything else by its status', () => {
    expect(trustFailureFor(null, null)).toEqual({ kind: 'offline' })
    expect(trustFailureFor(500, null)).toEqual({ kind: 'other', status: 500 })
    expect(trustFailureMessage({ kind: 'other', status: 500 })).toBe('The API answered HTTP 500. Try again.')
  })

  it('refreshes the store after a stale page or a node that left the graph', () => {
    expect(refetchAfterTrust({ kind: 'changed' })).toBe(true)
    expect(refetchAfterTrust({ kind: 'gone' })).toBe(true)
    expect(refetchAfterTrust({ kind: 'invalid' })).toBe(false)
  })
})
