import { useState } from 'react'
import type { NodeView } from '@/api/client'
import type { Result } from '@/lib/api-reply'
import { trustApi } from '@/lib/trust-api'
import { refetchAfterTrust, type TrustFailure, trustFailureMessage, trustStamp } from '@/lib/trust-display'
import { graphStore } from '@/store'

// One trust write from the side panel (F7): send it, then wait until the store's node has moved past the stamp it had,
// since the API publishes the node with node.updated before or after the request answers. A refused write after a stale
// page refreshes the store from a snapshot. The store never changes here except through that snapshot.

export type TrustWritePhase =
  | { kind: 'idle' }
  | { kind: 'sending' }
  | { kind: 'waiting'; stamp: string }
  | { kind: 'saved' }
  | { kind: 'failed'; message: string }

export function useTrustWrite(node: NodeView) {
  const stamp = trustStamp(node)
  const [phase, setPhase] = useState<TrustWritePhase>({ kind: 'idle' })
  if (phase.kind === 'waiting' && phase.stamp !== stamp) setPhase({ kind: 'saved' })

  const run = async (send: () => Promise<Result<NodeView, TrustFailure>>) => {
    setPhase({ kind: 'sending' })
    const answer = await send()
    if (answer.ok) {
      // The answer is the node as the API now holds it; if it equals the store's, nothing will move, so it is saved.
      setPhase(trustStamp(answer.data) === stamp ? { kind: 'saved' } : { kind: 'waiting', stamp })
      return
    }
    if (refetchAfterTrust(answer.failure)) {
      const snapshot = await trustApi.snapshot()
      if (snapshot !== null) graphStore.getState().hydrate(snapshot)
    }
    setPhase({ kind: 'failed', message: trustFailureMessage(answer.failure) })
  }

  return { phase, busy: phase.kind === 'sending' || phase.kind === 'waiting', run }
}
