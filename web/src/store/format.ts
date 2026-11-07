import type { Kpis } from '@/api/client'

// Presentation only: the API sends kilograms, euros and days; these turn them into what the UI shows
// (CLAUDE.md: tonnes with two decimals, euros without decimals, days as integers). No carbon is computed here.

const tonnes = new Intl.NumberFormat('en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const tonnesDelta = new Intl.NumberFormat('en-GB', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
  signDisplay: 'exceptZero',
})
const euros = new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'EUR', maximumFractionDigits: 0 })
const eurosDelta = new Intl.NumberFormat('en-GB', {
  style: 'currency',
  currency: 'EUR',
  maximumFractionDigits: 0,
  signDisplay: 'exceptZero',
})
const integerDelta = new Intl.NumberFormat('en-GB', { maximumFractionDigits: 0, signDisplay: 'exceptZero' })
const percent = new Intl.NumberFormat('en-GB', { style: 'percent', maximumFractionDigits: 0 })
const integer = new Intl.NumberFormat('en-GB', { maximumFractionDigits: 0 })
const factorValue = new Intl.NumberFormat('en-GB', { maximumSignificantDigits: 6 })
const calendarDate = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' })

const MISSING = '–'

export const CO2E_UNIT = 'tCO₂e'

/** Rounding can give -0, which Intl prints as "-0". */
const noNegativeZero = (n: number) => (Object.is(n, -0) ? 0 : n)

/** Kilograms in the hundredths of a tonne the UI shows (10 kg each): 5 386.24 → 539. */
const shownHundredths = (kg: number) => Math.round(kg / 10)

/**
 * Kilograms as tonnes with two decimals, without a unit: 48 000 → "48.00", 5 386.24 → "5.39". A positive amount below
 * 0.005 t shows as "<0.01" rather than "0.00", so a small supplier never reads as carbon-free.
 */
export function formatTonnes(kg: number): string {
  if (!Number.isFinite(kg)) return MISSING
  if (kg > 0 && kg < 5) return '<0.01'
  return tonnes.format(noNegativeZero(shownHundredths(kg) / 100))
}

/**
 * The gap between two figures shown side by side, worked out from the figures as shown, so the three always agree on
 * screen: counted 5 386.24 kg ("5.39") and answered 3 245.95 kg ("3.25") give "2.14", whatever the exact gap rounds to.
 */
export function formatTonnesGap(higherKg: number, lowerKg: number): string {
  if (!Number.isFinite(higherKg) || !Number.isFinite(lowerKg)) return MISSING
  return tonnes.format(noNegativeZero(Math.abs(shownHundredths(higherKg) - shownHundredths(lowerKg)) / 100))
}

/** The change from one shown figure to another, signed, worked out from the figures as shown (see formatTonnesGap). */
export function formatTonnesChange(afterKg: number, beforeKg: number): string {
  if (!Number.isFinite(afterKg) || !Number.isFinite(beforeKg)) return MISSING
  return tonnesDelta.format(noNegativeZero((shownHundredths(afterKg) - shownHundredths(beforeKg)) / 100))
}

/** "48.0 tCO₂e". */
export function formatTonnesCo2e(kg: number): string {
  return `${formatTonnes(kg)} ${CO2E_UNIT}`
}

/**
 * A change in kilograms (a scenario delta from the API) as signed tonnes with two decimals, without a unit:
 * −15 074 → "-15.07", 400 → "+0.40". A change that rounds to nothing reads "0.00", without a sign. Where both figures
 * are on screen, formatTonnesChange keeps the change in step with them.
 */
export function formatTonnesDelta(kg: number): string {
  if (!Number.isFinite(kg)) return MISSING
  return tonnesDelta.format(noNegativeZero(shownHundredths(kg) / 100))
}

/** Whole euros: 12 345.6 → "€12,346". */
export function formatEuros(eur: number): string {
  if (!Number.isFinite(eur)) return MISSING
  return euros.format(noNegativeZero(Math.round(eur)))
}

/** A change in euros, signed and whole: −3 910 → "-€3,910", 8 350 → "+€8,350", 0.4 → "€0". */
export function formatEurosDelta(eur: number): string {
  if (!Number.isFinite(eur)) return MISSING
  return eurosDelta.format(noNegativeZero(Math.round(eur)))
}

/** Whole days: "1 day", "35 days". */
export function formatDays(days: number): string {
  if (!Number.isFinite(days)) return MISSING
  const n = noNegativeZero(Math.round(days))
  return `${integer.format(n)} ${Math.abs(n) === 1 ? 'day' : 'days'}`
}

/** A change in days, signed and whole: 33 → "+33 days", −1 → "-1 day", 0 → "0 days". */
export function formatDaysDelta(days: number): string {
  if (!Number.isFinite(days)) return MISSING
  const n = noNegativeZero(Math.round(days))
  return `${integerDelta.format(n)} ${Math.abs(n) === 1 ? 'day' : 'days'}`
}

/** A 0–1 share as a whole percentage: 0.374 → "37%". */
export function formatShare(fraction: number): string {
  if (!Number.isFinite(fraction)) return MISSING
  return percent.format(noNegativeZero(Math.round(fraction * 100) / 100))
}

/**
 * The two halves of the total as whole percentages that add up to 100. The API's estimatedShare is 1 − primaryShare,
 * so the supplier-confirmed share is rounded and the estimated share is the rest: 0.125 shows as 13% and 87%, never
 * 13% and 88%. Both are the dash when there are no emissions to share out.
 */
export function formatShareSplit(kpis: Pick<Kpis, 'totalKg' | 'primaryShare'>): { confirmed: string; estimated: string } {
  if (!(kpis.totalKg > 0) || !Number.isFinite(kpis.primaryShare)) return { confirmed: MISSING, estimated: MISSING }
  const confirmed = Math.min(100, Math.max(0, Math.round(kpis.primaryShare * 100)))
  return { confirmed: percent.format(confirmed / 100), estimated: percent.format((100 - confirmed) / 100) }
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

/**
 * Time left in its two largest whole units, rounded down so a link never reads as lasting longer than it does:
 * 7 days less a minute → "6d 23h", 90 s → "1m 30s", 40 s → "40s". Nothing left (or less) reads "0s".
 */
export function formatTimeLeft(ms: number): string {
  if (!Number.isFinite(ms)) return MISSING
  const total = Math.max(0, Math.floor(ms / 1000))
  const days = Math.floor(total / 86_400)
  const hours = Math.floor(total / 3600) % 24
  const minutes = Math.floor(total / 60) % 60
  const seconds = total % 60
  if (days > 0) return `${days}d ${hours}h`
  if (hours > 0) return `${hours}h ${minutes}m`
  if (minutes > 0) return `${minutes}m ${seconds}s`
  return `${seconds}s`
}

export interface KpiFigures {
  /** Tonnes with two decimals, without the unit. */
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
