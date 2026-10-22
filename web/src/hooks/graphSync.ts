import type { DomainEvent, DomainEventType, GraphSnapshot } from '@/api/client'

// Pure decision logic behind useGraphEvents: which snapshot to fetch, which event to apply, when to poll or reconnect.
// The hook only performs the effects this returns, so every rule here is unit-tested without a browser.

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
  /** Sequence number the store is current to; null until the first snapshot. */
  lastSeq: number | null
  /** A snapshot request is in flight; events wait in `buffer` until it lands. */
  fetching: boolean
  buffer: DomainEvent[]
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

function fetchSnapshot(state: SyncState): SyncStep {
  return state.fetching ? none(state) : { state: { ...state, fetching: true }, effects: [{ kind: 'fetchSnapshot' }] }
}

export function step(state: SyncState, input: SyncInput): SyncStep {
  switch (input.kind) {
    case 'hello':
      // Every (re)connection refreshes the snapshot: it is cheap and covers whatever happened while the stream was down.
      return fetchSnapshot({ ...state, stream: 'open' })
    case 'resync':
      return fetchSnapshot(state)
    case 'streamError':
      // CONNECTING: the browser retries by itself. CLOSED (e.g. a 502 from the proxy): only a new EventSource recovers.
      return { state: { ...state, stream: 'down' }, effects: input.closed ? [{ kind: 'reconnect' }] : [] }
    case 'pollTick':
      return shouldPoll(state) ? fetchSnapshot(state) : none(state)
    case 'snapshotFailed':
      return none({ ...state, fetching: false, lastFetchOk: false })
    case 'snapshotLoaded':
      return loaded(state, input.snapshot)
    case 'event':
      return received(state, input.event)
  }
}

function received(state: SyncState, event: DomainEvent): SyncStep {
  if (state.fetching) return none({ ...state, buffer: [...state.buffer, event] })
  if (state.lastSeq === null) return fetchSnapshot({ ...state, buffer: [event] })
  if (event.seq <= state.lastSeq) return none(state)
  // A reset carries no nodes, and a gap means something was missed: both are answered with a fresh snapshot.
  if (event.type === 'graph.reset' || event.seq > state.lastSeq + 1) {
    return fetchSnapshot({ ...state, buffer: [event] })
  }
  return { state: { ...state, lastSeq: event.seq }, effects: [{ kind: 'apply', event }] }
}

function loaded(state: SyncState, snapshot: GraphSnapshot): SyncStep {
  const effects: SyncEffect[] = [{ kind: 'hydrate', snapshot }]
  const base: SyncState = { ...state, fetching: false, lastFetchOk: true, buffer: [] }
  let lastSeq = snapshot.lastSeq
  const pending = state.buffer.filter((e) => e.seq > lastSeq).sort((a, b) => a.seq - b.seq)
  for (const [i, event] of pending.entries()) {
    if (event.type === 'graph.reset' || event.seq > lastSeq + 1) {
      // The graph moved on while the snapshot was in flight: fetch again and keep what arrived after this point.
      const again = fetchSnapshot({ ...base, lastSeq, buffer: pending.slice(i) })
      return { state: again.state, effects: [...effects, ...again.effects] }
    }
    effects.push({ kind: 'apply', event })
    lastSeq = event.seq
  }
  return { state: { ...base, lastSeq }, effects }
}
