import type { Client } from 'openapi-fetch'
import { api, type ClaimDecision, type ClaimKind, type GraphSnapshot, type NodeView, type ReceiptRequest, type TargetRequest } from '@/api/client'
import type { paths } from '@/api/schema'
import { reply, type Result } from './api-reply'
import { type TrustFailure, trustFailureFor } from './trust-display'

// The trust model's requests (F7). None touches the store: the API publishes the node with `node.updated` and the sync
// applies it, so no figure is shown before the server computed it. After a 409 or 404 the caller hydrates the store
// from `snapshot` instead of waiting for the stream.

export interface TrustApi {
  /** POST /api/nodes/{nodeId}/receipt: the buyer's goods-receipt date and customs record; null fields clear them. */
  recordReceipt(nodeId: string, body: ReceiptRequest): Promise<Result<NodeView, TrustFailure>>
  /** POST /api/nodes/{nodeId}/claims/{kind} with the value the page showed, so a stale page is refused (409). */
  reviewClaim(nodeId: string, kind: ClaimKind, decision: ClaimDecision, expectedValue: string): Promise<Result<NodeView, TrustFailure>>
  /** PUT /api/nodes/{nodeId}/target; every field null goes back to the default. */
  setTarget(nodeId: string, body: TargetRequest): Promise<Result<NodeView, TrustFailure>>
  /** GET /api/graph, for a refresh after a refused write; null when it fails (the sync keeps trying anyway). */
  snapshot(): Promise<GraphSnapshot | null>
}

export function createTrustApi(client: Client<paths>): TrustApi {
  return {
    recordReceipt: (nodeId, body) =>
      reply(() => client.POST('/api/nodes/{nodeId}/receipt', { params: { path: { nodeId } }, body }), trustFailureFor),
    reviewClaim: (nodeId, kind, decision, expectedValue) =>
      reply(
        () => client.POST('/api/nodes/{nodeId}/claims/{kind}', { params: { path: { nodeId, kind } }, body: { decision, expectedValue } }),
        trustFailureFor,
      ),
    setTarget: (nodeId, body) =>
      reply(() => client.PUT('/api/nodes/{nodeId}/target', { params: { path: { nodeId } }, body }), trustFailureFor),
    snapshot: async () => {
      const answer = await reply(() => client.GET('/api/graph'), () => null)
      return answer.ok ? answer.data : null
    },
  }
}

export const trustApi = createTrustApi(api)
