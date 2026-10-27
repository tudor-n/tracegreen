import type { CarbonFigure, ClassificationView, FactorLookup, NodeView } from '@/api/client'
import { AI_MAPPED } from '@/copy/labels'

// What a supplier node shows, decided from the read model alone. Nothing here computes carbon.

/** The figure behind `carbonKg`: the activity figure once the node is on that basis, the spend figure otherwise. */
export function shownFigure(node: NodeView): CarbonFigure {
  return node.basis === 'activity' && node.activity !== null ? node.activity : node.spend
}

/** A factor row the seed still marks as a placeholder. It stays visible on screen (PRIORITY-CHANGES.md §2). */
export function isPlaceholder(factor: FactorLookup): boolean {
  return factor.source.includes('PLACEHOLDER')
}

/** "AI-mapped · 0.92" when an AI classification (cache or model) chose the category, null when the invoice did. */
export function aiMappedLabel(classification: ClassificationView): string | null {
  if (!classification.aiMapped) return null
  const { confidence } = classification
  return confidence === null || !Number.isFinite(confidence) ? AI_MAPPED : `${AI_MAPPED} · ${confidence.toFixed(2)}`
}

/** How many distinct invoices the suppliers came from. */
export function invoiceCount(nodes: readonly NodeView[]): number {
  return new Set(nodes.map((n) => n.invoice.id)).size
}

const monthYear = new Intl.DateTimeFormat('en-GB', { month: 'long', year: 'numeric', timeZone: 'UTC' })

/** The month of the latest invoice, "October 2026", or null with no (readable) invoice dates. */
export function invoicePeriod(nodes: readonly NodeView[]): string | null {
  const latest = nodes
    .map((n) => Date.parse(n.invoice.issueDate))
    .filter(Number.isFinite)
    .reduce((a, b) => Math.max(a, b), Number.NEGATIVE_INFINITY)
  return Number.isFinite(latest) ? monthYear.format(latest) : null
}
