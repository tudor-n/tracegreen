import type { CarbonFigure, ClassificationView, FactorLookup, NodeStatus, NodeView } from '@/api/client'
import { classificationCopy } from '@/copy/dashboard'
import { AI_MAPPED, BUYER_CORRECTED, provenance } from '@/copy/labels'

// What a supplier node shows, decided from the read model alone. Nothing here computes carbon.

/** The figure behind `carbonKg`: the activity figure once the node is on that basis, the spend figure otherwise. */
export function shownFigure(node: NodeView): CarbonFigure {
  return node.basis === 'activity' && node.activity !== null ? node.activity : node.spend
}

/**
 * Ruling 3's provenance line: "Supplier-confirmed · <source> <year>" once `carbonKg` is the activity figure, else
 * "Spend-based estimate · <source>". The spend figure's source is its first row, the emission factor; the rows after
 * it are the currency and price conversions. The activity figure names each distinct source and year of its rows.
 * Null when the figure has no factor rows (a row dropped from factors.json since the import), so there is no source
 * to name.
 */
export function provenanceLine(node: NodeView): string | null {
  if (node.basis === 'activity' && node.activity !== null) {
    const rows = [...new Map(node.activity.factors.map((f) => [`${f.source} ${f.year}`, f])).values()]
    const [first, ...rest] = rows
    if (first === undefined) return null
    return [provenance.activity(first.source, first.year), ...rest.map((f) => `${f.source} ${f.year}`)].join('; ')
  }
  const factor = node.spend.factors[0]
  return factor === undefined ? null : provenance.spend(factor.source)
}

/**
 * Whether a status change is the one the flip animation marks: anything else turning VERIFIED. The same node rendered
 * again, a first render, or a reset taking a node back to ESTIMATED never flips.
 */
export function justVerified(previous: NodeStatus, next: NodeStatus): boolean {
  return previous !== 'VERIFIED' && next === 'VERIFIED'
}

/** A factor row the seed still marks as a placeholder. It stays visible on screen (CLAUDE.md, Data rules). */
export function isPlaceholder(factor: FactorLookup): boolean {
  return factor.source.includes('PLACEHOLDER')
}

/**
 * The AI's reason for the category, shown only when the AI chose it (the label says whose words they are); null when the
 * invoice's own code decided, or when no reason was kept. The text is the model's: render it as text.
 */
export function aiReason(classification: ClassificationView): string | null {
  if (!classification.aiMapped) return null
  const reason = classification.rationale?.trim()
  return reason ? reason : null
}

/**
 * The chip naming who chose the category, with what it means: "AI-mapped · 0.92" for an AI classification, "Buyer-corrected"
 * once the buyer set it, nothing when the invoice's own code decided.
 */
export function classificationChip(classification: ClassificationView): { label: string; help: string } | null {
  if (classification.source === 'correction') return { label: BUYER_CORRECTED, help: classificationCopy.correctedHelp }
  const ai = aiMappedLabel(classification)
  return ai === null ? null : { label: ai, help: classificationCopy.aiMappedHelp }
}

/** "AI-mapped · 0.92" when an AI classification (cache or model) chose the category, null when the invoice did. */
export function aiMappedLabel(classification: ClassificationView): string | null {
  if (!classification.aiMapped) return null
  const { confidence } = classification
  return confidence === null || !Number.isFinite(confidence) ? AI_MAPPED : `${AI_MAPPED} · ${confidence.toFixed(2)}`
}
