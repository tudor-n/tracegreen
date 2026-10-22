import { useEffect, useRef, useState } from 'react'
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

/** The store seam (docs/plan/README.md §3): the store ignores events whose `seq` is not newer than its own. */
export interface GraphStoreActions {
  hydrate(snapshot: GraphSnapshot): void
  applyEvent(event: DomainEvent): void
}

export interface GraphEventsOptions {
  url?: string
  pollMs?: number
  reconnectMs?: number
}

/**
 * Keeps the store in sync with the API: snapshot on every stream (re)open and on `resync` or `graph.reset`, events in
 * between, polling `/api/graph` every 5 s while the stream is down. Returns the state for the connection badge.
 * The decisions live in graphSync.ts; this hook only performs them.
 */
export function useGraphEvents(store: GraphStoreActions, options: GraphEventsOptions = {}): Connection {
  const { url = '/api/events', pollMs = 5000, reconnectMs = 2000 } = options
  const [status, setStatus] = useState<Connection>('connecting')
  const storeRef = useRef(store)

  useEffect(() => {
    storeRef.current = store
  }, [store])

  useEffect(() => {
    let state = initialSyncState
    let source: EventSource | null = null
    let pollTimer: ReturnType<typeof setInterval> | null = null
    let reconnectTimer: ReturnType<typeof setTimeout> | null = null
    let disposed = false
    const aborts = new AbortController()

    const dispatch = (input: SyncInput) => {
      if (disposed) return
      const next = step(state, input)
      state = next.state
      for (const effect of next.effects) run(effect)
      setStatus(connection(state))
      syncPolling()
    }

    const run = (effect: SyncEffect) => {
      switch (effect.kind) {
        case 'fetchSnapshot':
          void loadSnapshot()
          break
        case 'hydrate':
          storeRef.current.hydrate(effect.snapshot)
          break
        case 'apply':
          storeRef.current.applyEvent(effect.event)
          break
        case 'reconnect':
          scheduleReconnect()
          break
      }
    }

    const loadSnapshot = async () => {
      try {
        const { data } = await api.GET('/api/graph', { signal: aborts.signal })
        dispatch(data ? { kind: 'snapshotLoaded', snapshot: data } : { kind: 'snapshotFailed' })
      } catch {
        dispatch({ kind: 'snapshotFailed' })
      }
    }

    const syncPolling = () => {
      const want = shouldPoll(state)
      if (want && pollTimer === null) {
        pollTimer = setInterval(() => dispatch({ kind: 'pollTick' }), pollMs)
      } else if (!want && pollTimer !== null) {
        clearInterval(pollTimer)
        pollTimer = null
      }
    }

    const open = () => {
      const stream = new EventSource(url)
      source = stream
      stream.addEventListener('hello', () => dispatch({ kind: 'hello' }))
      stream.addEventListener('resync', () => dispatch({ kind: 'resync' }))
      for (const type of DOMAIN_EVENT_TYPES) {
        stream.addEventListener(type, (message: MessageEvent<string>) => {
          dispatch({ kind: 'event', event: JSON.parse(message.data) as DomainEvent })
        })
      }
      stream.onerror = () => dispatch({ kind: 'streamError', closed: stream.readyState === EventSource.CLOSED })
    }

    const scheduleReconnect = () => {
      source?.close()
      if (reconnectTimer !== null) return
      reconnectTimer = setTimeout(() => {
        reconnectTimer = null
        if (!disposed) open()
      }, reconnectMs)
    }

    open()
    syncPolling()
    return () => {
      disposed = true
      aborts.abort()
      source?.close()
      if (pollTimer !== null) clearInterval(pollTimer)
      if (reconnectTimer !== null) clearTimeout(reconnectTimer)
    }
  }, [url, pollMs, reconnectMs])

  return status
}
