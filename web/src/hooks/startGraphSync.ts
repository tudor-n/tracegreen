import { api, type DomainEvent, type GraphSnapshot } from '@/api/client'
import {
  type Connection,
  connection,
  DOMAIN_EVENT_TYPES,
  initialSyncState,
  type SyncEffect,
  type SyncInput,
  shouldPoll,
  step,
} from './graphSync'

/**
 * The store seam (docs/plan/README.md §3). `hydrate` replaces everything, `lastSeq` included, even when the new
 * `lastSeq` is lower: the sequence restarts at 0 when the API restarts. `applyEvent` ignores a `seq` that is not newer
 * than its own. It never receives `graph.reset`: the sync refetches the snapshot instead.
 */
export interface GraphStoreActions {
  hydrate(snapshot: GraphSnapshot): void
  applyEvent(event: DomainEvent): void
}

/** The part of EventSource the sync uses, so tests can drive it without a browser. */
export interface EventStream {
  readonly readyState: number
  onerror: ((event: Event) => void) | null
  addEventListener(type: string, listener: (event: MessageEvent<string>) => void): void
  close(): void
}

export interface GraphSyncOptions {
  url?: string
  pollMs?: number
  reconnectMs?: number
  /** A snapshot request that hangs longer than this counts as failed, so it cannot hold events back forever. */
  snapshotTimeoutMs?: number
  onStatus?: (status: Connection) => void
  openStream?: (url: string) => EventStream
  loadSnapshot?: (signal: AbortSignal) => Promise<GraphSnapshot | undefined>
}

const CLOSED = 2 // EventSource.CLOSED, spelled out so this module also loads where EventSource does not exist

const loadFromApi = async (signal: AbortSignal) => (await api.GET('/api/graph', { signal })).data

/**
 * Keeps the store in sync with the API: snapshot on every stream (re)open and on `resync`, `graph.reset` or a gap,
 * events in between, polling `/api/graph` while the stream is down, a new EventSource when the browser gives up.
 * The decisions live in graphSync.ts; this only performs them. Returns the function that stops everything.
 */
export function startGraphSync(store: GraphStoreActions, options: GraphSyncOptions = {}): () => void {
  const {
    url = '/api/events',
    pollMs = 5000,
    reconnectMs = 2000,
    snapshotTimeoutMs = 8000,
    onStatus = () => {},
    openStream = (streamUrl: string) => new EventSource(streamUrl),
    loadSnapshot = loadFromApi,
  } = options

  let state = initialSyncState
  let source: EventStream | null = null
  let pollTimer: ReturnType<typeof setInterval> | null = null
  let reconnectTimer: ReturnType<typeof setTimeout> | null = null
  let stopped = false
  const aborts = new AbortController()

  const dispatch = (input: SyncInput) => {
    if (stopped) return
    const next = step(state, input)
    state = next.state
    for (const effect of next.effects) run(effect)
    onStatus(connection(state))
    syncPolling()
  }

  const run = (effect: SyncEffect) => {
    switch (effect.kind) {
      case 'fetchSnapshot':
        void fetchSnapshot()
        break
      case 'hydrate':
        store.hydrate(effect.snapshot)
        break
      case 'apply':
        store.applyEvent(effect.event)
        break
      case 'reconnect':
        scheduleReconnect()
        break
    }
  }

  const fetchSnapshot = async () => {
    try {
      const snapshot = await loadSnapshot(AbortSignal.any([aborts.signal, AbortSignal.timeout(snapshotTimeoutMs)]))
      dispatch(snapshot ? { kind: 'snapshotLoaded', snapshot } : { kind: 'snapshotFailed' })
    } catch {
      dispatch({ kind: 'snapshotFailed' })
    }
  }

  const syncPolling = () => {
    const want = !stopped && shouldPoll(state)
    if (want && pollTimer === null) {
      pollTimer = setInterval(() => dispatch({ kind: 'pollTick' }), pollMs)
    } else if (!want && pollTimer !== null) {
      clearInterval(pollTimer)
      pollTimer = null
    }
  }

  const open = () => {
    const stream = openStream(url)
    source = stream
    stream.addEventListener('hello', () => dispatch({ kind: 'hello' }))
    stream.addEventListener('resync', () => dispatch({ kind: 'resync' }))
    for (const type of DOMAIN_EVENT_TYPES) {
      stream.addEventListener(type, (message) => {
        dispatch({ kind: 'event', event: JSON.parse(message.data) as DomainEvent })
      })
    }
    stream.onerror = () => dispatch({ kind: 'streamError', closed: stream.readyState === CLOSED })
  }

  const scheduleReconnect = () => {
    source?.close()
    if (reconnectTimer !== null) return
    reconnectTimer = setTimeout(() => {
      reconnectTimer = null
      if (!stopped) open()
    }, reconnectMs)
  }

  onStatus(connection(state))
  open()
  syncPolling()

  return () => {
    stopped = true
    aborts.abort()
    source?.close()
    if (pollTimer !== null) clearInterval(pollTimer)
    if (reconnectTimer !== null) clearTimeout(reconnectTimer)
  }
}
