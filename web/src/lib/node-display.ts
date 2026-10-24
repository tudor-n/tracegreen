import type { CarbonFigure, ClassificationView, FactorLookup, NodeView } from '@/api/client'
import { AI_MAPPED, provenanceCopy } from '@/copy/dashboard'

// What a supplier node shows, decided from the read model alone. Nothing here computes carbon.

/** The figure behind `carbonKg`: the activity figure once the node is on that basis, the spend figure otherwise. */
export function shownFigure(node: NodeView): CarbonFigure {
  return node.basis === 'activity' && node.activity !== null ? node.activity : node.spend
}

/**
 * Ruling 3's provenance line: "Supplier-confirmed · <source> <year>" once `carbonKg` is the activity figure, else
 * "Spend-based estimate · <source>". The spend figure's source is its first row, the emission factor; the rows after
 * it are the currency and price conversions. The activity figure names each distinct source of its rows.
 */
export function provenanceLine(node: NodeView): string {
  if (node.basis === 'activity' && node.activity !== null) {
    const sources = [...new Set(node.activity.factors.map((f) => `${f.source} ${f.year}`))]
    return [provenanceCopy.activity, sources.join('; ')].filter(Boolean).join(' · ')
  }
  const factor = node.spend.factors[0]
  return factor === undefined ? provenanceCopy.spend : `${provenanceCopy.spend} · ${factor.source}`
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
