import createClient from 'openapi-fetch'
import { describe, expect, it } from 'vitest'
import type { paths } from '@/api/schema'
import { createTermsApi } from './terms-api'
import { supplierTerms as terms } from './test-fixtures'

function apiWith(handler: (request: Request) => Response | Promise<Response>, timeoutMs?: number) {
  const sent: Request[] = []
  const client = createClient<paths>({
    baseUrl: 'http://api.test',
    fetch: async (request: Request) => {
      sent.push(request)
      return handler(request)
    },
  })
  return { termsApi: createTermsApi(client, timeoutMs), sent }
}

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

describe('supplier terms', () => {
  it('reads the terms without a token', async () => {
    const { termsApi, sent } = apiWith(() => json(200, terms))

    expect(await termsApi.supplier()).toEqual({ ok: true, data: terms })
    const url = new URL(sent[0].url)
    expect(sent[0].method).toBe('GET')
    expect(url.pathname).toBe('/api/terms/supplier')
    expect(url.search).toBe('')
  })

  it('reports a server error, a network error and a hang as not loaded', async () => {
    expect(await apiWith(() => json(500, {})).termsApi.supplier()).toEqual({ ok: false, failure: 'unavailable' })
    expect(
      await apiWith(() => {
        throw new TypeError('fetch failed')
      }).termsApi.supplier(),
    ).toEqual({ ok: false, failure: 'unavailable' })
    const hanging = apiWith(
      (request) =>
        new Promise<Response>((_, reject) => request.signal.addEventListener('abort', () => reject(request.signal.reason))),
      20,
    )
    expect(await hanging.termsApi.supplier()).toEqual({ ok: false, failure: 'unavailable' })
  })
})
