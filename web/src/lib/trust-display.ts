import type { ClaimAssessment, NodeView, ProblemCode, TrustAssessment } from '@/api/client'
import { answersCopy, energySourceCopy, problemHeadlines, toolbarCopy, transportModeCopy, trustCopy } from '@/copy/dashboard'
import { CHECK_ANSWERS } from '@/copy/labels'
import { CO2E_UNIT, formatTonnesCo2e, formatTonnesGap } from '@/store/format'

// What the trust model (F7) shows, decided from the read model alone. The API computes the score, the levels, the
// counted figure and the kilograms at risk; nothing here weighs an answer or computes carbon.

/** The supplier list's search parameter, and its value for the answers to check. */
export const ANSWERS_FILTER = { param: 'filter', value: 'answers' } as const

/** Where the Overview's counter leads: the supplier list, filtered to the answers to check. */
export const ANSWERS_LIST_PATH = `/suppliers?${ANSWERS_FILTER.param}=${ANSWERS_FILTER.value}`

/** Whether the node's supplier answers wait for a person: the API sets `needsReview`. */
export function needsAnswerCheck(node: Pick<NodeView, 'trust'>): boolean {
  return node.trust?.needsReview === true
}

/** "Check answers" while the answers wait for a person, else null. */
export function answerCheckLabel(node: Pick<NodeView, 'trust'>): string | null {
  return needsAnswerCheck(node) ? CHECK_ANSWERS : null
}

/** The nodes whose answers wait for a person, the most kilograms at risk first, then the largest figure. */
export function answersToCheck<T extends Pick<NodeView, 'trust' | 'carbonKg'>>(nodes: readonly T[]): T[] {
  return nodes
    .filter(needsAnswerCheck)
    .sort((a, b) => (b.trust?.kgAtRisk ?? 0) - (a.trust?.kgAtRisk ?? 0) || b.carbonKg - a.carbonKg)
}

/** The answer as a reader says it: "Sea", "Renewable electricity", "2.3 t". */
export function claimValueLabel(claim: Pick<ClaimAssessment, 'kind' | 'value'>): string {
  if (claim.kind === 'mode' && claim.value in transportModeCopy) return transportModeCopy[claim.value as keyof typeof transportModeCopy]
  if (claim.kind === 'energy' && claim.value in energySourceCopy) return energySourceCopy[claim.value as keyof typeof energySourceCopy]
  return trustCopy.massValue(claim.value)
}

/**
 * A person may decide an answer that something contradicts, or change a decision already made, whatever the node's
 * status (a rejection kept through a partial answer must stay clearable); and an answer that lowers the figure once the
 * node is VERIFIED. On a partial answer that lowers it, a decision would change no figure, so none is offered.
 */
export function canDecide(
  claim: Pick<ClaimAssessment, 'reduces' | 'level' | 'review'>,
  status: NodeView['status'] = 'VERIFIED',
): boolean {
  return claim.level === 'contradicted' || claim.review !== null || (status === 'VERIFIED' && claim.reduces)
}

/**
 * The line under the score: for a partial answer, that the estimate counts; else the tonnes at risk, or that every
 * claimed reduction is backed.
 */
export function atRiskLine(
  trust: Pick<TrustAssessment, 'kgAtRisk'>,
  status: NodeView['status'] = 'VERIFIED',
  figures?: Pick<NodeView['figures'], 'countedKg' | 'claimedKg'>,
): string {
  if (status !== 'VERIFIED') return trustCopy.partial
  if (trust.kgAtRisk <= 0) return trustCopy.nothingAtRisk
  // Beside the counted and answered tiles, the gap is worked out from them as shown, so the three agree on screen.
  const gap =
    figures?.countedKg != null && figures.claimedKg != null
      ? `${formatTonnesGap(figures.countedKg, figures.claimedKg)} ${CO2E_UNIT}`
      : formatTonnesCo2e(trust.kgAtRisk)
  return trustCopy.atRisk(gap)
}

/** The Overview counter's body: the API's tonnes at stake (`kpis.kgAtRisk`), or why the answers are listed when none are. */
export function answersBody(kgAtRisk: number): string {
  return kgAtRisk > 0 ? answersCopy.countBody(formatTonnesCo2e(kgAtRisk)) : answersCopy.countBodyNoRisk
}

/**
 * What the store shows of a node's trust, as one value. A write the API accepted reaches the store with `node.updated`;
 * a control waits until this value moves on, so it never shows a figure the store does not have.
 */
export function trustStamp(node: Pick<NodeView, 'trust' | 'evidence' | 'figures'>): string {
  return JSON.stringify([
    node.trust?.claims.map((c) => [c.kind, c.value, c.level, c.review]) ?? null,
    node.evidence,
    node.figures.target,
  ])
}

/** Why a trust write was refused: `changed` (409), `invalid` (422), `gone` (404), `offline` (no answer), or `other`. */
export type TrustFailure = { kind: 'changed' | 'invalid' | 'gone' | 'offline' } | { kind: 'other'; status: number }

/** The failure for a refused write, by the ApiProblem `code` first and the HTTP status after it. */
export function trustFailureFor(status: number | null, code: ProblemCode | null): TrustFailure {
  if (status === null) return { kind: 'offline' }
  if (code === 'ClaimChanged' || (code === null && status === 409)) return { kind: 'changed' }
  if (code === 'NodeNotFound' || (code === null && status === 404)) return { kind: 'gone' }
  if (status === 422) return { kind: 'invalid' }
  return { kind: 'other', status }
}

/** The text for a refused write; the API's detail, when there is one, is shown after it. */
export function trustFailureMessage(failure: TrustFailure): string {
  switch (failure.kind) {
    case 'changed':
      return trustCopy.changed
    case 'invalid':
      return trustCopy.invalid
    case 'gone':
      return problemHeadlines.NodeNotFound
    case 'offline':
      return toolbarCopy.unreachable
    case 'other':
      return toolbarCopy.httpError(failure.status)
  }
}

/** Whether the store should be refreshed from a snapshot after this failure: the node moved on, or left the graph. */
export function refetchAfterTrust(failure: TrustFailure): boolean {
  return failure.kind === 'changed' || failure.kind === 'gone'
}
