import { api, type ProblemCode } from '@/api/client'
import { problemHeadlines, toolbarCopy } from '@/copy/dashboard'

// The toolbar's requests. Each resolves to the text to show; none touches the store, because the API publishes the
// result on the event stream (node.created, graph.reset) and the sync applies it.

export type ActionOutcome = { ok: boolean; message: string }

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

async function send<T>(
  request: () => Promise<Sent<T>>,
  onOk: (data: T | undefined) => string,
  byStatus: Partial<Record<number, string>> = {},
): Promise<ActionOutcome> {
  let sent: Sent<T>
  try {
    sent = await request()
  } catch {
    return { ok: false, message: toolbarCopy.unreachable }
  }
  const { status, ok } = sent.response
  if (ok) return { ok: true, message: onOk(sent.data) }
  return { ok: false, message: byStatus[status] ?? failureMessage(status, sent.error) }
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

/** Whether the API runs with demo tools. Health answers 503 with the same body while the database is down. */
export async function fetchDemoTools(signal?: AbortSignal): Promise<boolean> {
  try {
    const { data, error } = await api.GET('/api/health', { signal })
    return (data ?? error)?.demoTools === true
  } catch {
    return false
  }
}
