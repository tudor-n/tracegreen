import type { Client } from 'openapi-fetch'
import { api, type CategoryOption, type GraphSnapshot, type NodeView } from '@/api/client'
import type { paths } from '@/api/schema'
import { reply, type Result } from './api-reply'
import { type ReviewFailure, reviewFailureFor } from './review-display'

// The classification review's requests (F6). None touches the store: the API publishes the reviewed node with
// `node.updated` and the sync applies it, so no figure is shown before the server computed it. After a 409 the caller
// hydrates the store from `snapshot` instead of waiting for the stream.

export interface ReviewApi {
  /** POST /api/nodes/{nodeId}/classification with the category the page showed, so a stale page is refused (409). */
  review(nodeId: string, categoryId: string, expectedCategoryId: string): Promise<Result<NodeView, ReviewFailure>>
  /** GET /api/categories: the allow-list, in seed order, Unclassified included. */
  categories(): Promise<Result<CategoryOption[], null>>
  /** GET /api/graph, for a refresh after a refused review; null when it fails (the sync keeps trying anyway). */
  snapshot(): Promise<GraphSnapshot | null>
}

export function createReviewApi(client: Client<paths>): ReviewApi {
  return {
    review: (nodeId, categoryId, expectedCategoryId) =>
      reply(
        () => client.POST('/api/nodes/{nodeId}/classification', { params: { path: { nodeId } }, body: { categoryId, expectedCategoryId } }),
        reviewFailureFor,
      ),
    categories: () => reply(() => client.GET('/api/categories'), () => null),
    snapshot: async () => {
      const answer = await reply(() => client.GET('/api/graph'), () => null)
      return answer.ok ? answer.data : null
    },
  }
}

export const reviewApi = createReviewApi(api)

let categoriesOnce: Promise<CategoryOption[]> | null = null

/**
 * The allow-list, asked for once per page load and shared by every panel: it changes only with `seed/categories.json`.
 * A failed request is forgotten, so the next caller asks again.
 */
export function loadCategories(source: ReviewApi = reviewApi): Promise<CategoryOption[]> {
  categoriesOnce ??= source.categories().then((answer) => {
    if (answer.ok) return answer.data
    categoriesOnce = null
    throw new Error('categories unavailable')
  })
  return categoriesOnce
}
