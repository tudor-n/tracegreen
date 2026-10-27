import type { CarbonBasis, MacBadge, Uncertainty } from '@/api/client'

// Labels a juror reads anywhere in the SPA: rulings 2–4 of docs/PRIORITY-CHANGES.md, the AI labels of CLAUDE.md, the
// MAC badge names and the audit words. Components import them from here instead of writing strings inline. Records are
// keyed by the generated types, so a new value in the contract fails the typecheck until it has a label.

/** Ruling 2, word for word. `primaryShare` on the wire. */
export const supplierConfirmedShare = {
  label: 'Supplier-confirmed share',
  subtitle: 'by emissions, not by node count',
  tooltip:
    'share of total kgCO₂e from nodes the supplier confirmed — activity data from official factors, not externally verified PCFs',
} as const

/**
 * Ruling 3: the drawer's provenance line, by the `basis` of the figure behind `carbonKg`, with that figure's factor
 * `source` and `year` (docs/NEXT.md). PARTIAL nodes stay on the spend figure.
 */
export const provenance = {
  activity: (source: string, year: number) => `Supplier-confirmed · ${source} ${year}`,
  spend: (source: string) => `Spend-based estimate · ${source}`,
} satisfies Record<CarbonBasis, (source: string, year: number) => string>

/** Ruling 4: shown as a label, never as ±%. */
export const uncertaintyLabel = {
  low: 'Low uncertainty',
  medium: 'Medium uncertainty',
  high: 'High uncertainty',
} satisfies Record<Uncertainty, string>

/** CLAUDE.md: the label on every value an AI classification produced (cache or model). */
export const AI_MAPPED = 'AI-mapped'
/** CLAUDE.md: the label on every alternative the AI suggested. */
export const AI_SUGGESTED = 'AI-suggested · illustrative'
/** CLAUDE.md: the label on every field the AI read from a PDF. */
export const AI_EXTRACTED = 'AI-extracted · check fields'
/** Lane cost and lead time, and any other figure that is not measured. */
export const ILLUSTRATIVE = 'illustrative'

/** The simulator's MAC badge, by the generated badge value. */
export const macBadgeLabel = {
  'win-win': 'Win-win',
  'below-ets': 'Below the carbon price',
  premium: 'Premium option',
  'not-abatement': 'Not an abatement option',
} satisfies Record<MacBadge, string>

/** The audit panel (CLAUDE.md: the audit log is append-only and hash-chained). */
export const auditCopy = {
  title: 'Tamper-evident audit log',
  chainValid: 'Chain valid',
  chainBroken: (entry: number) => `Chain broken at entry ${entry}`,
} as const
