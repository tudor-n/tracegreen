import type { AuditEntry, DomainEvent, GraphSnapshot, Kpis, NodeView, ScenarioResult } from '@/api/client'
import type { ScenarioPreview } from './graphStore'

// Small builders for the store tests. Only the fields the store and selectors read are meaningful.

export const kpis = (totalKg: number, nodeCount = 1): Kpis => ({
  totalKg,
  primaryShare: 0,
  estimatedShare: 1,
  spendEur: 0,
  nodeCount,
  verifiedCount: 0,
})

export function node(id: string, carbonKg: number, overrides: Partial<NodeView> = {}): NodeView {
  const figure = { kg: carbonKg, factors: [] }
  return {
    id,
    label: `Supplier ${id}`,
    country: 'RO',
    status: 'ESTIMATED',
    carbonKg,
    basis: 'spend',
    uncertainty: 'high',
    spend: figure,
    activity: null,
    classification: {
      categoryId: 'cat',
      categoryLabel: 'Category',
      source: 'invoice',
      confidence: null,
      aiMapped: false,
      lineDescription: 'line',
    },
    invoice: { id: `inv-${id}`, number: `INV-${id}`, issueDate: '2026-10-01', currency: 'EUR', total: 1000 },
    lane: {
      id: 'lane',
      origin: 'X',
      dest: 'Y',
      mode: 'road',
      modeDefault: 'road',
      modes: ['road'],
      distanceKm: null,
      illustrative: true,
    },
    massT: null,
    transportMode: null,
    energySource: null,
    spendEur: 1000,
    costEur: 100,
    leadDays: 3,
    warnings: [],
    createdAt: '2026-10-20T10:00:00Z',
    ...overrides,
  }
}

export const audit = (id: number): AuditEntry => ({
  id,
  workspaceId: 'ws',
  ts: '2026-10-20T10:00:00Z',
  actor: 'system',
  action: 'test',
  payload: {},
  prevHash: '0'.repeat(64),
  hash: String(id).padStart(64, '0'),
})

export function snapshot(lastSeq: number, nodes: NodeView[] = [], auditEntries: AuditEntry[] = []): GraphSnapshot {
  return {
    buyer: { name: 'Buyer SRL', cui: 'RO1', country: 'RO', city: 'Sibiu' },
    nodes,
    kpis: kpis(nodes.reduce((sum, n) => sum + n.carbonKg, 0), nodes.length),
    audit: auditEntries,
    lastSeq,
    mode: 'memory',
  }
}

const meta = (seq: number) => ({ seq, ts: '2026-10-20T10:00:00Z', workspaceId: 'ws' })

export const nodeEvent = (
  seq: number,
  n: NodeView,
  type: 'node.created' | 'node.updated' = 'node.created',
  k: Kpis = kpis(n.carbonKg),
): DomainEvent => ({ type, node: n, kpis: k, ...meta(seq) })

export const auditEvent = (seq: number, entry: AuditEntry): DomainEvent => ({
  type: 'audit.appended',
  entry,
  ...meta(seq),
})

export const resetEvent = (seq: number): DomainEvent => ({ type: 'graph.reset', ...meta(seq) })

/** N1 air → sea as the domain's SimulatorTests compute it: 7.5 t, €48,690, 40 days, win-win. */
export const scenarioResult = (overrides: Partial<ScenarioResult> = {}): ScenarioResult => ({
  carbonKg: 7488.8,
  costEur: 48_690,
  leadDays: 40,
  delta: { carbonKg: -15_074, costEur: -3910, leadDays: 33 },
  mac: { kind: 'abatement', macEurPerT: -259.4, deltaCostEur: -3910, deltaCarbonT: 15.074 },
  badge: 'win-win',
  inputs: [],
  massUnknown: false,
  illustrative: true,
  ...overrides,
})

/** A mode-change preview for `n`, simulated against its current figures. */
export const preview = (n: NodeView, result: ScenarioResult = scenarioResult()): ScenarioPreview => ({
  nodeId: n.id,
  change: { mode: 'sea', alternativeId: null },
  result,
  baseline: { carbonKg: n.carbonKg, costEur: n.costEur, leadDays: n.leadDays },
})
