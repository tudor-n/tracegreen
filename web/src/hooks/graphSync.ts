import type { DomainEvent, DomainEventType, GraphSnapshot } from '@/api/client'

// Pure decision logic behind useGraphEvents: which snapshot to fetch, which event to apply, when to poll or reconnect.
// startGraphSync only performs the effects this returns, so every rule here is unit-tested without a browser.
//
// One fact carries most of it: a snapshot requested after an event arrived already contains that event (the server
// reads its sequence before the nodes). So every new request drops the buffer, and only events that arrive while a
// request is in flight are kept for after it lands.

/** What the connection badge shows. `connecting` lasts only until the first snapshot arrives or fails. */
export type Connection = 'connecting' | 'live' | 'polling' | 'offline'

// Keyed by every generated event type, so a new event in the contract fails the typecheck until it is listed here.
const EVENT_TYPES: Record<DomainEventType, true> = {
  'node.created': true,
  'node.updated': true,
  'node.verified': true,
  'audit.appended': true,
  'scenario.saved': true,
  'graph.reset': true,
}

/** SSE event names that carry a DomainEvent. `hello`, `resync` and `ping` are control messages. */
export const DOMAIN_EVENT_TYPES = Object.keys(EVENT_TYPES) as DomainEventType[]

export type SyncInput =
  | { kind: 'hello' }
  | { kind: 'event'; event: DomainEvent }
  | { kind: 'resync' }
  | { kind: 'streamError'; closed: boolean }
  | { kind: 'pollTick' }
  | { kind: 'snapshotLoaded'; snapshot: GraphSnapshot }
  | { kind: 'snapshotFailed' }

export type SyncEffect =
  | { kind: 'fetchSnapshot' }
  | { kind: 'hydrate'; snapshot: GraphSnapshot }
  | { kind: 'apply'; event: DomainEvent }
  | { kind: 'reconnect' }

export interface SyncState {
  /** Open after `hello`; down after an error until the next `hello`. */
  stream: 'connecting' | 'open' | 'down'
  /** Sequence number the store is current to; null until the first snapshot. Goes down when the API restarts. */
  lastSeq: number | null
  /** A snapshot request is in flight; events wait in `buffer` until it lands. */
  fetching: boolean
  buffer: DomainEvent[]
  /** A (re)connection or resync arrived while a request was in flight, so that answer may be too old: fetch once more. */
  refetch: boolean
  /** Whether the most recent snapshot request succeeded. */
  lastFetchOk: boolean
}

export interface SyncStep {
  state: SyncState
  effects: SyncEffect[]
}

export const initialSyncState: SyncState = {
  stream: 'connecting',
  lastSeq: null,
  fetching: false,
  buffer: [],
  refetch: false,
  lastFetchOk: true,
}

/** Poll while the stream is not open, and keep retrying while snapshots fail. */
export function shouldPoll(state: SyncState): boolean {
  return state.stream !== 'open' || !state.lastFetchOk
}

export function connection(state: SyncState): Connection {
  if (state.lastSeq === null) return state.lastFetchOk ? 'connecting' : 'offline'
  if (!state.lastFetchOk) return 'offline'
  return state.stream === 'open' ? 'live' : 'polling'
}

const none = (state: SyncState): SyncStep => ({ state, effects: [] })

/**
 * Starts a snapshot request. The buffer is dropped because the new snapshot covers it. If one is already in flight,
 * `queue` asks for another once it lands; polls don't queue, since the next tick asks again anyway.
 */
function fetchSnapshot(state: SyncState, queue = false): SyncStep {
  if (state.fetching) return queue ? none({ ...state, refetch: true }) : none(state)
  return { state: { ...state, fetching: true, refetch: false, buffer: [] }, effects: [{ kind: 'fetchSnapshot' }] }
}

export function step(state: SyncState, input: SyncInput): SyncStep {
  switch (input.kind) {
    case 'hello':
      // Every (re)connection refreshes the snapshot: a new EventSource gets no replay, so this covers what it missed.
      return fetchSnapshot({ ...state, stream: 'open' }, true)
    case 'resync':
      return fetchSnapshot(state, true)
    case 'streamError':
      return streamError(state, input.closed)
    case 'pollTick':
      return shouldPoll(state) ? fetchSnapshot(state) : none(state)
    case 'snapshotFailed':
      return none({ ...state, fetching: false, refetch: false, buffer: [], lastFetchOk: false })
    case 'snapshotLoaded':
      return loaded(state, input.snapshot)
    case 'event':
      return received(state, input.event)
  }
}

function streamError(state: SyncState, closed: boolean): SyncStep {
  const down: SyncState = { ...state, stream: 'down' }
  // On the first error, ask the API right away instead of waiting for a poll tick: the badge settles within a request.
  const checked = state.stream === 'down' ? none(down) : fetchSnapshot(down)
  // CONNECTING: the browser retries by itself. CLOSED (e.g. a 502 from the proxy): only a new EventSource recovers.
  return closed ? { state: checked.state, effects: [...checked.effects, { kind: 'reconnect' }] } : checked
}

function received(state: SyncState, event: DomainEvent): SyncStep {
  if (state.fetching) return none({ ...state, buffer: [...state.buffer, event] })
  if (state.lastSeq === null) return fetchSnapshot(state)
  if (event.seq <= state.lastSeq) return none(state)
  // A reset carries no nodes, and a gap means something was missed: both are answered with a fresh snapshot.
  if (event.type === 'graph.reset' || event.seq > state.lastSeq + 1) return fetchSnapshot(state)
  return { state: { ...state, lastSeq: event.seq }, effects: [{ kind: 'apply', event }] }
}

function loaded(state: SyncState, snapshot: GraphSnapshot): SyncStep {
  const effects: SyncEffect[] = [{ kind: 'hydrate', snapshot }]
  // The snapshot's sequence wins even when it is lower than ours: that only happens after an API restart.
  let lastSeq = snapshot.lastSeq
  const base: SyncState = { ...state, fetching: false, refetch: false, buffer: [], lastFetchOk: true }
  const pending = state.buffer.filter((e) => e.seq > lastSeq).sort((a, b) => a.seq - b.seq)
  for (const event of pending) {
    if (event.type === 'graph.reset' || event.seq > lastSeq + 1) {
      // The graph moved on while the snapshot was in flight: fetch again; the new answer covers the rest of the buffer.
      const again = fetchSnapshot({ ...base, lastSeq })
      return { state: again.state, effects: [...effects, ...again.effects] }
    }
    effects.push({ kind: 'apply', event })
    lastSeq = event.seq
  }
  const settled: SyncState = { ...base, lastSeq }
  if (!state.refetch) return { state: settled, effects }
  const again = fetchSnapshot(settled)
  return { state: again.state, effects: [...effects, ...again.effects] }
}
