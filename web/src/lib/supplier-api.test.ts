import createClient from 'openapi-fetch'
import { describe, expect, it } from 'vitest'
import type { PortalView } from '@/api/client'
import type { paths } from '@/api/schema'
import { createSupplierApi } from './supplier-api'
import { verifyTerms } from './test-fixtures'

const view: PortalView = {
  supplierLabel: 'Oradea Harness SRL',
  buyerName: 'Transilvania Electro-Assembly SRL',
  expiresAt: '2026-11-13T12:00:00Z',
  items: [],
  terms: verifyTerms,
}

/** A client whose fetch is `handler`, recording every request it sends. */
function apiWith(handler: (request: Request) => Response | Promise<Response>, timeoutMs?: number) {
  const sent: Request[] = []
  const client = createClient<paths>({
    baseUrl: 'http://api.test',
    fetch: async (request: Request) => {
      sent.push(request)
      return handler(request)
    },
  })
  return { supplier: createSupplierApi(client, timeoutMs), sent }
}

const json = (status: number, body: unknown, type = 'application/json') =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': type } })
const problem = (status: number, code: string) =>
  json(status, { type: 'about:blank', title: code, status, detail: '', code, nodeId: null }, 'application/problem+json')

describe('view', () => {
  it('posts the token in the body, never in the URL, and returns the list', async () => {
    const { supplier, sent } = apiWith(() => json(200, view))

    expect(await supplier.view('p1.abc')).toEqual({ ok: true, data: view })
    expect(sent[0].method).toBe('POST')
    const url = new URL(sent[0].url)
    expect(url.pathname).toBe('/api/portal/view')
    expect(url.search).toBe('')
    expect(await sent[0].json()).toEqual({ t: 'p1.abc' })
  })

  it.each([
    [401, 'InvalidToken', 'invalid'],
    [410, 'LinkExpired', 'expired'],
    [410, 'InvoiceChanged', 'gone'],
    [429, 'RateLimited', 'rateLimited'],
  ])('maps %i %s to %s', async (status, code, failure) => {
    const { supplier } = apiWith(() => problem(status, code))
    expect(await supplier.view('p1.abc')).toEqual({ ok: false, failure })
  })

  it('treats a network error as no connection', async () => {
    const { supplier } = apiWith(() => {
      throw new TypeError('fetch failed')
    })
    expect(await supplier.view('p1.abc')).toEqual({ ok: false, failure: 'offline' })
  })

  it('gives up on a request that hangs and offers a retry', async () => {
    const { supplier } = apiWith(
      (request) =>
        new Promise<Response>((_, reject) => request.signal.addEventListener('abort', () => reject(request.signal.reason))),
      20,
    )
    expect(await supplier.view('p1.abc')).toEqual({ ok: false, failure: 'offline' })
  })
})

describe('answer', () => {
  const body = {
    t: 'p1.abc',
    nodeId: '11111111-1111-4111-8111-000000000003',
    transportMode: 'road',
    energySource: null,
    acceptedTerms: verifyTerms.sha256,
  } as const

  it('posts the answers with the accepted terms and returns the outcome with the list as it now is', async () => {
    const result = { outcome: { nodeId: body.nodeId, status: 'PARTIAL', warnings: [] }, view }
    const { supplier, sent } = apiWith(() => json(200, result))

    expect(await supplier.answer(body)).toEqual({ ok: true, data: result })
    expect(new URL(sent[0].url).pathname).toBe('/api/portal/answer')
    expect(await sent[0].json()).toEqual(body)
  })

  it.each([
    [409, 'AlreadyConfirmed', 'changed'],
    [409, 'DeliveryChanged', 'changed'],
    [404, 'NodeNotFound', 'changed'],
    [422, 'InvalidAnswer', 'changed'],
    [422, 'TermsNotAccepted', 'termsChanged'],
    [401, 'InvalidToken', 'invalid'],
  ])('maps %i %s to %s', async (status, code, failure) => {
    const { supplier } = apiWith(() => problem(status, code))
    expect(await supplier.answer(body)).toEqual({ ok: false, failure })
  })
})
