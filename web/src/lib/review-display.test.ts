import { describe, expect, it } from 'vitest'
import type { CategoryOption, ClassificationView } from '@/api/client'
import { node } from '@/store/testFixtures'
import {
  CHECK_LIST_PATH,
  categoriesToCheck,
  categoryCheckLabel,
  categorySourceNote,
  needsCategoryCheck,
  refetchAfter,
  reviewChoices,
  reviewFailureFor,
  reviewFailureMessage,
  reviewStamp,
} from './review-display'

const base = node('a', 1000).classification
const classification = (overrides: Partial<ClassificationView>): ClassificationView => ({ ...base, ...overrides })

const aiMapped = classification({ categoryId: 'wire_harness', source: 'cache', aiMapped: true, confidence: 0.9, review: 'pending' })

describe('needsCategoryCheck', () => {
  it('flags every AI-mapped category the API marks pending, whatever its confidence', () => {
    expect(needsCategoryCheck(aiMapped)).toBe(true)
    expect(needsCategoryCheck({ ...aiMapped, source: 'model', confidence: 0.99 })).toBe(true)
    expect(needsCategoryCheck({ ...aiMapped, confidence: null })).toBe(true)
  })

  it('flags a category the AI could not give, until a person looked at it', () => {
    // The stub answers UNCLASSIFIED when the AI is down; the API gives it no review of its own.
    expect(needsCategoryCheck(classification({ categoryId: 'UNCLASSIFIED', source: 'stub', review: 'notNeeded' }))).toBe(true)
    expect(needsCategoryCheck(classification({ categoryId: 'UNCLASSIFIED', source: 'model', aiMapped: true, review: 'pending' }))).toBe(true)
    expect(needsCategoryCheck(classification({ categoryId: 'UNCLASSIFIED', source: 'buyer', review: 'confirmed' }))).toBe(false)
    expect(needsCategoryCheck(classification({ categoryId: 'UNCLASSIFIED', source: 'buyer', review: 'corrected' }))).toBe(false)
  })

  it('does not flag an invoice code or a reviewed category', () => {
    expect(needsCategoryCheck(classification({ categoryId: 'pcb', source: 'invoice', review: 'notNeeded' }))).toBe(false)
    expect(needsCategoryCheck({ ...aiMapped, source: 'buyer', aiMapped: false, confidence: null, review: 'confirmed' })).toBe(false)
    expect(needsCategoryCheck({ ...aiMapped, source: 'buyer', aiMapped: false, confidence: null, review: 'corrected' })).toBe(false)
  })

  it('does not flag a later line the buyer already decided for', () => {
    expect(needsCategoryCheck(classification({ categoryId: 'plastics', source: 'buyer', review: 'notNeeded' }))).toBe(false)
  })
})

describe('categoryCheckLabel and categoriesToCheck', () => {
  it('names the flag only while the node waits', () => {
    expect(categoryCheckLabel(aiMapped)).toBe('Check category')
    expect(categoryCheckLabel({ ...aiMapped, review: 'confirmed', source: 'buyer' })).toBeNull()
  })

  it('keeps the waiting nodes in the order given', () => {
    const nodes = [
      node('n3', 5000, { classification: aiMapped }),
      node('n2', 4000),
      node('n5', 3000, { classification: { ...aiMapped, categoryId: 'connectors' } }),
    ]
    expect(categoriesToCheck(nodes).map((n) => n.id)).toEqual(['n3', 'n5'])
    expect(categoriesToCheck([])).toEqual([])
  })

  it("leads the Overview's counter to the filtered supplier list", () => {
    expect(CHECK_LIST_PATH).toBe('/suppliers?filter=check')
  })
})

describe('categorySourceNote', () => {
  it('says the buyer set the category once the source is the buyer, and nothing otherwise', () => {
    expect(categorySourceNote(classification({ source: 'buyer', review: 'corrected' }))).toBe('Category set by buyer')
    expect(categorySourceNote(classification({ source: 'buyer', review: 'confirmed' }))).toBe('Category set by buyer')
    expect(categorySourceNote(aiMapped)).toBeNull()
    expect(categorySourceNote(classification({ source: 'invoice' }))).toBeNull()
  })
})

describe('reviewChoices', () => {
  const categories: CategoryOption[] = [
    { id: 'zinc_diecast', label: 'Zinc die-cast parts' },
    { id: 'UNCLASSIFIED', label: 'Unclassified' },
    { id: 'pcb', label: 'Printed circuit boards' },
  ]

  it("keeps the API's order with Unclassified last and marks the node's own category", () => {
    expect(reviewChoices(categories, 'pcb')).toEqual([
      { id: 'zinc_diecast', label: 'Zinc die-cast parts', current: false },
      { id: 'pcb', label: 'Printed circuit boards', current: true },
      { id: 'UNCLASSIFIED', label: 'Unclassified', current: false },
    ])
  })

  it('marks nothing when the node has a category the list no longer holds', () => {
    expect(reviewChoices(categories, 'steel').some((c) => c.current)).toBe(false)
    expect(reviewChoices([], 'pcb')).toEqual([])
  })
})

describe('reviewStamp', () => {
  it('moves on with the category, the review state or the review time, and with nothing else', () => {
    const before = reviewStamp(aiMapped)
    expect(reviewStamp({ ...aiMapped, confidence: 0.5, lineDescription: 'other' })).toBe(before)
    expect(reviewStamp({ ...aiMapped, review: 'confirmed', reviewedAt: '2026-11-06T12:00:00Z' })).not.toBe(before)
    expect(reviewStamp({ ...aiMapped, categoryId: 'pcb' })).not.toBe(before)
    const reviewed = { ...aiMapped, review: 'corrected' as const, reviewedAt: '2026-11-06T12:00:00Z' }
    expect(reviewStamp({ ...reviewed, reviewedAt: '2026-11-06T12:05:00Z' })).not.toBe(reviewStamp(reviewed))
  })
})

describe('reviewFailureFor and reviewFailureMessage', () => {
  it.each([
    [409, 'CategoryChanged', 'changed'],
    [422, 'CategoryNotAllowed', 'notAllowed'],
    [404, 'NodeNotFound', 'gone'],
    [409, null, 'changed'],
    [422, null, 'notAllowed'],
    [404, null, 'gone'],
  ] as const)('maps %i %s to %s', (status, code, kind) => {
    expect(reviewFailureFor(status, code).kind).toBe(kind)
  })

  it('treats no answer as offline and anything else by its status', () => {
    expect(reviewFailureFor(null, null)).toEqual({ kind: 'offline' })
    expect(reviewFailureFor(500, null)).toEqual({ kind: 'other', status: 500 })
    expect(reviewFailureFor(400, 'Schema')).toEqual({ kind: 'other', status: 400 })
  })

  it('says what happened, in the copy', () => {
    expect(reviewFailureMessage({ kind: 'changed' })).toBe('Someone changed this category in the meantime. It now shows as it is; check it again.')
    expect(reviewFailureMessage({ kind: 'notAllowed' })).toBe('That category is not in the list. Pick one of the categories shown.')
    expect(reviewFailureMessage({ kind: 'gone' })).toBe('Supplier not on the graph.')
    expect(reviewFailureMessage({ kind: 'offline' })).toBe("Can't reach the API. Check that it is running.")
    expect(reviewFailureMessage({ kind: 'other', status: 500 })).toBe('The API answered HTTP 500. Try again.')
  })

  it('refreshes the store only when the node moved on or left the graph', () => {
    expect(refetchAfter({ kind: 'changed' })).toBe(true)
    expect(refetchAfter({ kind: 'gone' })).toBe(true)
    expect(refetchAfter({ kind: 'notAllowed' })).toBe(false)
    expect(refetchAfter({ kind: 'offline' })).toBe(false)
  })
})
