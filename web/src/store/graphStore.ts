import { useStore } from 'zustand'
import { createStore, type StoreApi } from 'zustand/vanilla'
import type {
  AuditEntry,
  Buyer,
  DomainEvent,
  GraphSnapshot,
  Kpis,
  NodeView,
  ScenarioChange,
  ScenarioResult,
} from '@/api/client'
import type { GraphStoreActions } from '@/hooks/startGraphSync'

/** What the API last told us. Only `hydrate` and `applyEvent` change it; React Flow never writes here. */
export interface GraphDomain {
  buyer: Buyer | null
  /** Domain nodes by id, exactly as the API sent them. */
  nodes: Record<string, NodeView>
  kpis: Kpis | null
  /** Ascending by id, without duplicates. */
  audit: AuditEntry[]
  /** Null until the first snapshot, which is how components tell "loading" from "empty". */
  lastSeq: number | null
  mode: string
}

export type Panel = 'drawer' | 'audit' | 'simulator'

/** The figures of the base node that a scenario's deltas are taken against. */
export type ScenarioBaseline = Pick<NodeView, 'carbonKg' | 'costEur' | 'leadDays'>

/** A simulated change shown as a ghost node (B5). The base node never changes. */
export interface ScenarioPreview {
  nodeId: string
  change: ScenarioChange
  result: ScenarioResult
  /** The base node's figures when the simulation was asked for. */
  baseline: ScenarioBaseline
}

export const baselineOf = (node: NodeView): ScenarioBaseline => ({
  carbonKg: node.carbonKg,
  costEur: node.costEur,
  leadDays: node.leadDays,
})

/**
 * The preview while it still describes its node, else null: the node is gone, or its figures moved (a verification
 * rebuilds them) and the result's deltas no longer hold. A snapshot repeating the same figures keeps it.
 */
export function keepPreview(preview: ScenarioPreview | null, nodes: Record<string, NodeView>): ScenarioPreview | null {
  if (preview === null) return null
  const node = nodes[preview.nodeId]
  if (node === undefined) return null
  const now = baselineOf(node)
  const { baseline } = preview
  const same =
    now.carbonKg === baseline.carbonKg && now.costEur === baseline.costEur && now.leadDays === baseline.leadDays
  return same ? preview : null
}

export interface GraphUi {
  selectedNodeId: string | null
  openPanels: Record<Panel, boolean>
  scenarioPreview: ScenarioPreview | null
}

export interface GraphActions extends GraphStoreActions {
  /** Selecting a node opens the drawer; `null` closes it. */
  select(nodeId: string | null): void
  setPanel(panel: Panel, open: boolean): void
  setScenarioPreview(preview: ScenarioPreview | null): void
}

export type GraphState = GraphDomain & GraphUi & GraphActions

export const initialDomain: GraphDomain = { buyer: null, nodes: {}, kpis: null, audit: [], lastSeq: null, mode: '' }

export const initialUi: GraphUi = {
  selectedNodeId: null,
  openPanels: { drawer: false, audit: false, simulator: false },
  scenarioPreview: null,
}

/**
 * Replaces the whole domain, `lastSeq` included even when it goes down: the sequence restarts at 0 when the API
 * restarts. UI state survives unless it points at a node the snapshot no longer has (after a reset, say), and the
 * scenario preview only while its node keeps its figures (keepPreview).
 */
export function hydrateState(state: GraphUi, snapshot: GraphSnapshot): GraphDomain & GraphUi {
  const nodes = Object.fromEntries(snapshot.nodes.map((n) => [n.id, n]))
  const selectionGone = state.selectedNodeId !== null && !(state.selectedNodeId in nodes)
  return {
    buyer: snapshot.buyer,
    nodes,
    kpis: snapshot.kpis,
    audit: dedupeAudit(snapshot.audit),
    lastSeq: snapshot.lastSeq,
    mode: snapshot.mode,
    selectedNodeId: selectionGone ? null : state.selectedNodeId,
    openPanels: selectionGone ? { ...state.openPanels, drawer: false, simulator: false } : state.openPanels,
    scenarioPreview: selectionGone ? null : keepPreview(state.scenarioPreview, nodes),
  }
}

/**
 * Applies one event on top of the domain, or returns `state` itself when there is nothing to do, so subscribers
 * are not notified. An event at or below `lastSeq` is already in the state. Applying is idempotent anyway (nodes are
 * upserted by id, audit entries already listed are skipped), because an event can repeat what a snapshot holds.
 */
export function applyEventState(state: GraphDomain, event: DomainEvent): GraphDomain {
  if (state.lastSeq !== null && event.seq <= state.lastSeq) return state
  switch (event.type) {
    case 'node.created':
    case 'node.updated':
    case 'node.verified':
      return { ...state, nodes: { ...state.nodes, [event.node.id]: event.node }, kpis: event.kpis, lastSeq: event.seq }
    case 'audit.appended': {
      const listed = state.audit.some((e) => e.id === event.entry.id)
      return { ...state, audit: listed ? state.audit : insertAudit(state.audit, event.entry), lastSeq: event.seq }
    }
    case 'scenario.saved':
      // Saved scenarios are not rendered yet (B5); the sequence still moves on so later events stay in order.
      return { ...state, lastSeq: event.seq }
    case 'graph.reset':
      // Never forwarded by the sync, which refetches the snapshot instead (startGraphSync.ts). Ignored if it comes.
      return state
  }
}

function insertAudit(audit: AuditEntry[], entry: AuditEntry): AuditEntry[] {
  const at = audit.findIndex((e) => e.id > entry.id)
  return at === -1 ? [...audit, entry] : [...audit.slice(0, at), entry, ...audit.slice(at)]
}

function dedupeAudit(audit: AuditEntry[]): AuditEntry[] {
  const byId = new Map(audit.map((e) => [e.id, e]))
  return [...byId.values()].sort((a, b) => a.id - b.id)
}

/** False until the first snapshot has arrived. */
export const selectLoaded = (state: GraphState): boolean => state.lastSeq !== null

export type GraphStore = StoreApi<GraphState>

/** A fresh store; the app uses the one below, tests make their own. */
export function createGraphStore(): GraphStore {
  return createStore<GraphState>()((set) => ({
    ...initialDomain,
    ...initialUi,
    hydrate: (snapshot) => set((s) => hydrateState(s, snapshot)),
    // Returning the same state object skips notifying subscribers. A node event can move the figures the scenario
    // preview was simulated against, so the preview is checked again.
    applyEvent: (event) =>
      set((s) => {
        const next = applyEventState(s, event)
        return next === s ? s : { ...next, scenarioPreview: keepPreview(s.scenarioPreview, next.nodes) }
      }),
    select: (nodeId) =>
      set((s) => {
        if (nodeId !== null && !(nodeId in s.nodes)) return s
        const open = nodeId !== null
        return {
          selectedNodeId: nodeId,
          openPanels: { ...s.openPanels, drawer: open, simulator: open && s.openPanels.simulator },
          scenarioPreview: s.scenarioPreview?.nodeId === nodeId ? s.scenarioPreview : null,
        }
      }),
    // Closing the simulator takes its ghost off the canvas.
    setPanel: (panel, open) =>
      set((s) => {
        if (s.openPanels[panel] === open) return s
        const preview = panel === 'simulator' && !open ? null : s.scenarioPreview
        return { openPanels: { ...s.openPanels, [panel]: open }, scenarioPreview: preview }
      }),
    setScenarioPreview: (preview) => set({ scenarioPreview: preview }),
  }))
}

export const graphStore = createGraphStore()

/** Reads the app store. Pass a selector that returns a stable value (a field, or a memoised selector from this folder). */
export function useGraphStore<T>(selector: (state: GraphState) => T): T {
  return useStore(graphStore, selector)
}
