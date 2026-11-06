import { CheckIcon, ChevronDownIcon, CircleAlertIcon, SparklesIcon, XIcon } from 'lucide-react'
import { useId, useState } from 'react'
import type { ClaimAssessment, NodeView, TransportMode } from '@/api/client'
import { Chip, type ChipTone, Group, GroupRow, Tag } from '@/components/shell/Page'
import { Button } from '@/components/ui/button'
import { evidenceCopy, transportModeCopy, trustCopy } from '@/copy/dashboard'
import { useDemoTools } from '@/hooks/useDemoTools'
import { demoRecords } from '@/lib/demo-samples'
import { trustApi } from '@/lib/trust-api'
import { atRiskLine, canDecide, claimValueLabel } from '@/lib/trust-display'
import { cn } from '@/lib/utils'
import { formatDate } from '@/store'
import { useTrustWrite } from './useTrustWrite'

// How far a node's supplier answers can be believed (F7), as one card in the side panel: the API's band and score, the
// tonnes of claimed reductions nothing backs yet, each answer with its level, its main reason, the proof the supplier
// gave and Accept / Reject, then the buyer's own records (received on, customs) inline, with "Fill demo values" while
// the API runs with demo tools. Why the score lost points is one click away. Nothing here weighs an answer: every level
// and number is the API's. Test hooks: `trust-control` with `data-band` and `data-score`, `trust-at-risk`,
// `trust-claim-<kind>` with `data-level`, `trust-accept-<kind>`, `trust-reject-<kind>`, `trust-why`,
// `trust-signal-<code>`, `trust-error`; the records: `records-open`, `records-demo`, `records-received`,
// `records-border`, `records-mrn`, `records-save`, `records-error`.

const levelTone: Record<ClaimAssessment['level'], ChipTone> = {
  contradicted: 'hotspot',
  unproven: 'estimated',
  documented: 'neutral',
  corroborated: 'verified',
  reviewed: 'verified',
  notNeeded: 'neutral',
}

const bandTone: Record<NonNullable<NodeView['trust']>['band'], ChipTone> = {
  strong: 'verified',
  moderate: 'neutral',
  review: 'estimated',
}

export function TrustReview({ node }: { node: NodeView }) {
  const trust = node.trust
  const titleId = useId()
  const [why, setWhy] = useState(false)
  const { phase, busy, run } = useTrustWrite(node)
  if (trust === null) return null

  const decide = (claim: ClaimAssessment, decision: 'accepted' | 'rejected') =>
    void run(() => trustApi.reviewClaim(node.id, claim.kind, decision, claim.value))

  return (
    <section
      aria-labelledby={titleId}
      data-testid="trust-control"
      data-band={trust.band}
      data-score={trust.score}
      className={cn('flex flex-col gap-3 rounded-2xl p-[18px]', trust.needsReview ? 'bg-estimated-tint' : 'bg-grouped')}
    >
      <div className="flex items-start justify-between gap-3">
        <h3 id={titleId} className="min-w-0 flex-1 text-[15px] leading-snug font-semibold">
          {trustCopy.titleShort}
        </h3>
        <span className="flex shrink-0 items-center gap-2">
          <Chip tone={bandTone[trust.band]}>{trustCopy.band[trust.band]}</Chip>
          <span className="text-sm font-semibold tabular-nums">{trustCopy.scoreShort(trust.score)}</span>
        </span>
      </div>
      <p data-testid="trust-at-risk" className="-mt-1 text-[13px] text-ink-2">
        {atRiskLine(trust, node.status, node.figures)}
      </p>

      <ul aria-label={trustCopy.claims} className="flex flex-col gap-2">
        {trust.claims.map((claim) => (
          <li
            key={claim.kind}
            data-testid={`trust-claim-${claim.kind}`}
            data-level={claim.level}
            className="flex flex-col gap-1.5 rounded-[14px] bg-card p-3 shadow-[0_0_0_0.5px_var(--hairline)]"
          >
            <span className="flex flex-wrap items-center justify-between gap-2">
              <span className="text-sm">
                <span className="text-ink-2">{trustCopy.claimKind[claim.kind]}</span>{' '}
                <span className="font-semibold">{claimValueLabel(claim)}</span>
              </span>
              <Chip tone={levelTone[claim.level]}>
                {claim.level === 'contradicted' && <CircleAlertIcon className="size-3.5" strokeWidth={2.4} aria-hidden />}
                {trustCopy.level[claim.level]}
              </Chip>
            </span>
            <Proof node={node} claim={claim} />
            {claim.reasons[0] !== undefined && (
              // The detail carries the figures behind the reason (days, the document number); it is the API's text.
              <p className="text-xs leading-snug text-ink-2" title={claim.reasons.map((r) => r.detail).join('\n')}>
                {trustCopy.reason[mainReason(claim).code]}
              </p>
            )}
            {canDecide(claim, node.status) && (
              <span className="flex gap-2 pt-0.5">
                <Button
                  size="sm"
                  variant={claim.review === 'accepted' ? 'default' : 'outline'}
                  data-testid={`trust-accept-${claim.kind}`}
                  title={trustCopy.acceptHelp}
                  disabled={busy || claim.review === 'accepted'}
                  onClick={() => decide(claim, 'accepted')}
                >
                  <CheckIcon data-icon="inline-start" aria-hidden />
                  {trustCopy.accept}
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  data-testid={`trust-reject-${claim.kind}`}
                  title={trustCopy.rejectHelp}
                  disabled={busy || claim.review === 'rejected'}
                  onClick={() => decide(claim, 'rejected')}
                >
                  <XIcon data-icon="inline-start" aria-hidden />
                  {trustCopy.reject}
                </Button>
              </span>
            )}
          </li>
        ))}
      </ul>

      <BuyerRecords node={node} />

      {trust.signals.length > 0 && (
        <div className="flex flex-col gap-1.5">
          <button
            type="button"
            data-testid="trust-why"
            aria-expanded={why}
            onClick={() => setWhy((v) => !v)}
            className="flex items-center gap-1 self-start text-xs font-medium text-ink-2 hover:text-foreground"
          >
            {trustCopy.whyScore}
            <ChevronDownIcon className={cn('size-3.5 transition-transform', why && 'rotate-180')} aria-hidden />
          </button>
          {why && (
            <ul className="flex flex-col gap-1">
              {trust.signals.map((signal) => (
                <li key={signal.code} data-testid={`trust-signal-${signal.code}`} className="flex justify-between gap-3 text-xs" title={signal.detail}>
                  <span>{trustCopy.signal[signal.code]}</span>
                  {signal.points > 0 && <span className="shrink-0 font-semibold tabular-nums">{trustCopy.points(signal.points)}</span>}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      <p role="status" className="text-xs text-ink-2 empty:hidden">
        {busy ? trustCopy.saving : phase.kind === 'saved' ? trustCopy.saved : ''}
      </p>
      {phase.kind === 'failed' && (
        <p role="alert" data-testid="trust-error" className="text-xs text-destructive">
          {phase.message}
        </p>
      )}
    </section>
  )
}

/** The reason to show first: a person's decision, then a contradiction, then what backs it, then why nothing does. */
function mainReason(claim: ClaimAssessment) {
  const order = [
    'ACCEPTED_BY_BUYER',
    'REJECTED_BY_BUYER',
    'CUSTOMS_DISAGREES',
    'TRANSIT_TOO_FAST',
    'DOCUMENT_MODE_MISMATCH',
    'MASS_BELOW_INVOICE',
    'CUSTOMS_AGREES',
    'TRANSIT_FITS',
  ]
  return [...claim.reasons].sort((a, b) => rank(order, a.code) - rank(order, b.code))[0]
}

function rank(order: string[], code: string): number {
  const i = order.indexOf(code)
  return i < 0 ? order.length : i
}

/** The supplier's proof for this answer, as text: the shipping document for the mode, the certificate for the power. */
function Proof({ node, claim }: { node: NodeView; claim: ClaimAssessment }) {
  const { evidence } = node
  let text: string | null = null
  if (claim.kind === 'mode' && evidence.shippingDocType !== null) {
    text = `${evidenceCopy.docType[evidence.shippingDocType]} ${evidence.shippingDocId ?? ''}`
  } else if (claim.kind === 'energy' && evidence.energyCertType !== null) {
    text = `${evidenceCopy.certType[evidence.energyCertType]} ${evidence.energyCertId ?? ''}`
  }
  if (text === null) return null
  return <p className="font-mono text-xs break-all text-foreground">{text}</p>
}

/**
 * The buyer's own records of the delivery, inside the card: the goods-receipt date and, for an import, the customs
 * declaration's mode at the border and MRN. "Fill demo values" (demo tools only) puts in a receipt one lead time after
 * dispatch, the answered mode at the border and a demo MRN, so the transport answer matches; pick another border mode
 * to show a contradiction.
 */
function BuyerRecords({ node }: { node: NodeView }) {
  const { evidence } = node
  const recorded = evidence.receivedOn !== null || evidence.customsBorderMode !== null || evidence.customsMrn !== null
  const [open, setOpen] = useState(false)
  const [receivedOn, setReceivedOn] = useState(evidence.receivedOn ?? '')
  const [border, setBorder] = useState<TransportMode | ''>(evidence.customsBorderMode ?? '')
  const [mrn, setMrn] = useState(evidence.customsMrn ?? '')
  const [demo, setDemo] = useState(false)
  const demoTools = useDemoTools()
  const { phase, busy, run } = useTrustWrite(node)
  const ids = { received: useId(), border: useId(), mrn: useId() }
  const field = 'h-9 w-full rounded-lg bg-card px-2 text-sm text-foreground shadow-[0_0_0_0.5px_var(--hairline)]'
  const mode = node.transportMode ?? node.lane.mode

  const fill = () => {
    const values = demoRecords(mode, node.leadDays, evidence.dispatchDate, node.invoice.issueDate, new Date().toISOString().slice(0, 10))
    setReceivedOn(values.receivedOn)
    setBorder(values.customsBorderMode)
    setMrn(values.customsMrn)
    setDemo(true)
  }

  const save = () =>
    void run(() =>
      trustApi.recordReceipt(node.id, {
        receivedOn: receivedOn === '' ? null : receivedOn,
        customsBorderMode: border === '' ? null : border,
        customsMrn: mrn.trim() === '' ? null : mrn.trim(),
      }),
    )

  if (!open) {
    return (
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs text-ink-2">
          {recorded
            ? evidenceCopy.recordedLine(
                evidence.receivedOn === null ? null : formatDate(evidence.receivedOn),
                evidence.customsBorderMode === null ? null : transportModeCopy[evidence.customsBorderMode],
              )
            : evidenceCopy.recordsPrompt}
        </p>
        <Button size="sm" variant="outline" data-testid="records-open" onClick={() => setOpen(true)}>
          {recorded ? evidenceCopy.editRecords : evidenceCopy.addRecords}
        </Button>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-2.5 rounded-[14px] bg-card p-3 shadow-[0_0_0_0.5px_var(--hairline)]">
      <div className="flex items-center justify-between gap-2">
        <p className="text-[13px] font-semibold">{evidenceCopy.records}</p>
        {demo && <Tag tone="warning">{evidenceCopy.demoTag}</Tag>}
      </div>
      <div className="grid grid-cols-2 gap-2">
        <label htmlFor={ids.received} className="flex flex-col gap-1 text-xs text-ink-2">
          {evidenceCopy.receivedOnShort}
          <input id={ids.received} type="date" data-testid="records-received" value={receivedOn} onChange={(e) => setReceivedOn(e.target.value)} className={field} />
        </label>
        <label htmlFor={ids.border} className="flex flex-col gap-1 text-xs text-ink-2">
          {evidenceCopy.borderModeShort}
          <select id={ids.border} data-testid="records-border" value={border} onChange={(e) => setBorder(e.target.value as TransportMode | '')} className={field}>
            <option value="">{evidenceCopy.borderNone}</option>
            {(['sea', 'air', 'rail', 'road'] as const).map((m) => (
              <option key={m} value={m}>
                {transportModeCopy[m]}
              </option>
            ))}
          </select>
        </label>
      </div>
      <label htmlFor={ids.mrn} className="flex flex-col gap-1 text-xs text-ink-2">
        {evidenceCopy.mrn}
        <input
          id={ids.mrn}
          type="text"
          data-testid="records-mrn"
          value={mrn}
          maxLength={24}
          autoComplete="off"
          spellCheck={false}
          onChange={(e) => setMrn(e.target.value)}
          className={cn(field, 'font-mono uppercase')}
        />
      </label>
      <div className="flex flex-wrap items-center gap-2">
        {demoTools && (
          <Button size="sm" variant="outline" data-testid="records-demo" disabled={busy} onClick={fill}>
            <SparklesIcon data-icon="inline-start" aria-hidden />
            {evidenceCopy.fillDemo}
          </Button>
        )}
        <Button size="sm" data-testid="records-save" disabled={busy} onClick={save}>
          {evidenceCopy.save}
        </Button>
        <Button size="sm" variant="ghost" disabled={busy} onClick={() => setOpen(false)}>
          {evidenceCopy.close}
        </Button>
      </div>
      <p role="status" className="text-xs text-ink-2 empty:hidden">
        {busy ? trustCopy.saving : phase.kind === 'saved' ? trustCopy.saved : ''}
      </p>
      {phase.kind === 'failed' && (
        <p role="alert" data-testid="records-error" className="text-xs text-destructive">
          {phase.message}
        </p>
      )}
    </div>
  )
}

/** What the supplier gave to back the answers, on the Details tab; rendered as text, as normalised by the API. */
export function EvidenceGroup({ node }: { node: NodeView }) {
  const { evidence } = node
  const none = <span className="text-muted-foreground">{evidenceCopy.none}</span>
  return (
    <Group title={evidenceCopy.title}>
      <GroupRow label={evidenceCopy.shippingDoc}>
        {evidence.shippingDocType === null ? none : <span className="break-all">{`${evidenceCopy.docType[evidence.shippingDocType]} ${evidence.shippingDocId ?? ''}`}</span>}
      </GroupRow>
      <GroupRow label={evidenceCopy.dispatchDate}>{evidence.dispatchDate === null ? none : formatDate(evidence.dispatchDate)}</GroupRow>
      <GroupRow label={evidenceCopy.certificate}>
        {evidence.energyCertType === null ? none : <span className="break-all">{`${evidenceCopy.certType[evidence.energyCertType]} ${evidence.energyCertId ?? ''}`}</span>}
      </GroupRow>
      <GroupRow label={evidenceCopy.receivedOn}>{evidence.receivedOn === null ? none : formatDate(evidence.receivedOn)}</GroupRow>
      <GroupRow label={evidenceCopy.borderMode}>{evidence.customsBorderMode === null ? none : transportModeCopy[evidence.customsBorderMode]}</GroupRow>
      <GroupRow label={evidenceCopy.mrn}>{evidence.customsMrn === null ? none : <span className="font-mono break-all">{evidence.customsMrn}</span>}</GroupRow>
    </Group>
  )
}
