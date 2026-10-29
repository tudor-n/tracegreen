import createClient from 'openapi-fetch'
import { describe, expect, it } from 'vitest'
import type { VerifyContext } from '@/api/client'
import type { paths } from '@/api/schema'
import { createVerifyApi } from './verify-api'

const context: VerifyContext = {
  nodeId: '11111111-1111-4111-8111-000000000001',
  supplierLabel: 'Shenzhen Diecast Co.',
  buyerName: 'Transilvania Electro-Assembly SRL',
  invoiceNumber: 'SDC-2026-1028',
  issueDate: '2026-10-28',
  itemDescription: 'Zinc die-cast sensor housings, type ZH-220',
  origin: 'Shenzhen, CN',
  dest: 'Sibiu, RO',
  modes: ['rail', 'sea', 'air'],
  defaultMode: 'air',
  energySources: ['grid', 'renewable', 'unknown'],
  defaultEnergy: 'grid',
  massT: 2.3,
  expiresAt: '2026-11-05T12:00:00Z',
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
  return { verify: createVerifyApi(client, timeoutMs), sent }
}

const json = (status: number, body: unknown, type = 'application/json') =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': type } })
const problem = (status: number, code: string) =>
  json(status, { type: 'about:blank', title: code, status, detail: '', code, nodeId: null }, 'application/problem+json')

describe('context', () => {
  it('asks for the context with the token as ?t= and returns it', async () => {
    const { verify, sent } = apiWith(() => json(200, context))

    expect(await verify.context('v1.abc')).toEqual({ ok: true, data: context })
    const url = new URL(sent[0].url)
    expect(sent[0].method).toBe('GET')
    expect(url.pathname).toBe('/api/verify/context')
    expect(url.searchParams.get('t')).toBe('v1.abc')
  })

  it.each([
    [401, 'InvalidToken', 'invalid'],
    [409, 'AlreadyUsed', 'alreadyUsed'],
    [410, 'LinkExpired', 'expired'],
    [410, 'InvoiceChanged', 'invoiceChanged'],
    [429, 'RateLimited', 'rateLimited'],
  ])('maps %i %s to %s', async (status, code, failure) => {
    const { verify } = apiWith(() => problem(status, code))
    expect(await verify.context('v1.abc')).toEqual({ ok: false, failure })
  })

  it("treats a proxy's error page as no connection", async () => {
    const { verify } = apiWith(() => new Response('Bad Gateway', { status: 502, headers: { 'Content-Type': 'text/plain' } }))
    expect(await verify.context('v1.abc')).toEqual({ ok: false, failure: 'offline' })
  })

  it('treats a network error as no connection', async () => {
    const { verify } = apiWith(() => {
      throw new TypeError('fetch failed')
    })
    expect(await verify.context('v1.abc')).toEqual({ ok: false, failure: 'offline' })
  })

  it('gives up on a request that hangs and offers a retry', async () => {
    const { verify } = apiWith(
      (request) =>
        new Promise<Response>((_, reject) => request.signal.addEventListener('abort', () => reject(request.signal.reason))),
      20,
    )
    expect(await verify.context('v1.abc')).toEqual({ ok: false, failure: 'offline' })
  })
})

describe('submit', () => {
  it('posts the answers, a skip as null, and returns the outcome', async () => {
    const outcome = { nodeId: context.nodeId, status: 'PARTIAL', warnings: [] }
    const { verify, sent } = apiWith(() => json(200, outcome))

    const body = { t: 'v1.abc', transportMode: 'sea' as const, energySource: null }
    expect(await verify.submit(body)).toEqual({ ok: true, data: outcome })
    expect(sent[0].method).toBe('POST')
    expect(new URL(sent[0].url).pathname).toBe('/api/verify')
    expect(await sent[0].json()).toEqual(body)
  })

  it('maps a replay to "already used" and a refused answer to "link not valid"', async () => {
    expect(await apiWith(() => problem(409, 'AlreadyUsed')).verify.submit({ t: 'v1.abc' })).toEqual({
      ok: false,
      failure: 'alreadyUsed',
    })
    expect(await apiWith(() => problem(422, 'InvalidAnswer')).verify.submit({ t: 'v1.abc' })).toEqual({
      ok: false,
      failure: 'invalid',
    })
  })
})
