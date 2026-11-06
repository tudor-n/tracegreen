import type { CategoryOption, ClassificationView, NodeView, ProblemCode } from '@/api/client'
import { problemHeadlines, reviewCopy, toolbarCopy } from '@/copy/dashboard'
import { CATEGORY_SET_BY_BUYER, CHECK_CATEGORY } from '@/copy/labels'

// What the classification review (F6) shows, decided from the read model alone. The API decides `review`; the flag
// never reads the confidence, so a confident AI category is flagged as much as a doubtful one.

/** The category for "none of these"; it is on the API's allow-list like any other. */
export const UNCLASSIFIED = 'UNCLASSIFIED'

/** The supplier list's search parameter, and its value for the categories to check. */
export const CHECK_FILTER = { param: 'filter', value: 'check' } as const

/** Where the Overview's counter leads: the supplier list, filtered to the categories to check. */
export const CHECK_LIST_PATH = `/suppliers?${CHECK_FILTER.param}=${CHECK_FILTER.value}`

/**
 * Whether the node's category waits for a person: the API marks every AI-mapped category `pending`, and a node left
 * unclassified (the AI was unavailable) is flagged too until a person looks at it. Once a person confirmed or chose a
 * category, unclassified included, the flag is gone.
 */
export function needsCategoryCheck(classification: ClassificationView): boolean {
  return classification.review === 'pending' || (classification.categoryId === UNCLASSIFIED && classification.review === 'notNeeded')
}

/** "Check category" while the node waits for a person, else null. */
export function categoryCheckLabel(classification: ClassificationView): string | null {
  return needsCategoryCheck(classification) ? CHECK_CATEGORY : null
}

/** The nodes whose category waits for a person, in the order given. */
export function categoriesToCheck<T extends Pick<NodeView, 'classification'>>(nodes: readonly T[]): T[] {
  return nodes.filter((node) => needsCategoryCheck(node.classification))
}

/** "Category set by buyer" once a person confirmed or chose the category, else null. */
export function categorySourceNote(classification: ClassificationView): string | null {
  return classification.source === 'buyer' ? CATEGORY_SET_BY_BUYER : null
}

export interface ReviewChoice {
  id: string
  label: string
  /** The node's category now: choosing it confirms it, so it is marked rather than offered as a change. */
  current: boolean
}

/** "Change category"'s choices: the API's list in its order, with Unclassified last and the node's own category marked. */
export function reviewChoices(categories: readonly CategoryOption[], currentId: string): ReviewChoice[] {
  const named = categories.filter((c) => c.id !== UNCLASSIFIED)
  const unclassified = categories.filter((c) => c.id === UNCLASSIFIED)
  return [...named, ...unclassified].map((c) => ({ id: c.id, label: c.label, current: c.id === currentId }))
}

/**
 * What the store shows of a node's review, as one value. A review the API accepted reaches the store with
 * `node.updated`; the control waits until this value moves on, so it never shows a figure the store does not have.
 */
export function reviewStamp(classification: ClassificationView): string {
  return [classification.categoryId, classification.review, classification.reviewedAt ?? ''].join('|')
}

/** Why a review was refused: `changed` (409), `notAllowed` (422), `gone` (404), `offline` (no answer), or `other`. */
export type ReviewFailure = { kind: 'changed' | 'notAllowed' | 'gone' | 'offline' } | { kind: 'other'; status: number }

/** The failure for a refused review, by the ApiProblem `code` first and the HTTP status after it. */
export function reviewFailureFor(status: number | null, code: ProblemCode | null): ReviewFailure {
  if (status === null) return { kind: 'offline' }
  if (code === 'CategoryChanged' || (code === null && status === 409)) return { kind: 'changed' }
  if (code === 'CategoryNotAllowed' || (code === null && status === 422)) return { kind: 'notAllowed' }
  if (code === 'NodeNotFound' || (code === null && status === 404)) return { kind: 'gone' }
  return { kind: 'other', status }
}

/** The text for a refused review. */
export function reviewFailureMessage(failure: ReviewFailure): string {
  switch (failure.kind) {
    case 'changed':
      return reviewCopy.changed
    case 'notAllowed':
      return reviewCopy.notAllowed
    case 'gone':
      return problemHeadlines.NodeNotFound
    case 'offline':
      return toolbarCopy.unreachable
    case 'other':
      return toolbarCopy.httpError(failure.status)
  }
}

/** Whether the store should be refreshed from a snapshot after this failure: the node moved on, or left the graph. */
export function refetchAfter(failure: ReviewFailure): boolean {
  return failure.kind === 'changed' || failure.kind === 'gone'
}
