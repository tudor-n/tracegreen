import type {
  EnergySource,
  NodeStatus,
  PlausibilityCode,
  PlausibilityWarning,
  ProblemCode,
  TransportMode,
  VerifyContext,
  VerifyOutcome,
  VerifyRequest,
} from '@/api/client'
import { DEFAULT_LANG, type Lang, type VerifyResult } from '@/copy/verify'

// Pure decision logic behind the supplier page /verify/:nodeId: the language, which answers to offer and pre-select,
// what each API answer means for the page, and the page's states. The page performs the two requests the states ask for
// (the context while loading, the answers while submitting) and renders the rest, so every rule here is unit-tested
// without a browser.

/** `?lang=en` shows English; anything else, or no parameter, shows Romanian. */
export function resolveLang(param: string | null): Lang {
  return param?.trim().toLowerCase() === 'en' ? 'en' : DEFAULT_LANG
}

/** The error states a request can end in. */
export type Failure = Exclude<VerifyResult, 'thanks'>

/** A request's result for the page: its body, or what went wrong. */
export type Reply<T> = { ok: true; data: T } | { ok: false; failure: Failure }

/** The two requests the page makes. */
export interface VerifyApi {
  context(token: string): Promise<Reply<VerifyContext>>
  submit(request: VerifyRequest): Promise<Reply<VerifyOutcome>>
}

/** Failures the supplier can retry from the page. The others are final for this link. */
const RETRYABLE: Record<Failure, boolean> = {
  offline: true,
  rateLimited: true,
  alreadyUsed: false,
  expired: false,
  invoiceChanged: false,
  invalid: false,
}

/**
 * What a failed request means for the page: by the API's `code` when it sent one (clients switch on `code`, never on
 * titles), else by the HTTP status. `status` is null when no answer arrived. Anything else, such as a 5xx or a proxy's
 * 502 while the API restarts, counts as no connection, which offers a retry.
 */
export function failureFor(status: number | null, code: ProblemCode | null): Failure {
  switch (code) {
    case 'InvalidToken':
      return 'invalid'
    case 'AlreadyUsed':
      return 'alreadyUsed'
    case 'LinkExpired':
      return 'expired'
    case 'InvoiceChanged':
      return 'invoiceChanged'
    case 'RateLimited':
      return 'rateLimited'
    // The page offers the lane's modes alone and never sends two skips, so these mean the link no longer fits.
    case 'InvalidAnswer':
    case 'NodeNotFound':
      return 'invalid'
  }
  if (status === 401 || status === 404 || status === 422) return 'invalid'
  if (status === 409) return 'alreadyUsed'
  if (status === 410) return 'expired'
  if (status === 429) return 'rateLimited'
  return 'offline'
}

// Fixed orders, keyed by the generated enums, so the buttons never move between links and a new value fails the typecheck.
const MODE_ORDER: Record<TransportMode, number> = { road: 0, rail: 1, sea: 2, air: 3 }
const ENERGY_ORDER: Record<EnergySource, number> = { grid: 0, renewable: 1, unknown: 2 }

/** The transport modes the link's lane has, each once, in a fixed order. */
export function offeredModes(context: VerifyContext): TransportMode[] {
  return [...new Set(context.modes)].sort((a, b) => MODE_ORDER[a] - MODE_ORDER[b])
}

/** The energy answers the link offers, each once, in a fixed order. */
export function offeredEnergy(context: VerifyContext): EnergySource[] {
  return [...new Set(context.energySources)].sort((a, b) => ENERGY_ORDER[a] - ENERGY_ORDER[b])
}

/** The pre-selected answer: the context's default when it is offered, else the first option; none when there is none. */
function preselect<T>(options: T[], preferred: T): T | null {
  return options.includes(preferred) ? preferred : (options[0] ?? null)
}

/** Each question's answer: the chosen option, or null when the supplier skipped it (the node then becomes PARTIAL). */
export interface Answers {
  transport: TransportMode | null
  energy: EnergySource | null
}

/** The API refuses a submission with both questions skipped, so the page does not send one. */
export function canSubmit(answers: Answers): boolean {
  return answers.transport !== null || answers.energy !== null
}

/** The body of `POST /api/verify`. A skipped answer is sent as null; the mass is left to the invoice. */
export function submitBody(token: string, answers: Answers): VerifyRequest {
  return { t: token, transportMode: answers.transport, energySource: answers.energy }
}

/** Where a retry goes: the context request again, or the same answers sent again. */
type RetryTarget = { phase: 'loading' } | { phase: 'submitting'; context: VerifyContext; answers: Answers }

export type VerifyState =
  | { phase: 'loading' }
  | { phase: 'ready'; context: VerifyContext; answers: Answers }
  | { phase: 'submitting'; context: VerifyContext; answers: Answers }
  | { phase: 'done'; status: NodeStatus; warnings: PlausibilityWarning[] }
  | { phase: 'failed'; failure: Failure; retry: RetryTarget | null }

export type VerifyInput =
  | { kind: 'contextLoaded'; context: VerifyContext }
  | { kind: 'transport'; value: TransportMode | null }
  | { kind: 'energy'; value: EnergySource | null }
  | { kind: 'submit' }
  | { kind: 'submitted'; outcome: VerifyOutcome }
  | { kind: 'failed'; failure: Failure }
  | { kind: 'retry' }

/** A page opened without a token cannot load anything, so it starts on "link not valid" and sends no request. */
export function initialVerifyState(token: string | null): VerifyState {
  return token === null || token.trim() === ''
    ? { phase: 'failed', failure: 'invalid', retry: null }
    : { phase: 'loading' }
}

function failed(failure: Failure, target: RetryTarget): VerifyState {
  return { phase: 'failed', failure, retry: RETRYABLE[failure] ? target : null }
}

/** The next state. Inputs that do not fit the current state (a late reply, a tap while sending) leave it unchanged. */
export function verifyReducer(state: VerifyState, input: VerifyInput): VerifyState {
  switch (input.kind) {
    case 'contextLoaded': {
      if (state.phase !== 'loading') return state
      const { context } = input
      const answers = {
        transport: preselect(offeredModes(context), context.defaultMode),
        energy: preselect(offeredEnergy(context), context.defaultEnergy),
      }
      return { phase: 'ready', context, answers }
    }
    case 'transport':
      if (state.phase !== 'ready') return state
      if (input.value !== null && !offeredModes(state.context).includes(input.value)) return state
      return { ...state, answers: { ...state.answers, transport: input.value } }
    case 'energy':
      if (state.phase !== 'ready') return state
      if (input.value !== null && !offeredEnergy(state.context).includes(input.value)) return state
      return { ...state, answers: { ...state.answers, energy: input.value } }
    case 'submit':
      return state.phase === 'ready' && canSubmit(state.answers) ? { ...state, phase: 'submitting' } : state
    case 'submitted':
      return state.phase === 'submitting'
        ? { phase: 'done', status: input.outcome.status, warnings: input.outcome.warnings }
        : state
    case 'failed':
      if (state.phase === 'loading') return failed(input.failure, { phase: 'loading' })
      if (state.phase === 'submitting') {
        return failed(input.failure, { phase: 'submitting', context: state.context, answers: state.answers })
      }
      return state
    case 'retry':
      return state.phase === 'failed' && state.retry !== null ? state.retry : state
  }
}

/** The "please reconfirm" lines for the answer's plausibility warnings, one per code, in the language shown. */
export function reconfirmLines(warnings: PlausibilityWarning[], lines: Record<PlausibilityCode, string>): string[] {
  return [...new Set(warnings.map((w) => w.code))].map((code) => lines[code])
}

/** The invoice date as the supplier reads it: "28 octombrie 2026" or "28 October 2026". */
export function formatIssueDate(isoDate: string, lang: Lang): string {
  const date = new Date(`${isoDate}T00:00:00Z`)
  if (Number.isNaN(date.getTime())) return isoDate
  return new Intl.DateTimeFormat(lang === 'ro' ? 'ro-RO' : 'en-GB', {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(date)
}
