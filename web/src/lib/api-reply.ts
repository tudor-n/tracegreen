import type { ProblemCode } from '@/api/client'

// The answer to one request through the generated client, for the supplier-facing pages: the body, or a failure the
// caller names from the HTTP status and the ApiProblem `code`. A thrown request (network error, timeout abort) has
// neither, so it reaches `failure` as (null, null).

/** What openapi-fetch resolves to. */
export interface Sent<T> {
  data?: T
  error?: unknown
  response: Response
}

export type Result<T, F> = { ok: true; data: T } | { ok: false; failure: F }

/** The `code` of an ApiProblem body, or null for anything else (a proxy's error page, an empty body). */
function problemCode(body: unknown): ProblemCode | null {
  if (typeof body !== 'object' || body === null) return null
  const { code } = body as { code?: unknown }
  return typeof code === 'string' ? (code as ProblemCode) : null
}

export async function reply<T, F>(
  request: () => Promise<Sent<T>>,
  failure: (status: number | null, code: ProblemCode | null) => F,
): Promise<Result<T, F>> {
  let sent: Sent<T>
  try {
    sent = await request()
  } catch {
    return { ok: false, failure: failure(null, null) }
  }
  if (sent.response.ok && sent.data !== undefined) return { ok: true, data: sent.data }
  return { ok: false, failure: failure(sent.response.status, problemCode(sent.error)) }
}
