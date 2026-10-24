import type { Kpis } from '@/api/client'

// Presentation only: the API sends kilograms, euros and days; these turn them into what the UI shows
// (CLAUDE.md: tonnes with one decimal, euros without decimals, days as integers). No carbon is computed here.

const tonnes = new Intl.NumberFormat('en-GB', { minimumFractionDigits: 1, maximumFractionDigits: 1 })
const euros = new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'EUR', maximumFractionDigits: 0 })
const percent = new Intl.NumberFormat('en-GB', { style: 'percent', maximumFractionDigits: 0 })
const integer = new Intl.NumberFormat('en-GB', { maximumFractionDigits: 0 })
const factorValue = new Intl.NumberFormat('en-GB', { maximumSignificantDigits: 6 })
const calendarDate = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' })

const MISSING = '–'

export const CO2E_UNIT = 'tCO₂e'

/** Rounding can give -0, which Intl prints as "-0". */
const noNegativeZero = (n: number) => (Object.is(n, -0) ? 0 : n)

/**
 * Kilograms as tonnes with one decimal, without a unit: 48 000 → "48.0". A positive amount below 0.05 t shows as
 * "<0.1" rather than "0.0", so a small supplier never reads as carbon-free.
 */
export function formatTonnes(kg: number): string {
  if (!Number.isFinite(kg)) return MISSING
  const t = kg / 1000
  if (t > 0 && t < 0.05) return '<0.1'
  return tonnes.format(noNegativeZero(Math.round(t * 10) / 10))
}

/** "48.0 tCO₂e". */
export function formatTonnesCo2e(kg: number): string {
  return `${formatTonnes(kg)} ${CO2E_UNIT}`
}

/** Whole euros: 12 345.6 → "€12,346". */
export function formatEuros(eur: number): string {
  if (!Number.isFinite(eur)) return MISSING
  return euros.format(noNegativeZero(Math.round(eur)))
}

/** Whole days: "1 day", "35 days". */
export function formatDays(days: number): string {
  if (!Number.isFinite(days)) return MISSING
  const n = noNegativeZero(Math.round(days))
  return `${integer.format(n)} ${Math.abs(n) === 1 ? 'day' : 'days'}`
}

/** A 0–1 share as a whole percentage: 0.374 → "37%". */
export function formatShare(fraction: number): string {
  if (!Number.isFinite(fraction)) return MISSING
  return percent.format(noNegativeZero(Math.round(fraction * 100) / 100))
}

/** A factor row's value as the API sent it, to six significant digits: 0.462 → "0.462", 5000 → "5,000". */
export function formatFactorValue(value: number): string {
  if (!Number.isFinite(value)) return MISSING
  return factorValue.format(noNegativeZero(value))
}

/** Whole kilometres: 7500 → "7,500 km". A lane without a distance shows the dash. */
export function formatKm(km: number | null): string {
  if (km === null || !Number.isFinite(km)) return MISSING
  return `${integer.format(noNegativeZero(Math.round(km)))} km`
}

const dateTimeOptions: Intl.DateTimeFormatOptions = {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hourCycle: 'h23',
}
const localDateTime = new Intl.DateTimeFormat('en-GB', dateTimeOptions)

/**
 * An instant from the API ("2026-11-01T09:03:00+00:00") as "1 Nov 2026, 09:03:00", in the viewer's time zone unless
 * one is given (the tests pass "UTC").
 */
export function formatDateTime(iso: string, timeZone?: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return MISSING
  const format = timeZone === undefined ? localDateTime : new Intl.DateTimeFormat('en-GB', { ...dateTimeOptions, timeZone })
  return format.format(date)
}

/** A calendar date from the API ("2026-10-28") as "28 Oct 2026", read in UTC so no time zone moves it a day. */
export function formatDate(isoDate: string): string {
  const date = new Date(`${isoDate.slice(0, 10)}T00:00:00Z`)
  return Number.isNaN(date.getTime()) ? MISSING : calendarDate.format(date)
}

export interface KpiFigures {
  /** Tonnes with one decimal, without the unit. */
  total: string
  share: string
  spend: string
  nodes: string
}

/** The KPI bar's figures, straight from the API's KPIs. */
export function formatKpis(kpis: Kpis): KpiFigures {
  return {
    total: formatTonnes(kpis.totalKg),
    // With no emissions there is nothing to share out, and "0%" would read as "none confirmed".
    share: kpis.totalKg > 0 ? formatShare(kpis.primaryShare) : MISSING,
    spend: formatEuros(kpis.spendEur),
    nodes: Number.isFinite(kpis.nodeCount) ? integer.format(kpis.nodeCount) : MISSING,
  }
}
