import { describe, expect, it } from 'vitest'
import { createGraphStore } from './graphStore'
import { BUYER_NODE_ID, computeLayout, createLayoutMemo, layoutKey, NODE_SIZE } from './layout'
import {
  createGraphViewSelector,
  EDGE_WIDTH,
  edgeWidth,
  hotspotIds,
  type SupplierRfNode,
  selectSelectedNode,
  sortedNodes,
  toRfEdges,
  toRfNodes,
} from './selectors'
import { node, nodeEvent, snapshot } from './testFixtures'

describe('hotspotIds', () => {
  const nodes = [node('a', 300), node('b', 48_000), node('c', 900), node('d', 48_000)]

  it('picks the largest emitter by carbonKg, ties broken by id', () => {
    expect([...hotspotIds(nodes)]).toEqual(['b'])
    expect([...hotspotIds(nodes, 3)]).toEqual(['b', 'd', 'c'])
  })

  it('never marks a node without carbon, and handles empty input', () => {
    expect(hotspotIds([node('a', 0)]).size).toBe(0)
    expect(hotspotIds([]).size).toBe(0)
    expect(hotspotIds(nodes, 0).size).toBe(0)
  })

  it('is an overlay: the node status is left alone', () => {
    const store = createGraphStore()
    store.getState().hydrate(snapshot(1, [node('a', 10), node('b', 5000)]))
    const view = createGraphViewSelector()(store.getState())
    const b = view.nodes.find((n) => n.id === 'b') as SupplierRfNode
    expect(b.data.hotspot).toBe(true)
    expect(b.data.node.status).toBe('ESTIMATED')
  })
})

describe('edgeWidth', () => {
  it('grows with the log of the kilograms', () => {
    expect(edgeWidth(1000)).toBeCloseTo(Math.log(1000) * EDGE_WIDTH.perLogKg)
    expect(edgeWidth(1000)).toBeLessThan(edgeWidth(10_000))
  })

  it('clamps to the minimum and maximum, also for zero, negative and non-finite input', () => {
    expect(edgeWidth(0)).toBe(EDGE_WIDTH.min)
    expect(edgeWidth(-5)).toBe(EDGE_WIDTH.min)
    expect(edgeWidth(1)).toBe(EDGE_WIDTH.min)
    expect(edgeWidth(Number.NaN)).toBe(EDGE_WIDTH.min)
    expect(edgeWidth(1e12)).toBe(EDGE_WIDTH.max)
    expect(edgeWidth(Number.POSITIVE_INFINITY)).toBe(EDGE_WIDTH.max)
  })
})

describe('toRfNodes and toRfEdges', () => {
  const buyer = snapshot(0).buyer
  const nodes = sortedNodes({ b: node('b', 20), a: node('a', 10) })
  const positions = computeLayout(nodes.map((n) => n.id))

  it('gives the buyer first, then the suppliers at their layout positions', () => {
    const rf = toRfNodes(buyer, nodes, positions, new Set(['b']), 'a')
    expect(rf.map((n) => [n.id, n.type])).toEqual([
      [BUYER_NODE_ID, 'buyer'],
      ['a', 'supplier'],
      ['b', 'supplier'],
    ])
    expect(rf[1].position).toEqual(positions.get('a'))
    expect(rf.find((n) => n.id === 'a')?.selected).toBe(true)
    expect(rf.find((n) => n.id === 'b')?.selected).toBe(false)
  })

  it('leaves the buyer out until a snapshot names one', () => {
    expect(toRfNodes(null, nodes, positions, new Set(), null).map((n) => n.id)).toEqual(['a', 'b'])
  })

  it('draws one edge per supplier into the buyer, with its width', () => {
    const edges = toRfEdges(nodes)
    expect(edges.map((e) => [e.source, e.target])).toEqual([
      ['a', BUYER_NODE_ID],
      ['b', BUYER_NODE_ID],
    ])
    expect(edges[1].data?.width).toBe(edgeWidth(20))
    expect(edges[1].style?.strokeWidth).toBe(edgeWidth(20))
  })
})

describe('layout', () => {
  it('puts the suppliers in a column left of the buyer, without overlap', () => {
    const positions = computeLayout(['a', 'b', 'c'])
    const at = (id: string) => positions.get(id) ?? expect.unreachable(`no position for ${id}`)
    const suppliers = ['a', 'b', 'c'].map(at)
    for (const p of suppliers) expect(p.x + NODE_SIZE.supplier.width).toBeLessThan(at(BUYER_NODE_ID).x)
    const ys = suppliers.map((p) => p.y).sort((x, y) => x - y)
    expect(ys[1] - ys[0]).toBeGreaterThanOrEqual(NODE_SIZE.supplier.height)
    expect(ys[2] - ys[1]).toBeGreaterThanOrEqual(NODE_SIZE.supplier.height)
  })

  it('depends on the set of ids, not their order', () => {
    expect(layoutKey(['b', 'a'])).toBe(layoutKey(['a', 'b']))
    expect(computeLayout(['b', 'a'])).toEqual(computeLayout(['a', 'b']))
  })

  it('recomputes only when the set changes', () => {
    const layout = createLayoutMemo()
    const first = layout(['a', 'b'])
    expect(layout(['b', 'a'])).toBe(first)
    expect(layout(['a', 'b', 'c'])).not.toBe(first)
  })
})

describe('graph view selector', () => {
  const setup = () => {
    const store = createGraphStore()
    store.getState().hydrate(snapshot(1, [node('a', 10), node('b', 20)]))
    return { store, select: createGraphViewSelector() }
  }

  it('returns the same object while nothing it reads has changed', () => {
    const { store, select } = setup()
    const first = select(store.getState())
    store.getState().setPanel('audit', true)
    expect(select(store.getState())).toBe(first)
  })

  it('on selection keeps the edges and the positions, and rebuilds only the node array', () => {
    const { store, select } = setup()
    const first = select(store.getState())
    store.getState().select('a')
    const next = select(store.getState())
    expect(next).not.toBe(first)
    expect(next.edges).toBe(first.edges)
    expect(next.nodes.map((n) => n.position)).toEqual(first.nodes.map((n) => n.position))
    expect(next.nodes.find((n) => n.id === 'a')?.selected).toBe(true)
  })

  it('keeps positions when a node changes but the set of nodes does not', () => {
    const { store, select } = setup()
    const first = select(store.getState())
    store.getState().applyEvent(nodeEvent(2, node('a', 50_000, { status: 'VERIFIED' }), 'node.updated'))
    const next = select(store.getState())
    expect(next.edges).not.toBe(first.edges)
    expect(next.nodes.map((n) => n.position)).toEqual(first.nodes.map((n) => n.position))
    expect((next.nodes.find((n) => n.id === 'a') as SupplierRfNode).data.hotspot).toBe(true)
  })

  it('lays out again when a node arrives', () => {
    const { store, select } = setup()
    const first = select(store.getState())
    store.getState().applyEvent(nodeEvent(2, node('c', 30)))
    const next = select(store.getState())
    expect(next.nodes.map((n) => n.id)).toEqual([BUYER_NODE_ID, 'a', 'b', 'c'])
    expect(next.nodes.find((n) => n.id === 'a')?.position).not.toEqual(first.nodes.find((n) => n.id === 'a')?.position)
  })
})

describe('selectSelectedNode', () => {
  it('returns the domain node, or null', () => {
    const store = createGraphStore()
    store.getState().hydrate(snapshot(1, [node('a', 10)]))
    expect(selectSelectedNode(store.getState())).toBeNull()
    store.getState().select('a')
    expect(selectSelectedNode(store.getState())?.id).toBe('a')
  })
})
