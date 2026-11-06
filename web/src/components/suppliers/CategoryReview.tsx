import { CheckIcon, ChevronDownIcon, CircleAlertIcon } from 'lucide-react'
import { useEffect, useId, useRef, useState } from 'react'
import type { CategoryOption, NodeView } from '@/api/client'
import { Chip } from '@/components/shell/Page'
import { Button } from '@/components/ui/button'
import { reviewCopy } from '@/copy/dashboard'
import { loadCategories, reviewApi } from '@/lib/review-api'
import { needsCategoryCheck, refetchAfter, reviewChoices, reviewFailureMessage, reviewStamp } from '@/lib/review-display'
import { cn } from '@/lib/utils'
import { graphStore } from '@/store'

// The classification review in the side panel (F6): the node's category with the dominant line in full, "Correct" while
// the category waits for a person, and "Change category", which opens the allow-list, so a change takes two clicks. The
// page sends the category it showed (expectedCategoryId); the store changes only through the API's node.updated, so no
// figure appears before the server computed it, and the control waits until the store's node has moved on. A 409 means
// someone else reviewed first: the store is refreshed from a snapshot and the message says so.
// Test hooks: `review-control` with `data-review`, `review-line`, `review-confirm`, `review-change`, `review-option-<id>`,
// `review-error`.

/** The flag a node shows while its category waits for a person; text and icon, never colour alone. */
export function CategoryCheckChip({ label }: { label: string }) {
  return (
    <Chip tone="estimated">
      <CircleAlertIcon className="size-3.5" strokeWidth={2.4} aria-hidden />
      {label}
    </Chip>
  )
}

type Phase =
  | { kind: 'idle' }
  | { kind: 'sending' }
  /** The API accepted it; waiting for node.updated to move the store's node past `stamp`. */
  | { kind: 'waiting'; stamp: string }
  | { kind: 'saved' }
  | { kind: 'failed'; message: string }

export function CategoryReview({ node }: { node: NodeView }) {
  const classification = node.classification
  const flagged = needsCategoryCheck(classification)
  const stamp = reviewStamp(classification)
  const [phase, setPhase] = useState<Phase>({ kind: 'idle' })
  const [choosing, setChoosing] = useState(false)
  const change = useRef<HTMLButtonElement>(null)
  const titleId = useId()
  const choicesId = useId()

  // The reviewed node reaches the store with node.updated, before or after the POST answers; either way the stamp moves.
  if (phase.kind === 'waiting' && phase.stamp !== stamp) setPhase({ kind: 'saved' })

  // The "Correct" button goes away with the flag, so the focus moves to the button that stays.
  useEffect(() => {
    if (phase.kind === 'saved') change.current?.focus()
  }, [phase.kind])

  const busy = phase.kind === 'sending' || phase.kind === 'waiting'

  const submit = async (categoryId: string) => {
    setChoosing(false)
    setPhase({ kind: 'sending' })
    const answer = await reviewApi.review(node.id, categoryId, classification.categoryId)
    if (answer.ok) {
      setPhase({ kind: 'waiting', stamp })
      return
    }
    if (refetchAfter(answer.failure)) {
      const snapshot = await reviewApi.snapshot()
      if (snapshot !== null) graphStore.getState().hydrate(snapshot)
    }
    setPhase({ kind: 'failed', message: reviewFailureMessage(answer.failure) })
  }

  const closeChoices = () => {
    setChoosing(false)
    change.current?.focus()
  }

  return (
    <section
      aria-labelledby={titleId}
      data-testid="review-control"
      data-review={classification.review}
      className={cn('flex flex-col gap-3 rounded-2xl p-[18px]', flagged ? 'bg-estimated-tint' : 'bg-grouped')}
    >
      <h3 id={titleId} className="text-[13px] font-medium text-ink-2">
        {reviewCopy.title}
      </h3>
      <p className="-mt-2 text-[15px] font-semibold">{classification.categoryLabel}</p>
      <div className="flex flex-col gap-1">
        <p className="text-xs text-ink-2">{reviewCopy.lineLabel}</p>
        {/* Invoice text is data: rendered as text, in full. */}
        <p data-testid="review-line" className="text-sm leading-relaxed break-words whitespace-pre-wrap">
          {classification.lineDescription}
        </p>
      </div>
      <div className="flex flex-wrap gap-2">
        {flagged && (
          <Button data-testid="review-confirm" disabled={busy} onClick={() => void submit(classification.categoryId)}>
            <CheckIcon data-icon="inline-start" aria-hidden />
            {reviewCopy.confirm}
          </Button>
        )}
        <Button
          ref={change}
          variant="outline"
          data-testid="review-change"
          aria-expanded={choosing}
          aria-controls={choosing ? choicesId : undefined}
          disabled={busy}
          onClick={() => (choosing ? closeChoices() : setChoosing(true))}
        >
          {reviewCopy.change}
          <ChevronDownIcon data-icon="inline-end" aria-hidden className={cn('transition-transform', choosing && 'rotate-180')} />
        </Button>
      </div>
      {choosing && (
        <Choices id={choicesId} currentId={classification.categoryId} onPick={(id) => void submit(id)} onClose={closeChoices} />
      )}
      <p role="status" className="text-xs text-ink-2 empty:hidden">
        {busy ? reviewCopy.saving : phase.kind === 'saved' ? reviewCopy.saved : ''}
      </p>
      {phase.kind === 'failed' && (
        <p role="alert" data-testid="review-error" className="text-xs text-destructive">
          {phase.message}
        </p>
      )}
    </section>
  )
}

type CategoriesState = { kind: 'loading' } | { kind: 'ready'; categories: CategoryOption[] } | { kind: 'failed' }

/** The allow-list as buttons; Escape closes it without closing the panel, and the focus starts on the first choice. */
function Choices({ id, currentId, onPick, onClose }: { id: string; currentId: string; onPick: (id: string) => void; onClose: () => void }) {
  const [state, setState] = useState<CategoriesState>({ kind: 'loading' })
  const [attempt, setAttempt] = useState(0)
  const first = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    let live = true
    loadCategories().then(
      (categories) => live && setState({ kind: 'ready', categories }),
      () => live && setState({ kind: 'failed' }),
    )
    return () => {
      live = false
    }
  }, [attempt])

  useEffect(() => {
    if (state.kind === 'ready') first.current?.focus()
  }, [state.kind])

  const choices = state.kind === 'ready' ? reviewChoices(state.categories, currentId) : []
  const firstOpen = choices.findIndex((c) => !c.current)

  return (
    <div
      id={id}
      role="group"
      aria-label={reviewCopy.choices}
      onKeyDown={(event) => {
        if (event.key !== 'Escape') return
        // The panel closes on an Escape nobody handled; this one only closes the choices.
        event.preventDefault()
        onClose()
      }}
      className="flex flex-col gap-1 rounded-[14px] bg-card p-1.5 shadow-[0_0_0_0.5px_var(--hairline)]"
    >
      {state.kind === 'loading' && (
        <p role="status" className="px-2.5 py-2 text-[13px] text-muted-foreground">
          {reviewCopy.categoriesLoading}
        </p>
      )}
      {state.kind === 'failed' && (
        <div className="flex items-center justify-between gap-2 px-2.5 py-1.5">
          <p role="alert" className="text-[13px] text-destructive">
            {reviewCopy.categoriesFailed}
          </p>
          <Button
            size="sm"
            variant="outline"
            onClick={() => {
              setState({ kind: 'loading' })
              setAttempt((n) => n + 1)
            }}
          >
            {reviewCopy.retry}
          </Button>
        </div>
      )}
      {choices.map((choice, i) => (
        <button
          key={choice.id}
          ref={i === firstOpen ? first : undefined}
          type="button"
          data-testid={`review-option-${choice.id}`}
          disabled={choice.current}
          aria-current={choice.current || undefined}
          onClick={() => onPick(choice.id)}
          className="flex min-h-10 items-center justify-between gap-2 rounded-[10px] px-2.5 text-left text-sm font-medium transition-colors outline-none hover:bg-grouped focus-visible:ring-3 focus-visible:ring-ring disabled:text-muted-foreground disabled:hover:bg-transparent"
        >
          {choice.label}
          {choice.current && <span className="text-xs font-normal">{reviewCopy.current}</span>}
        </button>
      ))}
    </div>
  )
}
