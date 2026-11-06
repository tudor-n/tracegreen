import type { ChatFactView, NodeStatus, TransportMode } from '@/api/client'
import { chatCopy } from '@/copy/chat'
import { auditPanelCopy, nodeCopy, transportModeCopy } from '@/copy/dashboard'
import {
  formatDate,
  formatDays,
  formatEuros,
  formatEurosDelta,
  formatShare,
  formatTonnesCo2e,
  formatTonnesDelta,
} from '@/store'

// How a chat answer's facts read (F13), from the API's raw values alone: the SPA formats, the API never sends text a
// reader sees as a figure. Nothing here computes carbon.

const DELTA_FIELDS = new Set(['carbonDelta', 'costDelta'])

/** The value as the reader sees it, formatted by its kind like everywhere else in the app. */
export function factValue(fact: ChatFactView): string {
  const n = Number(fact.value)
  switch (fact.kind) {
    case 'carbonKg':
      return DELTA_FIELDS.has(fact.field) ? `${formatTonnesDelta(n)} t` : formatTonnesCo2e(n)
    case 'eur':
      return DELTA_FIELDS.has(fact.field) ? formatEurosDelta(n) : formatEuros(n)
    case 'days':
      return formatDays(n)
    case 'share':
      return formatShare(n)
    case 'count':
      return String(Math.round(n))
    case 'date':
      return formatDate(fact.value)
    case 'status':
      return nodeCopy.status[fact.value as NodeStatus] ?? fact.value
    case 'mode':
      return transportModeCopy[fact.value as TransportMode] ?? fact.value
    case 'text':
      if (fact.field === 'action') return auditPanelCopy.actions[fact.value] ?? fact.value
      return fact.value === 'none' && fact.field.startsWith('list') ? chatCopy.none : fact.value
  }
}

/** What the value is, and whose: "Emissions · Oradea Harness SRL", or "Total emissions" for a total. */
export function factLabel(fact: ChatFactView): string {
  const field = chatCopy.fields[fact.field] ?? chatCopy.unknownField
  // A supplier's own name needs no "· name" after it.
  const subject = fact.field === 'name' ? null : fact.subject
  return chatCopy.factLabel(field, subject)
}

/** Where a fact links: its supplier on the Suppliers screen, or the audit log; null for a total. */
export type FactLink = { kind: 'node'; nodeId: string; label: string } | { kind: 'audit'; label: string } | null

export function factLink(fact: ChatFactView): FactLink {
  if (fact.nodeId !== null) return { kind: 'node', nodeId: fact.nodeId, label: chatCopy.openSupplier(fact.subject ?? '') }
  if (fact.auditId !== null) return { kind: 'audit', label: chatCopy.openAudit }
  return null
}
