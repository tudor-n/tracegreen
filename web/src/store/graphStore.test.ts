import { describe, expect, it, vi } from 'vitest'
import { createGraphStore, selectLoaded } from './graphStore'
import { audit, auditEvent, kpis, node, nodeEvent, resetEvent, snapshot } from './testFixtures'

const ids = (store: ReturnType<typeof createGraphStore>) => Object.keys(store.getState().nodes).sort()

describe('hydrate', () => {
  it('starts empty and not loaded', () => {
    const store = createGraphStore()
    expect(selectLoaded(store.getState())).toBe(false)
    expect(store.getState().nodes).toEqual({})
  })

  it('replaces everything with the snapshot', () => {
    const store = createGraphStore()
    store.getState().hydrate(snapshot(3, [node('a', 10), node('b', 20)], [audit(1)]))
    store.getState().hydrate(snapshot(7, [node('c', 30)], [audit(1), audit(2)]))

    const s = store.getState()
    expect(ids(store)).toEqual(['c'])
    expect(s.kpis?.totalKg).toBe(30)
    expect(s.audit.map((e) => e.id)).toEqual([1, 2])
    expect(s.lastSeq).toBe(7)
    expect(s.buyer?.name).toBe('Buyer SRL')
    expect(s.mode).toBe('memory')
    expect(selectLoaded(s)).toBe(true)
  })

  it('takes a lower lastSeq, because the sequence restarts with the API', () => {
    const store = createGraphStore()
    store.getState().hydrate(snapshot(40, [node('a', 10)]))
    store.getState().hydrate(snapshot(0, [node('a', 10)]))
    expect(store.getState().lastSeq).toBe(0)

    store.getState().applyEvent(nodeEvent(1, node('b', 5)))
    expect(ids(store)).toEqual(['a', 'b'])
  })

  it('keeps the selection when the node is still there and drops it after a reset', () => {
    const store = createGraphStore()
    store.getState().hydrate(snapshot(1, [node('a', 10), node('b', 20)]))
    store.getState().select('b')
    store.getState().setPanel('simulator', true)

    store.getState().hydrate(snapshot(2, [node('a', 10), node('b', 25)]))
    expect(store.getState().selectedNodeId).toBe('b')
    expect(store.getState().openPanels.drawer).toBe(true)

    store.getState().hydrate(snapshot(0, [node('a', 10)]))
    expect(store.getState().selectedNodeId).toBeNull()
    expect(store.getState().openPanels).toEqual({ drawer: false, audit: false, simulator: false })
  })
})

describe('applyEvent', () => {
  it('upserts nodes by id and takes the KPIs from the event', () => {
    const store = createGraphStore()
    store.getState().hydrate(snapshot(4, [node('a', 10)]))

    store.getState().applyEvent(nodeEvent(5, node('b', 20), 'node.created', kpis(30, 2)))
    store.getState().applyEvent(nodeEvent(6, node('a', 12, { status: 'VERIFIED' }), 'node.updated', kpis(32, 2)))

    const s = store.getState()
    expect(ids(store)).toEqual(['a', 'b'])
    expect(s.nodes.a.status).toBe('VERIFIED')
    expect(s.nodes.a.carbonKg).toBe(12)
    expect(s.kpis?.totalKg).toBe(32)
    expect(s.lastSeq).toBe(6)
  })

  it('ignores stale and repeated events without notifying subscribers', () => {
    const store = createGraphStore()
    store.getState().hydrate(snapshot(5, [node('a', 10)]))
    const before = store.getState()
    const listener = vi.fn()
    store.subscribe(listener)

    store.getState().applyEvent(nodeEvent(5, node('a', 99)))
    store.getState().applyEvent(nodeEvent(2, node('z', 1)))

    expect(store.getState()).toBe(before)
    expect(listener).not.toHaveBeenCalled()
  })

  it('applies an event that repeats what the snapshot already holds without duplicating it', () => {
    const store = createGraphStore()
    store.getState().hydrate(snapshot(5, [node('a', 10)], [audit(1)]))

    store.getState().applyEvent(nodeEvent(6, node('a', 10)))
    store.getState().applyEvent(auditEvent(7, audit(1)))

    expect(ids(store)).toEqual(['a'])
    expect(store.getState().audit.map((e) => e.id)).toEqual([1])
    expect(store.getState().lastSeq).toBe(7)
  })

  it('keeps the audit list ascending by id', () => {
    const store = createGraphStore()
    store.getState().hydrate(snapshot(1, [], [audit(1), audit(3)]))

    store.getState().applyEvent(auditEvent(2, audit(4)))
    store.getState().applyEvent(auditEvent(3, audit(2)))

    expect(store.getState().audit.map((e) => e.id)).toEqual([1, 2, 3, 4])
  })

  it('ignores graph.reset, which the sync turns into a snapshot refetch', () => {
    const store = createGraphStore()
    store.getState().hydrate(snapshot(1, [node('a', 10)]))
    const before = store.getState()

    store.getState().applyEvent(resetEvent(2))

    expect(store.getState()).toBe(before)
  })

  it('does not touch UI state', () => {
    const store = createGraphStore()
    store.getState().hydrate(snapshot(1, [node('a', 10)]))
    store.getState().select('a')

    store.getState().applyEvent(nodeEvent(2, node('a', 11)))

    expect(store.getState().selectedNodeId).toBe('a')
    expect(store.getState().openPanels.drawer).toBe(true)
  })
})

describe('select', () => {
  it('opens the drawer for a known node, ignores an unknown one and closes on null', () => {
    const store = createGraphStore()
    store.getState().hydrate(snapshot(1, [node('a', 10)]))

    store.getState().select('missing')
    expect(store.getState().selectedNodeId).toBeNull()

    store.getState().select('a')
    expect(store.getState().selectedNodeId).toBe('a')
    expect(store.getState().openPanels.drawer).toBe(true)

    store.getState().select(null)
    expect(store.getState().selectedNodeId).toBeNull()
    expect(store.getState().openPanels.drawer).toBe(false)
  })
})
