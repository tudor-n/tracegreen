import type { Client } from 'openapi-fetch'
import { api } from '@/api/client'
import type { paths } from '@/api/schema'
import { reply } from './api-reply'
import { failureFor, type VerifyApi } from './verify-state'

// The supplier page's two requests through the generated client, each answered as a Reply that the page's state
// machine reads. A request that hangs counts as no connection, so the supplier gets a retry instead of an endless wait.

/** How long a supplier-facing page waits for an answer before it offers a retry. */
export const VERIFY_TIMEOUT_MS = 10_000

export function createVerifyApi(client: Client<paths>, timeoutMs = VERIFY_TIMEOUT_MS): VerifyApi {
  return {
    context: (token) =>
      reply(
        () => client.GET('/api/verify/context', { params: { query: { t: token } }, signal: AbortSignal.timeout(timeoutMs) }),
        failureFor,
      ),
    submit: (body) => reply(() => client.POST('/api/verify', { body, signal: AbortSignal.timeout(timeoutMs) }), failureFor),
  }
}

export const verifyApi = createVerifyApi(api)
