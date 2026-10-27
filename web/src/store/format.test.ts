import { describe, expect, it } from 'vitest'
import {
  formatDate,
  formatDateTime,
  formatDays,
  formatEuros,
  formatFactorValue,
  formatKm,
  formatKpis,
  formatShare,
  formatTimeLeft,
  formatTonnes,
  formatTonnesCo2e,
  formatTonnesDelta,
  formatEurosDelta,
  formatDaysDelta,
} from './format'
import { kpis } from './testFixtures'

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

describe('formatTonnesDelta', () => {
  it('signs a change in tonnes with one decimal', () => {
    expect(formatTonnesDelta(-15_074)).toBe('-15.1')
    expect(formatTonnesDelta(400)).toBe('+0.4')
    expect(formatTonnesDelta(1_234_567)).toBe('+1,234.6')
  })

  it('reads 0.0 without a sign when the change rounds to nothing, and the dash for no number', () => {
    expect(formatTonnesDelta(0)).toBe('0.0')
    expect(formatTonnesDelta(20)).toBe('0.0')
    expect(formatTonnesDelta(-20)).toBe('0.0')
    expect(formatTonnesDelta(Number.NaN)).toBe('–')
  })
})

describe('formatEurosDelta and formatDaysDelta', () => {
  it('sign a change in whole euros and whole days', () => {
    expect(formatEurosDelta(-3910)).toBe('-€3,910')
    expect(formatEurosDelta(8350.4)).toBe('+€8,350')
    expect(formatDaysDelta(33)).toBe('+33 days')
    expect(formatDaysDelta(-5)).toBe('-5 days')
    expect(formatDaysDelta(-1)).toBe('-1 day')
  })

  it('read a change that rounds to nothing without a sign, and the dash for no number', () => {
    expect(formatEurosDelta(0.4)).toBe('€0')
    expect(formatEurosDelta(-0.4)).toBe('€0')
    expect(formatDaysDelta(0)).toBe('0 days')
    expect(formatDaysDelta(-0.2)).toBe('0 days')
    expect(formatEurosDelta(Number.NaN)).toBe('–')
    expect(formatDaysDelta(Number.POSITIVE_INFINITY)).toBe('–')
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

describe('formatFactorValue', () => {
  it('keeps the value the API sent, to six significant digits', () => {
    expect(formatFactorValue(0.462)).toBe('0.462')
    expect(formatFactorValue(1.1193)).toBe('1.1193')
    expect(formatFactorValue(5000)).toBe('5,000')
    expect(formatFactorValue(0.123456789)).toBe('0.123457')
    expect(formatFactorValue(Number.NaN)).toBe('–')
  })
})

describe('formatKm', () => {
  it('shows whole kilometres, and the dash for a lane without a distance', () => {
    expect(formatKm(7500)).toBe('7,500 km')
    expect(formatKm(412.6)).toBe('413 km')
    expect(formatKm(null)).toBe('–')
    expect(formatKm(Number.NaN)).toBe('–')
  })
})

describe('formatDate', () => {
  it('shows a calendar date without shifting it by a time zone', () => {
    expect(formatDate('2026-10-28')).toBe('28 Oct 2026')
    expect(formatDate('2026-01-01')).toBe('1 Jan 2026')
  })

  it('shows the dash for something that is not a date', () => {
    expect(formatDate('soon')).toBe('–')
  })
})

describe('formatDateTime', () => {
  it('shows the instant with seconds on a 24-hour clock', () => {
    expect(formatDateTime('2026-11-01T09:03:00+00:00', 'UTC')).toBe('1 Nov 2026, 09:03:00')
    expect(formatDateTime('2026-10-24T13:05:09.123Z', 'UTC')).toBe('24 Oct 2026, 13:05:09')
  })

  it('converts to the given time zone', () => {
    expect(formatDateTime('2026-10-24T21:30:00Z', 'Europe/Bucharest')).toBe('25 Oct 2026, 00:30:00')
  })

  it('shows the dash for something that is not an instant', () => {
    expect(formatDateTime('later')).toBe('–')
  })
})

describe('formatTimeLeft', () => {
  const s = 1000
  const min = 60 * s
  const h = 60 * min
  const d = 24 * h

  it('shows the two largest units, rounded down', () => {
    expect(formatTimeLeft(7 * d - s)).toBe('6d 23h')
    expect(formatTimeLeft(7 * d)).toBe('7d 0h')
    expect(formatTimeLeft(5 * h + 12 * min + 59 * s)).toBe('5h 12m')
    expect(formatTimeLeft(90 * s)).toBe('1m 30s')
    expect(formatTimeLeft(40 * s + 999)).toBe('40s')
  })

  it('reads 0s when nothing is left, and the dash for no time at all', () => {
    expect(formatTimeLeft(999)).toBe('0s')
    expect(formatTimeLeft(-5 * min)).toBe('0s')
    expect(formatTimeLeft(Number.NaN)).toBe('–')
  })
})

describe('formatKpis', () => {
  it('formats the API figures for the KPI bar', () => {
    const figures = formatKpis({ ...kpis(37_046.86, 5), primaryShare: 0.1273, spendEur: 131_500 })
    expect(figures).toEqual({ total: '37.0', share: '13%', spend: '€131,500', nodes: '5' })
  })

  it('shows no share when there are no emissions to share out', () => {
    expect(formatKpis(kpis(0, 0))).toEqual({ total: '0.0', share: '–', spend: '€0', nodes: '0' })
  })
})
