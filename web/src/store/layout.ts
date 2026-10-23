import dagre from '@dagrejs/dagre'

export interface XY {
  x: number
  y: number
}

/** The id React Flow uses for the buyer node; supplier nodes use their domain id. */
export const BUYER_NODE_ID = 'buyer'

/** Box sizes the layout reserves; the node components (B2) are drawn to fit them. */
export const NODE_SIZE = {
  supplier: { width: 220, height: 88 },
  buyer: { width: 240, height: 96 },
} as const

/**
 * Top-left positions for the buyer and every supplier: suppliers in one column on the left, edges flowing right into
 * the buyer. Depends only on the set of supplier ids (every supplier has the same size and one edge to the buyer), so
 * the same set always gives the same picture.
 */
export function computeLayout(supplierIds: readonly string[]): Map<string, XY> {
  const g = new dagre.graphlib.Graph()
  g.setGraph({ rankdir: 'LR', nodesep: 24, ranksep: 160, marginx: 16, marginy: 16 })
  g.setDefaultEdgeLabel(() => ({}))
  g.setNode(BUYER_NODE_ID, { ...NODE_SIZE.buyer })
  for (const id of [...supplierIds].sort()) {
    g.setNode(id, { ...NODE_SIZE.supplier })
    g.setEdge(id, BUYER_NODE_ID)
  }
  dagre.layout(g)

  const positions = new Map<string, XY>()
  for (const id of g.nodes()) {
    const { x, y, width, height } = g.node(id)
    positions.set(id, { x: x - width / 2, y: y - height / 2 })
  }
  return positions
}

/** Same set of ids, in any order → same key. */
export function layoutKey(supplierIds: readonly string[]): string {
  return [...supplierIds].sort().join('|')
}

/**
 * `computeLayout` that reruns only when the set of supplier ids changes: a status flip, a new KPI or a selection
 * returns the previous positions object.
 */
export function createLayoutMemo(): (supplierIds: readonly string[]) => Map<string, XY> {
  let key: string | null = null
  let positions = new Map<string, XY>()
  return (supplierIds) => {
    const next = layoutKey(supplierIds)
    if (next !== key) {
      key = next
      positions = computeLayout(supplierIds)
    }
    return positions
  }
}
