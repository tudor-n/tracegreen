import { afterEach, describe, expect, it, vi } from 'vitest'
import { api } from '@/api/client'
import { savedScenario, scenarioResult } from '@/store/testFixtures'
import {
  askChat,
  correctCategory,
  failureMessage,
  fetchCategories,
  fetchSuggestions,
  requestLink,
  requestPortalLink,
  saveScenario,
  simulateChange,
  verifyAuditChain,
} from './dashboard-actions'

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

describe('requestPortalLink', () => {
  const nodeId = '00000000-0000-0000-0000-0000000000a1'
  const link = { nodeId, url: 'http://192.168.137.1:3000/supplier?t=p1.x', expiresAt: '2026-11-03T10:00:00Z', loopback: false }

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('posts the node id to the supplier page route and returns the link', async () => {
    const post = vi
      .spyOn(api, 'POST')
      .mockResolvedValue({ data: link, response: new Response(null, { status: 201 }) } as never)
    expect(await requestPortalLink(nodeId)).toEqual({ ok: true, data: link })
    expect(post).toHaveBeenCalledWith('/api/portal-link', { body: { nodeId } })
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

describe('simulateChange', () => {
  const nodeId = '11111111-1111-4111-8111-000000000001'
  const answer = (status: number, body: { data?: unknown; error?: unknown }) =>
    vi.spyOn(api, 'POST').mockResolvedValue({ ...body, response: new Response(null, { status }) } as never)

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('posts the node with a mode change and returns the result as the API computed it', async () => {
    const result = scenarioResult()
    const post = answer(200, { data: result })
    expect(await simulateChange(nodeId, { mode: 'sea', alternativeId: null })).toEqual({ ok: true, data: result })
    expect(post).toHaveBeenCalledWith('/api/simulate', { body: { nodeId, mode: 'sea', alternativeId: null } })
  })

  it('posts the node with an alternative supplier', async () => {
    const post = answer(200, { data: scenarioResult({ badge: 'premium' }) })
    await simulateChange(nodeId, { mode: null, alternativeId: 'N1-alt' })
    expect(post).toHaveBeenCalledWith('/api/simulate', { body: { nodeId, mode: null, alternativeId: 'N1-alt' } })
  })

  it("shows the API's problem for a change the node cannot take", async () => {
    answer(422, { error: problem(422, 'ModeNotAvailable', 'Lane CN-RO has no road leg.') })
    expect(await simulateChange(nodeId, { mode: 'road', alternativeId: null })).toEqual({
      ok: false,
      message: 'This lane has no such mode. Lane CN-RO has no road leg.',
    })
    answer(422, { error: problem(422, 'MassUnknown', '') })
    expect(await simulateChange(nodeId, { mode: 'sea', alternativeId: null })).toEqual({
      ok: false,
      message: 'Mass unknown for this item.',
    })
  })

  it('reports an unreachable API', async () => {
    vi.spyOn(api, 'POST').mockRejectedValue(new TypeError('Failed to fetch'))
    expect(await simulateChange(nodeId, { mode: 'sea', alternativeId: null })).toEqual({
      ok: false,
      message: "Can't reach the API. Check that it is running.",
    })
  })
})

describe('saveScenario', () => {
  const nodeId = '11111111-1111-4111-8111-000000000001'
  const answer = (status: number, body: { data?: unknown; error?: unknown }) =>
    vi.spyOn(api, 'POST').mockResolvedValue({ ...body, response: new Response(null, { status }) } as never)

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('posts the node with its change and returns the saved scenario', async () => {
    const scenario = savedScenario(nodeId, { mode: null, alternativeId: 'N1-alt' })
    const post = answer(201, { data: scenario })
    expect(await saveScenario(nodeId, { mode: null, alternativeId: 'N1-alt' })).toEqual({ ok: true, data: scenario })
    expect(post).toHaveBeenCalledWith('/api/scenarios', { body: { nodeId, mode: null, alternativeId: 'N1-alt' } })
  })

  it("shows the API's problem when the node is gone", async () => {
    answer(404, { error: problem(404, 'NodeNotFound', 'No node on the graph.') })
    expect(await saveScenario(nodeId, { mode: 'sea', alternativeId: null })).toEqual({
      ok: false,
      message: 'Supplier not on the graph. No node on the graph.',
    })
  })
})

describe('fetchCategories', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('returns the categories the API allows', async () => {
    const categories = [
      { id: 'pcb', label: 'Printed circuit boards' },
      { id: 'UNCLASSIFIED', label: 'Unclassified' },
    ]
    const get = vi.spyOn(api, 'GET').mockResolvedValue({ data: categories, response: new Response(null, { status: 200 }) } as never)
    expect(await fetchCategories()).toEqual({ ok: true, data: categories })
    expect(get).toHaveBeenCalledWith('/api/categories')
  })
})

describe('correctCategory', () => {
  const nodeId = '11111111-1111-4111-8111-000000000002'
  const answer = (status: number, body: { data?: unknown; error?: unknown }) =>
    vi.spyOn(api, 'PUT').mockResolvedValue({ ...body, response: new Response(null, { status }) } as never)

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('puts the category on the node and returns the node as the API recomputed it', async () => {
    const recomputed = { id: nodeId }
    const put = answer(200, { data: recomputed })
    expect(await correctCategory(nodeId, 'connectors')).toEqual({ ok: true, data: recomputed })
    expect(put).toHaveBeenCalledWith('/api/nodes/{nodeId}/classification', {
      params: { path: { nodeId } },
      body: { categoryId: 'connectors' },
    })
  })

  it("shows the API's problem for a category outside the allow-list", async () => {
    answer(422, { error: problem(422, 'CategoryNotAllowed', 'Category bogus is not one of the allowed categories.') })
    expect(await correctCategory(nodeId, 'bogus')).toEqual({
      ok: false,
      message: 'That category is not one of the allowed categories. Category bogus is not one of the allowed categories.',
    })
  })
})

describe('fetchSuggestions', () => {
  const nodeId = '11111111-1111-4111-8111-000000000002'

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it("asks for the node's suggestions with the caller's abort signal", async () => {
    const view = { suggestions: [{ alternativeId: 'N2-alt', rationale: 'Fits.' }], provider: 'openai:model-a' }
    const get = vi.spyOn(api, 'GET').mockResolvedValue({ data: view, response: new Response(null, { status: 200 }) } as never)
    const signal = new AbortController().signal
    expect(await fetchSuggestions(nodeId, signal)).toEqual({ ok: true, data: view })
    expect(get).toHaveBeenCalledWith('/api/nodes/{nodeId}/suggestions', { params: { path: { nodeId } }, signal })
  })

  it('reports an unreachable API instead of throwing', async () => {
    vi.spyOn(api, 'GET').mockRejectedValue(new TypeError('Failed to fetch'))
    expect((await fetchSuggestions(nodeId)).ok).toBe(false)
  })
})

describe('askChat', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('posts the question with the earlier questions and returns the checked answer', async () => {
    const answer = { status: 'answered', segments: [{ text: null, factId: 'S1.name' }], facts: [], provider: 'openai:model-a' }
    const post = vi.spyOn(api, 'POST').mockResolvedValue({ data: answer, response: new Response(null, { status: 200 }) } as never)
    expect(await askChat('Who emits most?', ['Before?'])).toEqual({ ok: true, data: answer })
    expect(post).toHaveBeenCalledWith('/api/chat', { body: { question: 'Who emits most?', history: ['Before?'] }, signal: undefined })
  })

  it('shows the rate limit as its headline', async () => {
    vi.spyOn(api, 'POST').mockResolvedValue({
      error: problem(429, 'RateLimited', 'Too many attempts from this address; wait a minute.'),
      response: new Response(null, { status: 429 }),
    } as never)
    const result = await askChat('Q', [])
    expect(result.ok).toBe(false)
  })
})
