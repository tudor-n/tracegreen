import { describe, expect, it } from 'vitest'
import type { FactorLookup } from '@/api/client'
import { node } from '@/store/testFixtures'
import { aiMappedLabel, isPlaceholder, justVerified, provenanceLine, shownFigure } from './node-display'

const row = (source: string): FactorLookup => ({ value: 1, unit: 'kgCO2e/t', source, year: 2026, rowLabel: 'row' })

describe('shownFigure', () => {
  const spend = { kg: 22_563, factors: [row('US EPA v1.3')] }
  const activity = { kg: 4716, factors: [row('PLACEHOLDER - replace at T4')] }

  it('is the spend figure while the node is on the spend basis, even when an activity figure exists', () => {
    expect(shownFigure(node('a', 22_563, { spend, activity: null }))).toBe(spend)
    expect(shownFigure(node('a', 22_563, { basis: 'spend', spend, activity, status: 'PARTIAL' }))).toBe(spend)
  })

  it('is the activity figure once the node is on that basis', () => {
    expect(shownFigure(node('a', 4716, { basis: 'activity', spend, activity, status: 'VERIFIED' }))).toBe(activity)
  })

  it('falls back to the spend figure when the activity figure is missing', () => {
    expect(shownFigure(node('a', 22_563, { basis: 'activity', spend, activity: null }))).toBe(spend)
  })
})

describe('isPlaceholder', () => {
  it('flags a source the seed marks PLACEHOLDER, and nothing else', () => {
    expect(isPlaceholder(row('PLACEHOLDER - replace with a DESNZ 2026 Freighting goods or EEA row at T4'))).toBe(true)
    expect(isPlaceholder(row('US EPA Supply Chain GHG Emission Factors v1.3'))).toBe(false)
  })
})

describe('aiMappedLabel', () => {
  const classification = node('a', 1).classification

  it('labels a cache or model classification with its confidence to two decimals', () => {
    expect(aiMappedLabel({ ...classification, source: 'cache', aiMapped: true, confidence: 0.9 })).toBe('AI-mapped · 0.90')
    expect(aiMappedLabel({ ...classification, source: 'model', aiMapped: true, confidence: 0.923 })).toBe('AI-mapped · 0.92')
  })

  it('drops the confidence when there is none', () => {
    expect(aiMappedLabel({ ...classification, source: 'model', aiMapped: true, confidence: null })).toBe('AI-mapped')
  })

  it('shows nothing when the invoice itself classified the line', () => {
    expect(aiMappedLabel({ ...classification, source: 'invoice', aiMapped: false, confidence: 1 })).toBeNull()
  })
})

describe('provenanceLine', () => {
  const epa = { ...row('US EPA Supply Chain GHG Emission Factors v1.3'), year: 2024 }
  const ecb = { ...row('ECB euro foreign exchange reference rates'), unit: 'USD/EUR' }
  const sea = { ...row('PLACEHOLDER - DESNZ Freighting goods'), year: 2026 }
  const pcb = { ...row('PLACEHOLDER - DESNZ Material use'), year: 2026 }

  it('names the emission factor of a spend estimate, not the conversion rows after it', () => {
    const n = node('a', 22_563, { spend: { kg: 22_563, factors: [epa, ecb] } })
    expect(provenanceLine(n)).toBe('Spend-based estimate · US EPA Supply Chain GHG Emission Factors v1.3')
  })

  it('stays on the spend estimate while a partial node keeps the spend basis', () => {
    const n = node('a', 2000, {
      status: 'PARTIAL',
      spend: { kg: 2000, factors: [epa] },
      activity: { kg: 900, factors: [sea] },
    })
    expect(provenanceLine(n)).toBe('Spend-based estimate · US EPA Supply Chain GHG Emission Factors v1.3')
  })

  it('names each distinct source and year of a supplier-confirmed figure', () => {
    const n = node('a', 4716, {
      status: 'VERIFIED',
      basis: 'activity',
      spend: { kg: 4700, factors: [epa] },
      activity: { kg: 4716, factors: [sea, pcb, sea] },
    })
    expect(provenanceLine(n)).toBe(
      'Supplier-confirmed · PLACEHOLDER - DESNZ Freighting goods 2026; PLACEHOLDER - DESNZ Material use 2026',
    )
  })

  it('falls back to the bare label when the figure has no factor rows', () => {
    expect(provenanceLine(node('a', 1))).toBe('Spend-based estimate')
    expect(provenanceLine(node('a', 1, { basis: 'activity', activity: { kg: 1, factors: [] } }))).toBe(
      'Supplier-confirmed',
    )
  })
})

describe('justVerified', () => {
  it('flips when an estimated or partial node turns verified', () => {
    expect(justVerified('ESTIMATED', 'VERIFIED')).toBe(true)
    expect(justVerified('PARTIAL', 'VERIFIED')).toBe(true)
  })

  it('does not flip a node that was already verified, or one that is not verified now', () => {
    expect(justVerified('VERIFIED', 'VERIFIED')).toBe(false)
    expect(justVerified('VERIFIED', 'ESTIMATED')).toBe(false)
    expect(justVerified('ESTIMATED', 'PARTIAL')).toBe(false)
    expect(justVerified('ESTIMATED', 'ESTIMATED')).toBe(false)
  })
})
