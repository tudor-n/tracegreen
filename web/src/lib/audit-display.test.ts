import { describe, expect, it } from 'vitest'
import { audit } from '@/store/testFixtures'
import { auditActionLabel, auditActorLabel, chainResultLabel, newestFirst, shortHash } from './audit-display'

describe('auditActionLabel', () => {
  it('labels the actions the API writes today', () => {
    expect(auditActionLabel('seed.loaded')).toBe('Demo data loaded')
    expect(auditActionLabel('invoice.imported')).toBe('Invoice imported')
    expect(auditActionLabel('link.requested')).toBe('Verification link requested')
    expect(auditActionLabel('verification.submitted')).toBe('Supplier answered')
  })

  it('shows an action without a label as the action itself', () => {
    expect(auditActionLabel('scenario.saved')).toBe('scenario.saved')
  })

  it('does not treat inherited object keys as labels', () => {
    expect(auditActionLabel('toString')).toBe('toString')
  })
})

describe('auditActorLabel', () => {
  it('labels the known actors and passes others through', () => {
    expect(auditActorLabel('seed')).toBe('Seed data')
    expect(auditActorLabel('buyer')).toBe('Buyer')
    expect(auditActorLabel('auditor')).toBe('auditor')
  })
})

describe('shortHash', () => {
  it('keeps the first twelve characters', () => {
    expect(shortHash({ ...audit(1), hash: 'f86066551c66a0b1c2d3e4f5' })).toBe('f86066551c66')
  })
})

describe('chainResultLabel', () => {
  it("says the chain is valid, or names the API's first bad entry", () => {
    expect(chainResultLabel({ valid: true, length: 7, brokenAt: null })).toBe('Chain valid')
    expect(chainResultLabel({ valid: false, length: 7, brokenAt: 3 })).toBe('Chain broken at entry 3')
  })

  it('never reads a broken chain as valid, even without a position', () => {
    expect(chainResultLabel({ valid: false, length: 7, brokenAt: null })).toBe('Chain broken at entry 7')
  })
})

describe('newestFirst', () => {
  it('reverses the ascending store order without changing the store array', () => {
    const ascending = [audit(1), audit(2), audit(3)]
    expect(newestFirst(ascending).map((e) => e.id)).toEqual([3, 2, 1])
    expect(ascending.map((e) => e.id)).toEqual([1, 2, 3])
  })
})
