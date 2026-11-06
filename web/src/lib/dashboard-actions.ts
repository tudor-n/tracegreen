import {
  api,
  type ChatAnswer,
  type DraftInvoice,
  type IngestResult,
  type PdfDraftView,
  type ChainVerification,
  type ProblemCode,
  type RequestLinkResponse,
  type Scenario,
  type ScenarioChange,
  type ScenarioResult,
  type SuggestionsView,
} from '@/api/client'
import { problemHeadlines, toolbarCopy } from '@/copy/dashboard'

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

/**
 * POST /api/ingest/pdf/extract (F9): an AI copies the PDF's fields into a draft for a person to check. Nothing is
 * imported until the draft is confirmed.
 */
export function extractPdf(file: File): Promise<Answer<PdfDraftView>> {
  return fetchBody(() =>
    api.POST('/api/ingest/pdf/extract', {
      // openapi-fetch sends no body unless one is given; the serializer below sends the File itself.
      body: file as never,
      bodySerializer: () => {
        const form = new FormData()
        form.append('file', file)
        return form
      },
    }),
  )
}

/** POST /api/ingest/pdf/confirm: import the fields as the person checked them; the node arrives through node.created. */
export function confirmPdf(draftId: string, invoice: DraftInvoice): Promise<Answer<IngestResult>> {
  return fetchBody(() => api.POST('/api/ingest/pdf/confirm', { body: { draftId, invoice } }))
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

/**
 * POST /api/portal-link: the supplier page link (F12) for the supplier of this node, reusable until it expires. Like a
 * verification link, the token exists only inside `url`, so the answer stays in the component that shows it.
 */
export function requestPortalLink(nodeId: string): Promise<Answer<RequestLinkResponse>> {
  return fetchBody(() => api.POST('/api/portal-link', { body: { nodeId } }))
}

/** POST /api/audit/verify: the API recomputes every hash of the audit log and names the first entry that fails. */
export function verifyAuditChain(): Promise<Answer<ChainVerification>> {
  return fetchBody(() => api.POST('/api/audit/verify'))
}

/**
 * POST /api/simulate: what one change (a mode or an alternative supplier) would do to the node as it is now. The API
 * computes every figure, delta, the MAC and its badge, and stores nothing; a mode the lane lacks, a node without a
 * mass or an alternative of another category comes back as its problem's headline and detail.
 */
export function simulateChange(nodeId: string, change: ScenarioChange): Promise<Answer<ScenarioResult>> {
  return fetchBody(() => api.POST('/api/simulate', { body: { nodeId, ...change } }))
}

/**
 * POST /api/scenarios, the simulator's "Apply": the API recomputes the change, saves it next to the node (never over
 * it) and publishes scenario.saved. The node keeps its figures; the answer is the saved scenario.
 */
export function saveScenario(nodeId: string, change: ScenarioChange): Promise<Answer<Scenario>> {
  return fetchBody(() => api.POST('/api/scenarios', { body: { nodeId, ...change } }))
}

/**
 * GET /api/nodes/{id}/suggestions (F11): which of the node's curated alternatives the AI picks, each with a short reason.
 * Never a figure and never a new supplier. Asked when the What if tab opens and aborted when it closes; a failure only
 * means no suggestion, so it never blocks the simulator.
 */
export function fetchSuggestions(nodeId: string, signal?: AbortSignal): Promise<Answer<SuggestionsView>> {
  return fetchBody(() => api.GET('/api/nodes/{nodeId}/suggestions', { params: { path: { nodeId } }, signal }))
}

/**
 * POST /api/chat (F13): the question and the last three typed questions. The answer's sentence is the AI's, every value
 * in it the API filled in from the data after checking the sentence; a question the data does not answer comes back
 * notAnswered, never as a guess.
 */
export function askChat(question: string, history: readonly string[], signal?: AbortSignal): Promise<Answer<ChatAnswer>> {
  return fetchBody(() => api.POST('/api/chat', { body: { question, history: [...history] }, signal }))
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
