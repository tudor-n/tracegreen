import {
  api,
  type ChainVerification,
  type ProblemCode,
  type RequestLinkResponse,
  type ScenarioChange,
  type ScenarioResult,
} from '@/api/client'
import { problemHeadlines, simulatorCopy, toolbarCopy } from '@/copy/dashboard'

// The dashboard's requests. Each resolves to what to show; none touches the store, because the API publishes the
// result on the event stream (node.created, graph.reset, audit.appended) and the sync applies it.

export type ActionOutcome = { ok: boolean; message: string }

/** A request's answer: its body, or the text to show instead. */
export type Answer<T> = { ok: true; data: T } | { ok: false; message: string }

/** The text for a failed request: the headline for the API's error code, then its detail; else the HTTP status. */
export function failureMessage(status: number, body: unknown): string {
  const problem = readProblem(body)
  if (problem !== null) return [problemHeadlines[problem.code], problem.detail].filter(Boolean).join(' ')
  // A proxy in front of the API (nginx's body limit in the demo image) answers 413 with its own page.
  if (status === 413) return problemHeadlines.TooLarge
  return toolbarCopy.httpError(status)
}

/** The code and detail of an ApiProblem body, or null for anything else. Clients switch on `code`, never on titles. */
function readProblem(body: unknown): { code: ProblemCode; detail: string } | null {
  if (typeof body !== 'object' || body === null) return null
  const { code, detail } = body as { code?: unknown; detail?: unknown }
  if (typeof code !== 'string' || !Object.hasOwn(problemHeadlines, code)) return null
  return { code: code as ProblemCode, detail: typeof detail === 'string' ? detail.trim() : '' }
}

interface Sent<T> {
  data?: T
  error?: unknown
  response: Response
}

/** The successful response, or the text to show when the request failed or the API refused it. */
async function call<T>(
  request: () => Promise<Sent<T>>,
  byStatus: Partial<Record<number, string>> = {},
): Promise<Answer<Sent<T>>> {
  let sent: Sent<T>
  try {
    sent = await request()
  } catch {
    return { ok: false, message: toolbarCopy.unreachable }
  }
  const { status, ok } = sent.response
  if (ok) return { ok: true, data: sent }
  return { ok: false, message: byStatus[status] ?? failureMessage(status, sent.error) }
}

async function send<T>(
  request: () => Promise<Sent<T>>,
  onOk: (data: T | undefined) => string,
  byStatus: Partial<Record<number, string>> = {},
): Promise<ActionOutcome> {
  const answer = await call(request, byStatus)
  return answer.ok ? { ok: true, message: onOk(answer.data.data) } : answer
}

/** A request whose body is the point: a success without one is reported by its status. */
async function fetchBody<T>(request: () => Promise<Sent<T>>): Promise<Answer<T>> {
  const answer = await call(request)
  if (!answer.ok) return answer
  const { data, response } = answer.data
  return data === undefined ? { ok: false, message: toolbarCopy.httpError(response.status) } : { ok: true, data }
}

/** POST /api/ingest/ubl with the file in the multipart field `file`. */
export function importInvoice(file: File): Promise<ActionOutcome> {
  return send(
    () =>
      api.POST('/api/ingest/ubl', {
        // openapi-typescript types a binary field as `string`; the serializer below sends the File itself.
        body: { file: file as unknown as string },
        bodySerializer: () => {
          const form = new FormData()
          form.append('file', file)
          return form
        },
      }),
    (data) => toolbarCopy.imported(data?.node.label ?? file.name),
  )
}

/** POST /api/ingest/demo: seed N1 with its fixed ids. */
export function loadDemoInvoice(): Promise<ActionOutcome> {
  return send(
    () => api.POST('/api/ingest/demo'),
    (data) => toolbarCopy.imported(data?.node.label ?? ''),
  )
}

/** POST /api/dev/reset: back to the seed start state (404 unless the API runs with DEMO_TOOLS=1). */
export function resetDemo(): Promise<ActionOutcome> {
  return send(
    () => api.POST('/api/dev/reset'),
    () => toolbarCopy.resetDone,
    { 404: toolbarCopy.resetUnavailable },
  )
}

/**
 * POST /api/request-link: a signed single-use link for the supplier's phone. The token exists only inside `url`, so
 * the answer is kept in the component that shows it and never logged.
 */
export function requestLink(nodeId: string): Promise<Answer<RequestLinkResponse>> {
  return fetchBody(() => api.POST('/api/request-link', { body: { nodeId } }))
}

/** POST /api/audit/verify: the API recomputes every hash of the audit log and names the first entry that fails. */
export function verifyAuditChain(): Promise<Answer<ChainVerification>> {
  return fetchBody(() => api.POST('/api/audit/verify'))
}

/**
 * A simulated change for a node: the API's result against the node as it is now. The route is not in the contract yet
 * (A7, slice 5), so until it is this answers that the simulator is not available; the panel shows that as its error.
 */
export function simulateChange(_nodeId: string, _change: ScenarioChange): Promise<Answer<ScenarioResult>> {
  return Promise.resolve({ ok: false, message: simulatorCopy.unavailable })
}

/** Whether the API runs with demo tools. Health answers 503 with the same body while the database is down. */
export async function fetchDemoTools(signal?: AbortSignal): Promise<boolean> {
  try {
    const { data, error } = await api.GET('/api/health', { signal })
    return (data ?? error)?.demoTools === true
  } catch {
    return false
  }
}
