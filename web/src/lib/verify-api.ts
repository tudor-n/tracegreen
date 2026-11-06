import type { Client } from 'openapi-fetch'
import { api, type ProblemCode } from '@/api/client'
import type { paths } from '@/api/schema'
import { failureFor, type Reply, type VerifyApi } from './verify-state'

// The supplier page's two requests through the generated client, each answered as a Reply that the page's state
// machine reads. A request that hangs counts as no connection, so the supplier gets a retry instead of an endless wait.

/** How long the page waits for an answer before it offers a retry. */
export const VERIFY_TIMEOUT_MS = 10_000

export interface Sent<T> {
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

/**
 * A request's body, or what went wrong as `failureOf` reads it: by the API's `code` and the HTTP status, or with both
 * null when no answer arrived (a thrown fetch, a timeout). The supplier page (F12) reads failures its own way.
 */
export async function replyWith<T, F>(
  request: () => Promise<Sent<T>>,
  failureOf: (status: number | null, code: ProblemCode | null) => F,
): Promise<{ ok: true; data: T } | { ok: false; failure: F }> {
  let sent: Sent<T>
  try {
    sent = await request()
  } catch {
    return { ok: false, failure: failureOf(null, null) }
  }
  if (sent.response.ok && sent.data !== undefined) return { ok: true, data: sent.data }
  return { ok: false, failure: failureOf(sent.response.status, problemCode(sent.error)) }
}

function reply<T>(request: () => Promise<Sent<T>>): Promise<Reply<T>> {
  return replyWith(request, failureFor)
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
