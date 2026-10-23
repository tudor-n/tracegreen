import type { AuditEntry, DomainEvent, GraphSnapshot, Kpis, NodeView } from '@/api/client'

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
