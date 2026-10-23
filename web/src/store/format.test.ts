import { describe, expect, it } from 'vitest'
import { formatDays, formatEuros, formatShare, formatTonnes, formatTonnesCo2e } from './format'

describe('formatTonnes', () => {
  it('turns kilograms into tonnes with one decimal', () => {
    expect(formatTonnes(48_000)).toBe('48.0')
    expect(formatTonnes(1234)).toBe('1.2')
    expect(formatTonnes(1250)).toBe('1.3')
    expect(formatTonnes(1_234_567)).toBe('1,234.6')
    expect(formatTonnesCo2e(2300)).toBe('2.3 tCO₂e')
  })

  it('shows a small positive amount as <0.1 and zero as 0.0', () => {
    expect(formatTonnes(30)).toBe('<0.1')
    expect(formatTonnes(50)).toBe('0.1')
    expect(formatTonnes(0)).toBe('0.0')
    expect(formatTonnes(-0.0001)).toBe('0.0')
  })

  it('prints a dash for a missing number', () => {
    expect(formatTonnes(Number.NaN)).toBe('–')
  })
})

describe('formatEuros', () => {
  it('rounds to whole euros', () => {
    expect(formatEuros(12_345.6)).toBe('€12,346')
    expect(formatEuros(0.4)).toBe('€0')
    expect(formatEuros(-0.4)).toBe('€0')
    expect(formatEuros(-1500)).toBe('-€1,500')
  })
})

describe('formatDays', () => {
  it('rounds to whole days', () => {
    expect(formatDays(1)).toBe('1 day')
    expect(formatDays(34.6)).toBe('35 days')
    expect(formatDays(0)).toBe('0 days')
  })
})

describe('formatShare', () => {
  it('shows a 0–1 share as a whole percentage', () => {
    expect(formatShare(0.374)).toBe('37%')
    expect(formatShare(1)).toBe('100%')
    expect(formatShare(0)).toBe('0%')
  })
})
