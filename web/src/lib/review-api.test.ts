import createClient from 'openapi-fetch'
import { describe, expect, it } from 'vitest'
import type { CategoryOption } from '@/api/client'
import type { paths } from '@/api/schema'
import { node, snapshot } from '@/store/testFixtures'
import { createReviewApi, loadCategories, type ReviewApi } from './review-api'

/** A client whose fetch is `handler`, recording every request it sends. */
function apiWith(handler: (request: Request) => Response | Promise<Response>) {
  const sent: Request[] = []
  const client = createClient<paths>({
    baseUrl: 'http://api.test',
    fetch: async (request: Request) => {
      sent.push(request)
      return handler(request)
    },
  })
  return { review: createReviewApi(client), sent }
}

const json = (status: number, body: unknown, type = 'application/json') =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': type } })
const problem = (status: number, code: string) =>
  json(status, { type: 'about:blank', title: code, status, detail: '', code, nodeId: null }, 'application/problem+json')

const categories: CategoryOption[] = [
  { id: 'pcb', label: 'Printed circuit boards' },
  { id: 'UNCLASSIFIED', label: 'Unclassified' },
]

describe('review', () => {
  it('posts the chosen and the expected category to the node and returns the node', async () => {
    const reviewed = node('n4', 600)
    const { review, sent } = apiWith(() => json(200, reviewed))

    expect(await review.review('n4', 'pcb', 'packaging_paper')).toEqual({ ok: true, data: reviewed })
    expect(sent[0].method).toBe('POST')
    expect(new URL(sent[0].url).pathname).toBe('/api/nodes/n4/classification')
    expect(await sent[0].json()).toEqual({ categoryId: 'pcb', expectedCategoryId: 'packaging_paper' })
  })

  it.each([
    [409, 'CategoryChanged', 'changed'],
    [422, 'CategoryNotAllowed', 'notAllowed'],
    [404, 'NodeNotFound', 'gone'],
  ])('maps %i %s to %s', async (status, code, kind) => {
    const { review } = apiWith(() => problem(status, code))
    expect(await review.review('n4', 'pcb', 'packaging_paper')).toEqual({ ok: false, failure: { kind } })
  })

  it('treats a network error as offline', async () => {
    const { review } = apiWith(() => {
      throw new TypeError('fetch failed')
    })
    expect(await review.review('n4', 'pcb', 'packaging_paper')).toEqual({ ok: false, failure: { kind: 'offline' } })
  })
})

describe('categories and snapshot', () => {
  it('reads the allow-list and the graph', async () => {
    const graph = snapshot(7, [node('n4', 600)])
    const { review, sent } = apiWith((request) => json(200, new URL(request.url).pathname === '/api/categories' ? categories : graph))

    expect(await review.categories()).toEqual({ ok: true, data: categories })
    expect(await review.snapshot()).toEqual(graph)
    expect(sent.map((r) => new URL(r.url).pathname)).toEqual(['/api/categories', '/api/graph'])
  })

  it('gives null for a snapshot that fails', async () => {
    const { review } = apiWith(() => json(503, {}))
    expect(await review.snapshot()).toBeNull()
  })
})

describe('loadCategories', () => {
  const source = (answers: Awaited<ReturnType<ReviewApi['categories']>>[]) => {
    let calls = 0
    const api: ReviewApi = {
      review: () => Promise.reject(new Error('unused')),
      snapshot: () => Promise.resolve(null),
      categories: () => Promise.resolve(answers[Math.min(calls++, answers.length - 1)]),
    }
    return { api, calls: () => calls }
  }

  it('asks again after a failure, then keeps the answer for every later caller', async () => {
    const { api, calls } = source([{ ok: false, failure: null }, { ok: true, data: categories }])

    await expect(loadCategories(api)).rejects.toThrow()
    expect(await loadCategories(api)).toEqual(categories)
    expect(await loadCategories(api)).toEqual(categories)
    expect(calls()).toBe(2)
  })
})
