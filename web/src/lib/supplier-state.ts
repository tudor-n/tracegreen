import type {
  EnergySource,
  PlausibilityWarning,
  PortalAnswerRequest,
  PortalAnswerResult,
  PortalItem,
  PortalView,
  ProblemCode,
  TransportMode,
} from '@/api/client'
import { type Answers, canSubmit, offeredEnergy, offeredModes, preselect } from './verify-state'

// Pure decision logic behind the supplier page /supplier (F12): what each API answer means, which delivery is open, the
// answers pre-selected for it, and what happens after a send. The page performs the requests the state asks for (the
// list while loading or stale, the open delivery's answers while sending) and renders the rest, so every rule here is
// unit-tested without a browser. The questions, their order and their rules are those of /verify (./verify-state.ts).
// An answer comes back with the list as it now is, so confirming a delivery is one request.

/**
 * What a failed request means. `invalid`, `expired` and `gone` end the link; `changed` means the one delivery moved on
 * (already confirmed, no longer listed) and the list should be loaded again; `rateLimited` and `offline` can be retried.
 */
export type SupplierFailure = 'invalid' | 'expired' | 'gone' | 'changed' | 'rateLimited' | 'offline'

/** The page-wide failures: the link itself, or no list to show. */
export type PageFailure = Exclude<SupplierFailure, 'changed'>

/** The failures shown on the open delivery, which leave the rest of the list usable. */
export type DeliveryFailure = Extract<SupplierFailure, 'changed' | 'rateLimited' | 'offline'>

export type SupplierReply<T> = { ok: true; data: T } | { ok: false; failure: SupplierFailure }

/** The two requests the page makes. */
export interface SupplierApi {
  view(token: string): Promise<SupplierReply<PortalView>>
  answer(request: PortalAnswerRequest): Promise<SupplierReply<PortalAnswerResult>>
}

/**
 * By the API's `code` when it sent one, else by the HTTP status; `status` is null when no answer arrived. Anything else,
 * such as a 5xx or a proxy's 502 while the API restarts, counts as no connection, which offers a retry.
 */
export function supplierFailureFor(status: number | null, code: ProblemCode | null): SupplierFailure {
  switch (code) {
    case 'InvalidToken':
      return 'invalid'
    case 'LinkExpired':
      return 'expired'
    case 'InvoiceChanged':
      return 'gone'
    case 'RateLimited':
      return 'rateLimited'
    case 'AlreadyConfirmed':
    case 'NodeNotFound':
    case 'InvalidAnswer':
      return 'changed'
  }
  if (status === 401) return 'invalid'
  if (status === 404 || status === 409 || status === 422) return 'changed'
  if (status === 410) return 'expired'
  if (status === 429) return 'rateLimited'
  return 'offline'
}

const PAGE_RETRYABLE: Record<PageFailure, boolean> = {
  invalid: false,
  expired: false,
  gone: false,
  rateLimited: true,
  offline: true,
}

/** The open delivery: the supplier's answers so far, whether they are on the way, and what went wrong last time. */
export interface Draft {
  nodeId: string
  answers: Answers
  sending: boolean
  failure: DeliveryFailure | null
}

export type SupplierState =
  | { phase: 'loading' }
  | {
      phase: 'ready'
      view: PortalView
      /** At most one delivery is open, so the page stays one short list. */
      draft: Draft | null
      /** The "please reconfirm" warnings of answers sent from this page, by node id. */
      warnings: Record<string, PlausibilityWarning[]>
      /** True when the supplier asked to reload the list (a delivery changed meanwhile): it is fetched again. */
      stale: boolean
    }
  | { phase: 'failed'; failure: PageFailure; retryable: boolean }

export type SupplierInput =
  | { kind: 'loaded'; view: PortalView }
  | { kind: 'loadFailed'; failure: SupplierFailure }
  | { kind: 'open'; nodeId: string }
  | { kind: 'close' }
  | { kind: 'transport'; value: TransportMode | null }
  | { kind: 'energy'; value: EnergySource | null }
  | { kind: 'send' }
  | { kind: 'answered'; nodeId: string; result: PortalAnswerResult }
  | { kind: 'answerFailed'; nodeId: string; failure: SupplierFailure }
  | { kind: 'reload' }

/** A page opened without a token cannot load anything, so it starts on "link not valid" and sends no request. */
export function initialSupplierState(token: string | null): SupplierState {
  return token === null || token.trim() === '' ? pageFailed('invalid') : { phase: 'loading' }
}

function pageFailed(failure: SupplierFailure): SupplierState {
  // A list that cannot be read for "changed" reasons (a 404) means the link no longer fits.
  const page: PageFailure = failure === 'changed' ? 'invalid' : failure
  return { phase: 'failed', failure: page, retryable: PAGE_RETRYABLE[page] }
}

/** A confirmed delivery has nothing left to ask. */
export function isOpenForAnswers(item: PortalItem): boolean {
  return item.status !== 'VERIFIED'
}

/** The answers pre-selected when a delivery opens: what the API suggests, if it is on offer. */
export function draftFor(item: PortalItem): Draft {
  return {
    nodeId: item.nodeId,
    answers: {
      transport: preselect(offeredModes(item), item.defaultMode),
      energy: preselect(offeredEnergy(item), item.defaultEnergy),
    },
    sending: false,
    failure: null,
  }
}

/** The body of `POST /api/portal/answer`. A skipped answer is sent as null. */
export function answerBody(token: string, draft: Draft): PortalAnswerRequest {
  return { t: token, nodeId: draft.nodeId, transportMode: draft.answers.transport, energySource: draft.answers.energy }
}

/** "1 of 2 confirmed": PARTIAL deliveries still have an answer left, so they do not count. */
export function confirmedCount(view: PortalView): { confirmed: number; total: number } {
  return { confirmed: view.items.filter((i) => i.status === 'VERIFIED').length, total: view.items.length }
}

function itemOf(view: PortalView, nodeId: string): PortalItem | undefined {
  return view.items.find((i) => i.nodeId === nodeId)
}

/** The next state. Inputs that do not fit the current state (a late reply, a tap while sending) leave it unchanged. */
export function supplierReducer(state: SupplierState, input: SupplierInput): SupplierState {
  switch (input.kind) {
    case 'loaded': {
      if (state.phase === 'loading') return { phase: 'ready', view: input.view, draft: null, warnings: {}, stale: false }
      if (state.phase !== 'ready' || !state.stale) return state
      // A fresh list keeps the open delivery while it is still there to answer.
      const kept = state.draft === null ? undefined : itemOf(input.view, state.draft.nodeId)
      const draft = kept !== undefined && isOpenForAnswers(kept) ? state.draft : null
      return { ...state, view: input.view, draft, stale: false }
    }
    case 'loadFailed':
      if (state.phase === 'loading') return pageFailed(input.failure)
      // The list on screen is still right after a failed refresh; only the order and pre-selections wait.
      if (state.phase === 'ready' && state.stale) return { ...state, stale: false }
      return state
    case 'open': {
      if (state.phase !== 'ready' || state.draft?.sending) return state
      const item = itemOf(state.view, input.nodeId)
      if (item === undefined || !isOpenForAnswers(item)) return state
      return { ...state, draft: draftFor(item) }
    }
    case 'close':
      return state.phase === 'ready' && state.draft !== null && !state.draft.sending ? { ...state, draft: null } : state
    case 'transport':
    case 'energy': {
      if (state.phase !== 'ready' || state.draft === null || state.draft.sending) return state
      const item = itemOf(state.view, state.draft.nodeId)
      if (item === undefined) return state
      if (input.kind === 'transport' && input.value !== null && !offeredModes(item).includes(input.value)) return state
      if (input.kind === 'energy' && input.value !== null && !offeredEnergy(item).includes(input.value)) return state
      const key = input.kind === 'transport' ? 'transport' : 'energy'
      return { ...state, draft: { ...state.draft, answers: { ...state.draft.answers, [key]: input.value } } }
    }
    case 'send':
      if (state.phase !== 'ready' || state.draft === null || state.draft.sending || !canSubmit(state.draft.answers)) {
        return state
      }
      return { ...state, draft: { ...state.draft, sending: true, failure: null } }
    case 'answered': {
      if (state.phase !== 'ready' || state.draft?.nodeId !== input.nodeId || !state.draft.sending) return state
      // The API's list: the new status, the new order, and the answer's power source pre-selected on the others.
      return {
        ...state,
        view: input.result.view,
        draft: null,
        warnings: { ...state.warnings, [input.nodeId]: input.result.outcome.warnings },
      }
    }
    case 'answerFailed': {
      if (state.phase !== 'ready' || state.draft?.nodeId !== input.nodeId || !state.draft.sending) return state
      const { failure } = input
      if (failure === 'invalid' || failure === 'expired' || failure === 'gone') return pageFailed(failure)
      return { ...state, draft: { ...state.draft, sending: false, failure } }
    }
    case 'reload':
      if (state.phase === 'failed') return state.retryable ? { phase: 'loading' } : state
      if (state.phase === 'ready' && !state.draft?.sending) return { ...state, draft: null, stale: true }
      return state
  }
}

/** What a confirmed or half-confirmed delivery says the supplier answered, in the language shown. */
export function answerSummary(
  item: PortalItem,
  labels: { transport: Record<TransportMode, string>; energy: Record<EnergySource, string> },
): string {
  const parts = [
    item.answeredMode === null ? null : labels.transport[item.answeredMode],
    item.answeredEnergy === null ? null : labels.energy[item.answeredEnergy],
  ]
  return parts.filter((part): part is string => part !== null).join(' · ')
}
