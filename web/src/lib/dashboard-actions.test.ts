import { afterEach, describe, expect, it, vi } from 'vitest'
import { api } from '@/api/client'
import { failureMessage, requestLink, verifyAuditChain } from './dashboard-actions'

const problem = (status: number, code: string, detail: string) => ({
  type: 'about:blank',
  title: 'ignored: clients switch on code, never on titles',
  status,
  detail,
  code,
  nodeId: null,
})

describe('failureMessage', () => {
  it('puts the headline for the code before the API detail', () => {
    expect(failureMessage(409, problem(409, 'Duplicate', 'Invoice SDC-2026-1028 is already imported.'))).toBe(
      'Already on the graph. Invoice SDC-2026-1028 is already imported.',
    )
    expect(failureMessage(400, problem(400, 'DoctypeForbidden', 'Document type definitions are not accepted.'))).toBe(
      'Refused for safety. Document type definitions are not accepted.',
    )
    expect(failureMessage(413, problem(413, 'TooLarge', 'The invoice exceeds 4 MB.'))).toBe(
      'File too large. The invoice exceeds 4 MB.',
    )
    expect(failureMessage(422, problem(422, 'Schema', 'Malformed XML: Data at the root level is invalid.'))).toBe(
      'The invoice could not be read. Malformed XML: Data at the root level is invalid.',
    )
  })

  it('shows the headline alone when the detail is empty', () => {
    expect(failureMessage(422, problem(422, 'NoLane', '  '))).toBe('Supplier country not covered.')
  })

  it('falls back to the HTTP status for a body that is not an ApiProblem', () => {
    expect(failureMessage(502, '<html>Bad Gateway</html>')).toBe('The API answered HTTP 502. Try again.')
    expect(failureMessage(500, undefined)).toBe('The API answered HTTP 500. Try again.')
    expect(failureMessage(500, problem(500, 'SomethingNew', 'detail'))).toBe('The API answered HTTP 500. Try again.')
    expect(failureMessage(500, problem(500, 'toString', 'detail'))).toBe('The API answered HTTP 500. Try again.')
  })

  it("reads a proxy's bare 413 as too large", () => {
    expect(failureMessage(413, '<html>413 Request Entity Too Large</html>')).toBe('File too large.')
  })
})

describe('requestLink', () => {
  const nodeId = '00000000-0000-0000-0000-0000000000a1'
  const link = { nodeId, url: 'http://192.168.137.1:3000/verify/a1?t=v1.x', expiresAt: '2026-11-03T10:00:00Z', loopback: false }
  const answer = (status: number, body: { data?: unknown; error?: unknown }) =>
    vi.spyOn(api, 'POST').mockResolvedValue({ ...body, response: new Response(null, { status }) } as never)

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('posts the node id and returns the link', async () => {
    const post = answer(201, { data: link })
    expect(await requestLink(nodeId)).toEqual({ ok: true, data: link })
    expect(post).toHaveBeenCalledWith('/api/request-link', { body: { nodeId } })
  })

  it("shows the API's problem when the node is gone", async () => {
    answer(404, { error: problem(404, 'NodeNotFound', 'No node 00000000-0000-0000-0000-0000000000a1.') })
    expect(await requestLink(nodeId)).toEqual({
      ok: false,
      message: 'Supplier not on the graph. No node 00000000-0000-0000-0000-0000000000a1.',
    })
  })

  it('reports an unreachable API, and a success without a body by its status', async () => {
    vi.spyOn(api, 'POST').mockRejectedValue(new TypeError('Failed to fetch'))
    expect(await requestLink(nodeId)).toEqual({ ok: false, message: "Can't reach the API. Check that it is running." })
    answer(201, {})
    expect(await requestLink(nodeId)).toEqual({ ok: false, message: 'The API answered HTTP 201. Try again.' })
  })
})

describe('verifyAuditChain', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it("returns the API's verdict", async () => {
    const verdict = { valid: false, length: 7, brokenAt: 3 }
    const post = vi
      .spyOn(api, 'POST')
      .mockResolvedValue({ data: verdict, response: new Response(null, { status: 200 }) } as never)
    expect(await verifyAuditChain()).toEqual({ ok: true, data: verdict })
    expect(post).toHaveBeenCalledWith('/api/audit/verify')
  })

  it('shows the status while the endpoint is not implemented', async () => {
    vi.spyOn(api, 'POST').mockResolvedValue({ error: undefined, response: new Response(null, { status: 501 }) } as never)
    expect(await verifyAuditChain()).toEqual({ ok: false, message: 'The API answered HTTP 501. Try again.' })
  })
})
