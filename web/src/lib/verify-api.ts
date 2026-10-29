import type { Client } from 'openapi-fetch'
import { api, type ProblemCode } from '@/api/client'
import type { paths } from '@/api/schema'
import { failureFor, type Reply, type VerifyApi } from './verify-state'

// The supplier page's two requests through the generated client, each answered as a Reply that the page's state
// machine reads. A request that hangs counts as no connection, so the supplier gets a retry instead of an endless wait.

/** How long the page waits for an answer before it offers a retry. */
export const VERIFY_TIMEOUT_MS = 10_000

interface Sent<T> {
  data?: T
  error?: unknown
  response: Response
}

/** The `code` of an ApiProblem body, or null for anything else (a proxy's error page, an empty body). */
function problemCode(body: unknown): ProblemCode | null {
  if (typeof body !== 'object' || body === null) return null
  const { code } = body as { code?: unknown }
  return typeof code === 'string' ? (code as ProblemCode) : null
}

async function reply<T>(request: () => Promise<Sent<T>>): Promise<Reply<T>> {
  let sent: Sent<T>
  try {
    sent = await request()
  } catch {
    return { ok: false, failure: 'offline' }
  }
  if (sent.response.ok && sent.data !== undefined) return { ok: true, data: sent.data }
  return { ok: false, failure: failureFor(sent.response.status, problemCode(sent.error)) }
}

export function createVerifyApi(client: Client<paths>, timeoutMs = VERIFY_TIMEOUT_MS): VerifyApi {
  return {
    context: (token) =>
      reply(() =>
        client.GET('/api/verify/context', { params: { query: { t: token } }, signal: AbortSignal.timeout(timeoutMs) }),
      ),
    submit: (body) => reply(() => client.POST('/api/verify', { body, signal: AbortSignal.timeout(timeoutMs) })),
  }
}

export const verifyApi = createVerifyApi(api)
