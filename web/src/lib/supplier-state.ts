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
import type { Result } from './api-reply'
import {
  EMPTY_EVIDENCE,
  type EvidenceDraft,
  type EvidenceField,
  type EvidenceProblems,
  evidenceBody,
  evidenceForMode,
  evidenceProblems,
  hasEvidenceProblem,
  setEvidence,
} from './evidence'
import { type Answers, canSubmit, offeredEnergy, offeredModes, preselect } from './verify-state'

// Pure decision logic behind the supplier page /supplier (F12): what each API answer means, which delivery is open, the
// answers pre-selected for it, and what happens after a send. The page performs the requests the state asks for (the
// list while loading or stale, the open delivery's answers while sending) and renders the rest, so every rule here is
// unit-tested without a browser. The questions, their order and their rules are those of /verify (./verify-state.ts),
// and so is the supplier terms checkbox: one box on the page, unticked until the supplier ticks it, and no answer is sent
// without it. So is the optional proof (F7, rules in ./evidence.ts): per delivery, empty until the supplier adds some,
// and sent with that delivery's answers. An answer comes back with the list as it now is, so confirming a delivery is one
// request.

/**
 * What a failed request means. `invalid`, `expired` and `gone` end the link; `changed` means the one delivery moved on
 * (already confirmed, answered from elsewhere a moment earlier, no longer listed) and the list should be loaded again;
 * `termsChanged` means the supplier terms changed on the server since the page read them, so they are read and ticked
 * again; `proofRefused` means the API took the answers but not the proof that came with them (F7), so the delivery
 * stays open for the supplier to fix or clear it; `rateLimited` and `offline` can be retried.
 */
export type SupplierFailure =
  | 'invalid'
  | 'expired'
  | 'gone'
  | 'changed'
  | 'termsChanged'
  | 'proofRefused'
  | 'rateLimited'
  | 'offline'

/** The page-wide failures: the link itself, or no list to show. */
export type PageFailure = Exclude<SupplierFailure, 'changed' | 'termsChanged' | 'proofRefused'>

/** The failures shown on the open delivery, which leave the rest of the list usable. */
export type DeliveryFailure = Extract<SupplierFailure, 'changed' | 'termsChanged' | 'rateLimited' | 'offline'>

export type SupplierReply<T> = Result<T, SupplierFailure>

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
    // The page sends the terms it showed, so this means they changed on the server since the page loaded.
    case 'TermsNotAccepted':
      return 'termsChanged'
    // The answers were fine but the proof cannot be right (a number the page's own check let through): fix or clear it.
    case 'InvalidEvidence':
      return 'proofRefused'
    case 'AlreadyConfirmed':
    case 'DeliveryChanged':
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

/**
 * The open delivery: the supplier's answers and proof so far, whether they are on the way, and what went wrong last time.
 * `proofRefused` is true after the API refused the proof sent, until the supplier changes the answers or the proof, or
 * sends again.
 */
export interface Draft {
  nodeId: string
  answers: Answers
  evidence: EvidenceDraft
  sending: boolean
  failure: DeliveryFailure | null
  proofRefused: boolean
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
      /**
       * The SHA-256 of the terms the supplier ticked on this page, or null. Only the supplier's tick sets it, so the box
       * starts unticked (Civil Code art. 1203); terms that changed meanwhile carry another SHA-256 and read as unticked.
       */
      accepted: string | null
      /** The UTC date the list last arrived on, which bounds the dispatch date. */
      today: string
    }
  | { phase: 'failed'; failure: PageFailure; retryable: boolean }

export type SupplierInput =
  /** `today` is the page's date (todayUtc) when the list arrived; the reducer reads no clock. */
  | { kind: 'loaded'; view: PortalView; today: string }
  | { kind: 'loadFailed'; failure: SupplierFailure }
  | { kind: 'open'; nodeId: string }
  | { kind: 'close' }
  | { kind: 'transport'; value: TransportMode | null }
  | { kind: 'energy'; value: EnergySource | null }
  | { kind: 'evidence'; field: EvidenceField; value: string }
  | { kind: 'terms'; accepted: boolean }
  | { kind: 'send' }
  | { kind: 'answered'; nodeId: string; result: PortalAnswerResult }
  | { kind: 'answerFailed'; nodeId: string; failure: SupplierFailure }
  | { kind: 'reload' }

/** A page opened without a token cannot load anything, so it starts on "link not valid" and sends no request. */
export function initialSupplierState(token: string | null): SupplierState {
  return token === null || token.trim() === '' ? pageFailed('invalid') : { phase: 'loading' }
}

function pageFailed(failure: SupplierFailure): SupplierState {
  // A list that cannot be read for "changed" reasons (a 404) means the link no longer fits. Reading the list never asks
  // for the terms or takes proof, so a terms or proof refusal there is the same kind of mismatch.
  const page: PageFailure =
    failure === 'changed' || failure === 'termsChanged' || failure === 'proofRefused' ? 'invalid' : failure
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
    evidence: EMPTY_EVIDENCE,
    sending: false,
    failure: null,
    proofRefused: false,
  }
}

/** The open delivery's proof problems: its invoice's issue date and the day the list arrived bound the dispatch date. */
export function draftProblems(draft: Draft, item: Pick<PortalItem, 'issueDate'>, today: string): EvidenceProblems {
  return evidenceProblems(draft.evidence, draft.answers, item.issueDate, today)
}

/** True when the supplier ticked the terms the list on screen carries. */
export function termsAccepted(state: Extract<SupplierState, { phase: 'ready' }>): boolean {
  return state.accepted !== null && state.accepted === state.view.terms.sha256
}

/** The tick stays only while the list carries the terms it was given for. */
function keptTick(accepted: string | null, view: PortalView): string | null {
  return accepted === view.terms.sha256 ? accepted : null
}

/**
 * The body of `POST /api/portal/answer`. A skipped answer is sent as null. `acceptedTerms` is the SHA-256 of the terms
 * the supplier ticked, from the list, so the server records exactly what was accepted. `evidence` is there only when
 * the supplier added proof the answers carry, so an untouched section sends the same body as before F7.
 */
export function answerBody(token: string, draft: Draft, acceptedTerms: string): PortalAnswerRequest {
  const body: PortalAnswerRequest = {
    t: token,
    nodeId: draft.nodeId,
    transportMode: draft.answers.transport,
    energySource: draft.answers.energy,
    acceptedTerms,
  }
  const proof = evidenceBody(draft.evidence, draft.answers)
  return proof === null ? body : { ...body, evidence: proof }
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
      if (state.phase === 'loading') {
        return { phase: 'ready', view: input.view, draft: null, warnings: {}, stale: false, accepted: null, today: input.today }
      }
      if (state.phase !== 'ready' || !state.stale) return state
      // A fresh list keeps the open delivery while it is still there to answer.
      const kept = state.draft === null ? undefined : itemOf(input.view, state.draft.nodeId)
      const draft = kept !== undefined && isOpenForAnswers(kept) ? state.draft : null
      return {
        ...state,
        view: input.view,
        draft,
        stale: false,
        accepted: keptTick(state.accepted, input.view),
        today: input.today,
      }
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
      const { draft } = state
      if (input.kind === 'transport') {
        // A typed number stays with its document only while that document fits the new answer (./evidence.ts).
        const evidence = evidenceForMode(draft.evidence, draft.answers.transport, input.value)
        const answers = { ...draft.answers, transport: input.value }
        return { ...state, draft: { ...draft, answers, evidence, proofRefused: false } }
      }
      return { ...state, draft: { ...draft, answers: { ...draft.answers, energy: input.value }, proofRefused: false } }
    }
    case 'evidence': {
      if (state.phase !== 'ready' || state.draft === null || state.draft.sending) return state
      const evidence = setEvidence(state.draft.evidence, input.field, input.value, state.draft.answers)
      return evidence === state.draft.evidence ? state : { ...state, draft: { ...state.draft, evidence, proofRefused: false } }
    }
    case 'terms':
      if (state.phase !== 'ready' || state.draft?.sending) return state
      return { ...state, accepted: input.accepted ? state.view.terms.sha256 : null }
    case 'send': {
      if (
        state.phase !== 'ready' ||
        state.draft === null ||
        state.draft.sending ||
        !canSubmit(state.draft.answers) ||
        !termsAccepted(state)
      ) {
        return state
      }
      // A proof field the supplier filled in with a problem holds the answers back; untouched proof never does.
      const item = itemOf(state.view, state.draft.nodeId)
      if (item === undefined || hasEvidenceProblem(draftProblems(state.draft, item, state.today))) return state
      return { ...state, draft: { ...state.draft, sending: true, failure: null, proofRefused: false } }
    }
    case 'answered': {
      if (state.phase !== 'ready' || state.draft?.nodeId !== input.nodeId || !state.draft.sending) return state
      // The API's list: the new status, the new order, and the answer's power source pre-selected on the others.
      return {
        ...state,
        view: input.result.view,
        draft: null,
        warnings: { ...state.warnings, [input.nodeId]: input.result.outcome.warnings },
        accepted: keptTick(state.accepted, input.result.view),
      }
    }
    case 'answerFailed': {
      if (state.phase !== 'ready' || state.draft?.nodeId !== input.nodeId || !state.draft.sending) return state
      const { failure } = input
      if (failure === 'invalid' || failure === 'expired' || failure === 'gone') return pageFailed(failure)
      // New terms are read before anything is sent again: the list is fetched again with the box unticked, and the
      // supplier's answers stay on the open delivery.
      if (failure === 'termsChanged') {
        return { ...state, draft: { ...state.draft, sending: false, failure }, stale: true, accepted: null }
      }
      // The answer was not recorded: the delivery stays open with its answers, its proof and the tick, for the supplier
      // to fix or clear the proof. A refusal without proof sent cannot be fixed here, so it reads as a changed delivery.
      if (failure === 'proofRefused') {
        if (evidenceBody(state.draft.evidence, state.draft.answers) === null) {
          return { ...state, draft: { ...state.draft, sending: false, failure: 'changed' } }
        }
        return { ...state, draft: { ...state.draft, sending: false, proofRefused: true } }
      }
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
