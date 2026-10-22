import { useEffect, useRef, useState } from 'react'
import type { Connection } from './graphSync'
import { type GraphStoreActions, startGraphSync } from './startGraphSync'

export type { Connection } from './graphSync'
export type { GraphStoreActions } from './startGraphSync'

export interface GraphEventsOptions {
  url?: string
  pollMs?: number
  reconnectMs?: number
}

/**
 * Keeps the store in sync with the API for as long as the component is mounted (see startGraphSync) and returns the
 * state for the connection badge: `connecting`, `live`, `polling` or `offline`.
 */
export function useGraphEvents(store: GraphStoreActions, options: GraphEventsOptions = {}): Connection {
  const { url = '/api/events', pollMs = 5000, reconnectMs = 2000 } = options
  const [status, setStatus] = useState<Connection>('connecting')
  const storeRef = useRef(store)

  useEffect(() => {
    storeRef.current = store
  }, [store])

  useEffect(
    () =>
      startGraphSync(
        {
          hydrate: (snapshot) => storeRef.current.hydrate(snapshot),
          applyEvent: (event) => storeRef.current.applyEvent(event),
        },
        { url, pollMs, reconnectMs, onStatus: setStatus },
      ),
    [url, pollMs, reconnectMs],
  )

  return status
}
