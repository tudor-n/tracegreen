import type { Edge, Node } from '@xyflow/react'
import type { Buyer, NodeView } from '@/api/client'
import type { GraphState } from './graphStore'
import { BUYER_NODE_ID, createLayoutMemo, NODE_SIZE, type XY } from './layout'

// React Flow gets arrays derived from the store and never writes back (roadmap §8.4): the canvas is controlled,
// clicks go to `select`, and nothing React Flow creates is stored.

export type SupplierNodeData = { node: NodeView; hotspot: boolean }
export type BuyerNodeData = { buyer: Buyer }
export type SupplierRfNode = Node<SupplierNodeData, 'supplier'>
export type BuyerRfNode = Node<BuyerNodeData, 'buyer'>
export type GraphRfNode = SupplierRfNode | BuyerRfNode
export type SupplierRfEdge = Edge<{ width: number }>

/** How many of the largest emitters get the hotspot ring. */
export const HOTSPOT_COUNT = 1

export const EDGE_WIDTH = { min: 1, max: 8, perLogKg: 0.6 } as const

/** Nodes in a stable order: oldest first, then by id. */
export function sortedNodes(nodes: Record<string, NodeView>): NodeView[] {
  return Object.values(nodes).sort((a, b) => a.createdAt.localeCompare(b.createdAt) || a.id.localeCompare(b.id))
}

/**
 * The ids of the `count` largest emitters by `carbonKg`, ties broken by id. A node without carbon is never a
 * hotspot. This is an overlay for the ring, never a status.
 */
export function hotspotIds(nodes: readonly NodeView[], count: number = HOTSPOT_COUNT): Set<string> {
  const ranked = nodes
    .filter((n) => n.carbonKg > 0)
    .sort((a, b) => b.carbonKg - a.carbonKg || a.id.localeCompare(b.id))
  return new Set(ranked.slice(0, Math.max(0, count)).map((n) => n.id))
}

/** Stroke width in px from a clamped natural log of the node's kilograms (roadmap §6: edge_width = clamp(log(kg))). */
export function edgeWidth(carbonKg: number): number {
  const scaled = carbonKg > 1 ? Math.log(carbonKg) * EDGE_WIDTH.perLogKg : 0
  return Math.min(EDGE_WIDTH.max, Math.max(EDGE_WIDTH.min, scaled))
}

/**
 * A node's fixed size, as React Flow's `width`, `height` and `measured`. The canvas is controlled and never feeds React
 * Flow's measurements back, so without `measured` every new node object would count as unmeasured: React Flow would
 * drop its handle positions and measure it again, and the edges would blink on every event.
 */
const sized = ({ width, height }: { width: number; height: number }) => ({ width, height, measured: { width, height } })

/** The buyer node (when the snapshot has one) and every supplier node, at the given positions and their fixed sizes. */
export function toRfNodes(
  buyer: Buyer | null,
  nodes: readonly NodeView[],
  positions: Map<string, XY>,
  hotspots: Set<string>,
  selectedId: string | null,
): GraphRfNode[] {
  const origin = { x: 0, y: 0 }
  const suppliers: SupplierRfNode[] = nodes.map((node) => ({
    id: node.id,
    type: 'supplier',
    position: positions.get(node.id) ?? origin,
    ...sized(NODE_SIZE.supplier),
    data: { node, hotspot: hotspots.has(node.id) },
    selected: node.id === selectedId,
  }))
  if (buyer === null) return suppliers
  const buyerNode: BuyerRfNode = {
    id: BUYER_NODE_ID,
    type: 'buyer',
    position: positions.get(BUYER_NODE_ID) ?? origin,
    ...sized(NODE_SIZE.buyer),
    data: { buyer },
    selectable: false,
  }
  return [buyerNode, ...suppliers]
}

/** One edge per supplier, into the buyer. */
export function toRfEdges(nodes: readonly NodeView[]): SupplierRfEdge[] {
  return nodes.map((node) => {
    const width = edgeWidth(node.carbonKg)
    return {
      id: `edge-${node.id}`,
      source: node.id,
      target: BUYER_NODE_ID,
      data: { width },
      style: { strokeWidth: width },
      selectable: false,
    }
  })
}

export interface GraphView {
  nodes: GraphRfNode[]
  edges: SupplierRfEdge[]
}

/**
 * A selector for `useGraphStore` that returns the same `GraphView` object until the nodes, the buyer or the selection
 * change, so React Flow does not rerender on unrelated updates. The layout reruns only when the set of node ids
 * changes, and the edges only when the nodes change; a selection rebuilds just the node array.
 */
export function createGraphViewSelector(): (state: GraphState) => GraphView {
  const layout = createLayoutMemo()
  let nodesIn: GraphState['nodes'] | null = null
  let buyerIn: Buyer | null = null
  let selectedIn: string | null = null
  let sorted: NodeView[] = []
  let hotspots = new Set<string>()
  let view: GraphView = { nodes: [], edges: [] }

  return (state) => {
    const nodesChanged = state.nodes !== nodesIn
    if (!nodesChanged && state.buyer === buyerIn && state.selectedNodeId === selectedIn) return view
    if (nodesChanged) {
      sorted = sortedNodes(state.nodes)
      hotspots = hotspotIds(sorted)
    }
    const positions = layout(sorted.map((n) => n.id))
    view = {
      nodes: toRfNodes(state.buyer, sorted, positions, hotspots, state.selectedNodeId),
      edges: nodesChanged ? toRfEdges(sorted) : view.edges,
    }
    nodesIn = state.nodes
    buyerIn = state.buyer
    selectedIn = state.selectedNodeId
    return view
  }
}

export const selectGraphView = createGraphViewSelector()

/** The selected domain node, or null. */
export const selectSelectedNode = (state: GraphState): NodeView | null =>
  state.selectedNodeId === null ? null : (state.nodes[state.selectedNodeId] ?? null)
