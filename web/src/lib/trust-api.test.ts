import createClient from 'openapi-fetch'
import { describe, expect, it } from 'vitest'
import type { paths } from '@/api/schema'
import { node, snapshot } from '@/store/testFixtures'
import { createTrustApi } from './trust-api'

function apiWith(handler: (request: Request) => Response | Promise<Response>) {
  const sent: Request[] = []
  const client = createClient<paths>({
    baseUrl: 'http://api.test',
    fetch: async (request: Request) => {
      sent.push(request)
      return handler(request)
    },
  })
  return { trust: createTrustApi(client), sent }
}

const json = (status: number, body: unknown, type = 'application/json') =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': type } })
const problem = (status: number, code: string) =>
  json(status, { type: 'about:blank', title: code, status, detail: '', code, nodeId: null }, 'application/problem+json')

describe('trust api', () => {
  it('posts the buyer records to the node', async () => {
    const updated = node('n2', 600)
    const { trust, sent } = apiWith(() => json(200, updated))

    const body = { receivedOn: '2026-11-01', customsBorderMode: 'sea' as const, customsMrn: '26RO1234567890ABCD' }
    expect(await trust.recordReceipt('n2', body)).toEqual({ ok: true, data: updated })
    expect(sent[0].method).toBe('POST')
    expect(new URL(sent[0].url).pathname).toBe('/api/nodes/n2/receipt')
    expect(await sent[0].json()).toEqual(body)
  })

  it('posts a decision with the value the page showed', async () => {
    const { trust, sent } = apiWith(() => json(200, node('n3', 1)))

    await trust.reviewClaim('n3', 'energy', 'accepted', 'renewable')
    expect(new URL(sent[0].url).pathname).toBe('/api/nodes/n3/claims/energy')
    expect(await sent[0].json()).toEqual({ decision: 'accepted', expectedValue: 'renewable' })
  })

  it('puts the target', async () => {
    const { trust, sent } = apiWith(() => json(200, node('n2', 1)))

    await trust.setTarget('n2', { mode: 'rail', energy: null, alternativeId: null })
    expect(sent[0].method).toBe('PUT')
    expect(new URL(sent[0].url).pathname).toBe('/api/nodes/n2/target')
  })

  it.each([
    [409, 'ClaimChanged', 'changed'],
    [422, 'InvalidRecord', 'invalid'],
    [404, 'NodeNotFound', 'gone'],
  ])('maps %i %s to %s', async (status, code, kind) => {
    const { trust } = apiWith(() => problem(status, code))
    expect(await trust.reviewClaim('n3', 'mode', 'rejected', 'sea')).toEqual({ ok: false, failure: { kind } })
  })

  it('reads a snapshot, or null when the graph cannot be read', async () => {
    const graph = snapshot(3, [node('n2', 1)])
    expect(await apiWith(() => json(200, graph)).trust.snapshot()).toEqual(graph)
    expect(await apiWith(() => json(500, {})).trust.snapshot()).toBeNull()
  })
})
