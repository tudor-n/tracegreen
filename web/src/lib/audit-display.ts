import type { AuditEntry, ChainVerification } from '@/api/client'
import { auditPanelCopy } from '@/copy/dashboard'
import { auditCopy } from '@/copy/labels'

// What the audit panel shows for an entry, decided from the entry alone.

/** A label from `labels`, or `key` itself. Own keys only: an action named "toString" must not find Object's method. */
function labelOr(labels: Readonly<Partial<Record<string, string>>>, key: string): string {
  return (Object.hasOwn(labels, key) ? labels[key] : undefined) ?? key
}

/** The label for an action, or the action itself when it has none yet ("scenario.saved" before its label lands). */
export function auditActionLabel(action: string): string {
  return labelOr(auditPanelCopy.actions, action)
}

/** The label for an actor, or the actor itself when it has none yet. */
export function auditActorLabel(actor: string): string {
  return labelOr(auditPanelCopy.actors, actor)
}

/** The start of the entry's hash, enough to tell entries apart on screen; the full hash goes in the tooltip. */
export function shortHash(entry: AuditEntry): string {
  return entry.hash.slice(0, 12)
}

/**
 * "Chain valid", or "Chain broken at entry n" with the API's 1-based position, which is the entry's #id: ids start at 1
 * and a demo reset restarts them. The API always names the entry when the chain is broken; the length is only the
 * fallback the type allows.
 */
export function chainResultLabel(result: ChainVerification): string {
  return result.valid ? auditCopy.chainValid : auditCopy.chainBroken(result.brokenAt ?? result.length)
}

/** Newest entry first, for the panel. The store keeps them ascending by id. */
export function newestFirst(audit: readonly AuditEntry[]): AuditEntry[] {
  return [...audit].reverse()
}
