import { describe, expect, it } from 'vitest'
import type { DomainEvent, GraphSnapshot } from '@/api/client'
import {
  connection,
  DOMAIN_EVENT_TYPES,
  initialSyncState,
  shouldPoll,
  step,
  type SyncInput,
  type SyncState,
  type SyncStep,
} from './graphSync'

// The reducer reads only `lastSeq` from a snapshot and `seq` / `type` from an event.
const snapshot = (lastSeq: number) => ({ lastSeq }) as unknown as GraphSnapshot
const event = (seq: number, type: DomainEvent['type'] = 'node.created') => ({ seq, type }) as unknown as DomainEvent

function run(inputs: SyncInput[], from: SyncState = initialSyncState): SyncStep {
  let current: SyncStep = { state: from, effects: [] }
  for (const input of inputs) current = step(current.state, input)
  return current
}

const kinds = (s: SyncStep) => s.effects.map((e) => e.kind)
const applied = (s: SyncStep) => s.effects.flatMap((e) => (e.kind === 'apply' ? [e.event.seq] : []))

/** Stream open and store hydrated at `lastSeq`. */
const live = (lastSeq: number) => run([{ kind: 'hello' }, { kind: 'snapshotLoaded', snapshot: snapshot(lastSeq) }]).state

describe('first connection', () => {
  it('fetches the snapshot on hello and hydrates the store with it', () => {
    const hello = run([{ kind: 'hello' }])
    expect(kinds(hello)).toEqual(['fetchSnapshot'])
    expect(connection(hello.state)).toBe('connecting')

    const loaded = step(hello.state, { kind: 'snapshotLoaded', snapshot: snapshot(4) })
    expect(kinds(loaded)).toEqual(['hydrate'])
    expect(loaded.state.lastSeq).toBe(4)
    expect(connection(loaded.state)).toBe('live')
    expect(shouldPoll(loaded.state)).toBe(false)
  })

  it('shows offline when the first snapshot fails', () => {
    const failed = run([{ kind: 'hello' }, { kind: 'snapshotFailed' }])
    expect(connection(failed.state)).toBe('offline')
    expect(shouldPoll(failed.state)).toBe(true)
  })

  it('polls while the stream has not opened yet', () => {
    expect(shouldPoll(initialSyncState)).toBe(true)
    expect(kinds(run([{ kind: 'pollTick' }]))).toEqual(['fetchSnapshot'])
  })
})

describe('events', () => {
  it('applies the next event and drops stale or repeated ones', () => {
    const next = step(live(4), { kind: 'event', event: event(5) })
    expect(applied(next)).toEqual([5])
    expect(next.state.lastSeq).toBe(5)

    expect(kinds(step(next.state, { kind: 'event', event: event(5) }))).toEqual([])
    expect(kinds(step(next.state, { kind: 'event', event: event(3) }))).toEqual([])
  })

  it('refetches the snapshot when a sequence number is skipped', () => {
    const gap = step(live(4), { kind: 'event', event: event(7) })
    expect(kinds(gap)).toEqual(['fetchSnapshot'])
    expect(gap.state.lastSeq).toBe(4)
  })

  it('answers graph.reset with a fresh snapshot instead of forwarding it', () => {
    const reset = step(live(4), { kind: 'event', event: event(5, 'graph.reset') })
    expect(kinds(reset)).toEqual(['fetchSnapshot'])

    const reloaded = step(reset.state, { kind: 'snapshotLoaded', snapshot: snapshot(5) })
    expect(kinds(reloaded)).toEqual(['hydrate'])
    expect(reloaded.state.lastSeq).toBe(5)
  })

  it('buffers events while a snapshot is in flight and applies only the newer ones after hydrating', () => {
    const fetching = run([{ kind: 'hello' }, { kind: 'event', event: event(5) }, { kind: 'event', event: event(6) }])
    expect(kinds(fetching)).toEqual([])

    const loaded = step(fetching.state, { kind: 'snapshotLoaded', snapshot: snapshot(5) })
    expect(kinds(loaded)).toEqual(['hydrate', 'apply'])
    expect(applied(loaded)).toEqual([6])
    expect(loaded.state.lastSeq).toBe(6)
    expect(loaded.state.buffer).toEqual([])
  })

  it('fetches again when a reset arrived while the snapshot was in flight', () => {
    const fetching = run([{ kind: 'hello' }, { kind: 'event', event: event(6, 'graph.reset') }])

    const loaded = step(fetching.state, { kind: 'snapshotLoaded', snapshot: snapshot(5) })
    expect(kinds(loaded)).toEqual(['hydrate', 'fetchSnapshot'])
    expect(loaded.state.fetching).toBe(true)
  })

  it('fetches a snapshot for an event that arrives before any snapshot', () => {
    const early = run([{ kind: 'event', event: event(9) }])
    expect(kinds(early)).toEqual(['fetchSnapshot'])
  })
})

describe('reconnect and polling', () => {
  it('switches to polling when the stream drops and back to live when it reopens', () => {
    const down = step(live(4), { kind: 'streamError', closed: false })
    expect(kinds(down)).toEqual([])
    expect(shouldPoll(down.state)).toBe(true)
    expect(connection(down.state)).toBe('polling')

    const tick = step(down.state, { kind: 'pollTick' })
    expect(kinds(tick)).toEqual(['fetchSnapshot'])
    const polled = step(tick.state, { kind: 'snapshotLoaded', snapshot: snapshot(6) })
    expect(connection(polled.state)).toBe('polling')

    const reopened = run([{ kind: 'hello' }, { kind: 'snapshotLoaded', snapshot: snapshot(6) }], polled.state)
    expect(connection(reopened.state)).toBe('live')
    expect(shouldPoll(reopened.state)).toBe(false)
  })

  it('shows offline while polls fail and recovers on the next good snapshot', () => {
    const down = run([{ kind: 'streamError', closed: false }, { kind: 'pollTick' }, { kind: 'snapshotFailed' }], live(4))
    expect(connection(down.state)).toBe('offline')

    const back = run([{ kind: 'pollTick' }, { kind: 'snapshotLoaded', snapshot: snapshot(4) }], down.state)
    expect(connection(back.state)).toBe('polling')
  })

  it('asks for a new EventSource when the browser gave up on the stream', () => {
    expect(kinds(step(live(4), { kind: 'streamError', closed: true }))).toEqual(['reconnect'])
  })

  it('refetches the snapshot on resync and on every hello', () => {
    expect(kinds(step(live(4), { kind: 'resync' }))).toEqual(['fetchSnapshot'])
    expect(kinds(step(live(4), { kind: 'hello' }))).toEqual(['fetchSnapshot'])
  })

  it('never has two snapshot requests in flight', () => {
    const first = run([{ kind: 'streamError', closed: false }, { kind: 'pollTick' }], live(4))
    expect(kinds(first)).toEqual(['fetchSnapshot'])
    expect(kinds(step(first.state, { kind: 'pollTick' }))).toEqual([])
    expect(kinds(step(first.state, { kind: 'resync' }))).toEqual([])
  })

  it('does not poll while the stream is open and snapshots succeed', () => {
    expect(kinds(step(live(4), { kind: 'pollTick' }))).toEqual([])
  })
})

it('listens for every domain event type in the contract', () => {
  expect([...DOMAIN_EVENT_TYPES].sort()).toEqual([
    'audit.appended',
    'graph.reset',
    'node.created',
    'node.updated',
    'node.verified',
    'scenario.saved',
  ])
})
