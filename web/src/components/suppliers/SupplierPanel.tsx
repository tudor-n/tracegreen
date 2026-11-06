import { ListChecksIcon, SmartphoneIcon, XIcon } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import type { CarbonFigure, NodeView, RequestLinkResponse } from '@/api/client'
import { LinkCard } from '@/components/dashboard/LinkCard'
import { SimulatorPanel } from '@/components/dashboard/SimulatorPanel'
import { Chip, type ChipTone, Group, GroupRow } from '@/components/shell/Page'
import { Button } from '@/components/ui/button'
import { drawerCopy, energySourceCopy, nodeCopy, suppliersCopy, transportModeCopy } from '@/copy/dashboard'
import { AI_MAPPED, ILLUSTRATIVE, uncertaintyLabel } from '@/copy/labels'
import { requestLink, requestPortalLink } from '@/lib/dashboard-actions'
import { aiMappedLabel, aiReason, isPlaceholder, provenanceLine, shownFigure } from '@/lib/node-display'
import { cn } from '@/lib/utils'
import {
  CO2E_UNIT,
  formatDate,
  formatDays,
  formatEuros,
  formatFactorValue,
  formatKm,
  formatTonnes,
  formatTonnesCo2e,
  useGraphStore,
} from '@/store'

// The side panel for the selected supplier (v2 design). Four tabs give each kind of information its own place:
// Summary (the figure, its provenance and the one action), Details (invoice and lane), Factors (the rows behind the
// figure) and What if (the simulator, B5). Every number comes from the API as it is. The links belong to the node they
// were asked for: the panel body is keyed by node (never by tab), so another node starts without them, switching tabs
// keeps them and any request still on its way, and the tokens never leave this component's state. Escape or the close
// button closes the panel, unless the Escape already closed something inside it (a tooltip); a reset that removes the
// node closes it too (hydrateState). Frozen test hooks (RUNBOOK): `node-drawer`, `drawer-request`, `qr-url`, `drawer-simulate`;
// `drawer-portal` and `portal-url` for the supplier page link (F12), which lists every delivery from this supplier.

type Tab = 'summary' | 'details' | 'factors' | 'whatIf'

const statusTone: Record<NodeView['status'], ChipTone> = {
  ESTIMATED: 'estimated',
  PARTIAL: 'estimated',
  VERIFIED: 'verified',
}

export function SupplierPanel({ node, hotspot }: { node: NodeView; hotspot: boolean }) {
  const select = useGraphStore((s) => s.select)
  const ai = aiMappedLabel(node.classification)
  const panel = useRef<HTMLElement>(null)

  // Below the lg breakpoint the panel sits under the tree; bring it into view when another supplier is picked.
  useEffect(() => {
    if (window.matchMedia('(max-width: 1023px)').matches) panel.current?.scrollIntoView({ block: 'start', behavior: 'smooth' })
  }, [node.id])

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !event.defaultPrevented) select(null)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [select])

  return (
    <aside
      ref={panel}
      data-testid="node-drawer"
      aria-label={node.label}
      className="flex w-full flex-col gap-5 bg-card px-6 pt-6 pb-8 shadow-[0_-0.5px_0_var(--hairline)] motion-safe:animate-in motion-safe:fade-in-0 motion-safe:slide-in-from-right-6 motion-safe:duration-500 lg:sticky lg:top-0 lg:h-dvh lg:w-[360px] lg:shrink-0 lg:overflow-y-auto lg:shadow-[-0.5px_0_0_var(--hairline),-18px_0_40px_-30px_rgb(0_0_0/0.35)]"
    >
      <div className="flex items-start gap-3">
        <div className="flex min-w-0 flex-1 flex-col gap-1.5">
          <h2 className="font-display text-[22px] leading-tight font-semibold tracking-[-0.02em]">{node.label}</h2>
          <p className="text-[13px] text-muted-foreground">
            {node.classification.categoryLabel} · {node.lane.origin}
          </p>
        </div>
        <button
          type="button"
          aria-label={suppliersCopy.close}
          onClick={() => select(null)}
          className="inline-flex size-[30px] shrink-0 items-center justify-center rounded-full bg-fill text-ink-2 transition-colors hover:bg-fill-strong"
        >
          <XIcon className="size-3.5" strokeWidth={2.6} aria-hidden />
        </button>
      </div>
      <div className="-mt-2 flex flex-wrap gap-1.5">
        <span data-testid="drawer-status" data-status={node.status}>
          <Chip tone={statusTone[node.status]}>{nodeCopy.status[node.status]}</Chip>
        </span>
        {hotspot && <Chip tone="hotspot">{nodeCopy.hotspot}</Chip>}
        {ai !== null && <Chip>{ai}</Chip>}
      </div>
      <PanelBody key={node.id} node={node} />
    </aside>
  )
}

function PanelBody({ node }: { node: NodeView }) {
  const simulating = useGraphStore((s) => s.openPanels.simulator)
  const setPanel = useGraphStore((s) => s.setPanel)
  const [tab, setTab] = useState<Exclude<Tab, 'whatIf'>>('summary')
  const shown: Tab = simulating ? 'whatIf' : tab
  const [links, setLinks] = useState<Links>({ verify: null, portal: null })
  const [requesting, setRequesting] = useState<LinkKind | null>(null)
  const [error, setError] = useState<string | null>(null)

  const onRequest = async (kind: LinkKind) => {
    setRequesting(kind)
    setError(null)
    const answer = kind === 'verify' ? await requestLink(node.id) : await requestPortalLink(node.id)
    if (answer.ok) setLinks((l) => ({ ...l, [kind]: answer.data }))
    else setError(answer.message)
    setRequesting(null)
  }

  const pick = (next: Tab) => {
    // The What if tab is the simulator: leaving it takes the ghost row off the tree.
    setPanel('simulator', next === 'whatIf')
    if (next !== 'whatIf') setTab(next)
  }

  const tabs: { id: Tab; label: string; testId?: string }[] = [
    { id: 'summary', label: suppliersCopy.tabs.summary },
    { id: 'details', label: suppliersCopy.tabs.details },
    { id: 'factors', label: suppliersCopy.tabs.factors },
    { id: 'whatIf', label: suppliersCopy.tabs.whatIf, testId: 'drawer-simulate' },
  ]

  return (
    <>
      <div role="tablist" aria-label={suppliersCopy.tabsLabel} className="flex rounded-full bg-[#e9e9ed] p-0.5">
        {tabs.map((t) => (
          <button
            key={t.id}
            type="button"
            role="tab"
            aria-selected={shown === t.id}
            data-testid={t.testId}
            onClick={() => pick(t.id)}
            className={cn(
              'h-[30px] flex-1 rounded-full px-2 text-[13px] font-medium whitespace-nowrap transition-[background-color,box-shadow] duration-200',
              shown === t.id ? 'bg-card shadow-[0_0_0_0.5px_rgb(0_0_0/0.08),0_2px_6px_-2px_rgb(0_0_0/0.2)]' : 'text-ink-2',
            )}
          >
            {t.label}
          </button>
        ))}
      </div>
      <div role="tabpanel" className="flex flex-col gap-5 motion-safe:animate-in motion-safe:fade-in-0 motion-safe:duration-300" key={shown}>
        {shown === 'summary' && (
          <Summary node={node} links={links} requesting={requesting} error={error} onRequest={(kind) => void onRequest(kind)} />
        )}
        {shown === 'details' && <Details node={node} />}
        {shown === 'factors' && <Factors node={node} />}
        {shown === 'whatIf' && <SimulatorPanel node={node} />}
      </div>
    </>
  )
}

type LinkKind = 'verify' | 'portal'
/** The links asked for this supplier, kept above the tabs so switching tabs keeps them. Tokens stay in this state. */
type Links = Record<LinkKind, RequestLinkResponse | null>

interface SummaryProps {
  node: NodeView
  links: Links
  /** Which link is on its way; both buttons wait while one is. */
  requesting: LinkKind | null
  error: string | null
  onRequest: (kind: LinkKind) => void
}

function Summary({ node, links, requesting, error, onRequest }: SummaryProps) {
  const figure = shownFigure(node)
  const provenance = provenanceLine(node)
  const answered = node.transportMode !== null || node.energySource !== null

  return (
    <>
      <div className="flex flex-col gap-2">
        <p className="flex items-baseline gap-1.5 font-display leading-none">
          <span className="text-[40px] font-semibold tracking-[-0.03em]">{formatTonnes(node.carbonKg)}</span>
          <span className="text-[13px] font-medium text-muted-foreground">{CO2E_UNIT}</span>
        </p>
        {provenance !== null && (
          <p data-testid="drawer-provenance" className="text-[13px] leading-relaxed break-words text-ink-2">
            {provenance}
          </p>
        )}
        <p className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
          <span data-testid="drawer-uncertainty" data-uncertainty={node.uncertainty}>
            {uncertaintyLabel[node.uncertainty]}
          </span>
          {figure.factors.some(isPlaceholder) && <Chip tone="estimated">{nodeCopy.placeholder}</Chip>}
        </p>
        {figure !== node.spend && (
          <p className="text-xs text-muted-foreground">
            {drawerCopy.spendBefore}: {formatTonnesCo2e(node.spend.kg)}
          </p>
        )}
      </div>

      {answered && (
        <Group title={drawerCopy.answers}>
          <GroupRow label={drawerCopy.transport}>
            {node.transportMode === null ? drawerCopy.skipped : transportModeCopy[node.transportMode]}
          </GroupRow>
          <GroupRow label={drawerCopy.energy}>
            {node.energySource === null ? drawerCopy.skipped : energySourceCopy[node.energySource]}
          </GroupRow>
        </Group>
      )}

      {links.verify !== null && <LinkCard link={links.verify} />}
      {links.portal !== null && <LinkCard link={links.portal} kind="portal" />}
      {/* Both buttons stay: a link is single-use or expires, so the buyer can always ask for a fresh one here. */}
      <div className="flex flex-col gap-3 rounded-2xl bg-grouped p-[18px]">
        <h3 className="text-[15px] font-semibold">{suppliersCopy.askTitle}</h3>
        <p className="text-[13px] leading-relaxed text-ink-2">{suppliersCopy.askBody}</p>
        <Button
          size="lg"
          variant={links.verify === null ? 'default' : 'outline'}
          disabled={requesting !== null}
          data-testid="drawer-request"
          onClick={() => onRequest('verify')}
        >
          <SmartphoneIcon data-icon="inline-start" aria-hidden />
          {requesting === 'verify' ? drawerCopy.requesting : links.verify === null ? drawerCopy.requestData : drawerCopy.requestAgain}
        </Button>
        <Button variant="outline" size="lg" disabled={requesting !== null} data-testid="drawer-portal" onClick={() => onRequest('portal')}>
          <ListChecksIcon data-icon="inline-start" aria-hidden />
          {requesting === 'portal'
            ? drawerCopy.supplierPageRequesting
            : links.portal === null
              ? drawerCopy.supplierPage
              : drawerCopy.supplierPageAgain}
        </Button>
        {error !== null && (
          <p role="alert" data-testid="drawer-request-error" className="text-xs text-destructive">
            {error}
          </p>
        )}
      </div>
    </>
  )
}

function Details({ node }: { node: NodeView }) {
  const reason = aiReason(node.classification)
  return (
    <>
      <Group title={drawerCopy.invoice}>
        <GroupRow label={drawerCopy.invoiceNumber}>{node.invoice.number}</GroupRow>
        <GroupRow label={drawerCopy.issued}>{formatDate(node.invoice.issueDate)}</GroupRow>
        <GroupRow label={drawerCopy.spend}>{formatEuros(node.spendEur)}</GroupRow>
        <GroupRow label={drawerCopy.classification}>{node.classification.categoryLabel}</GroupRow>
      </Group>
      <Group title={drawerCopy.invoiceLine}>
        {/* Invoice text is data: rendered as text, clamped (padding outside the clamp, so no fourth line peeks), in full on hover. */}
        <div className="py-3">
          <p className="line-clamp-3 text-sm break-words" title={node.classification.lineDescription}>
            {node.classification.lineDescription}
          </p>
        </div>
      </Group>
      {reason !== null && (
        <Group title={drawerCopy.whyCategory} tag={AI_MAPPED}>
          {/* The model's own words: rendered as text, clamped, and in full on hover. */}
          <div className="py-3">
            <p className="line-clamp-3 text-sm break-words" title={reason}>
              {reason}
            </p>
          </div>
        </Group>
      )}
      <Group title={drawerCopy.lane} tag={ILLUSTRATIVE}>
        <GroupRow label={drawerCopy.route}>
          {node.lane.origin} → {node.lane.dest}
        </GroupRow>
        <GroupRow label={drawerCopy.mode}>{transportModeCopy[node.lane.mode]}</GroupRow>
        <GroupRow label={drawerCopy.distance}>{formatKm(node.lane.distanceKm)}</GroupRow>
        <GroupRow label={drawerCopy.cost} note={ILLUSTRATIVE}>
          <span data-testid="drawer-cost">{formatEuros(node.costEur)}</span>
        </GroupRow>
        <GroupRow label={drawerCopy.leadTime} note={ILLUSTRATIVE}>
          <span data-testid="drawer-lead-time">{formatDays(node.leadDays)}</span>
        </GroupRow>
      </Group>
    </>
  )
}

/**
 * The rows behind the figure on screen and, once a supplier confirmed, the rows behind the spend-based estimate it
 * replaced, so every figure the panel shows (Summary shows both) names its source, year and row label.
 */
function Factors({ node }: { node: NodeView }) {
  const figure = shownFigure(node)
  return (
    <>
      <FactorGroup title={suppliersCopy.factorsIntro(formatTonnesCo2e(node.carbonKg))} factors={figure.factors} />
      {figure !== node.spend && (
        <FactorGroup title={suppliersCopy.factorsBefore(formatTonnesCo2e(node.spend.kg))} factors={node.spend.factors} />
      )}
    </>
  )
}

function FactorGroup({ title, factors }: { title: string; factors: CarbonFigure['factors'] }) {
  return (
    <Group title={title}>
      {factors.length === 0 ? (
        <p className="py-3 text-sm text-muted-foreground">{nodeCopy.noFactors}</p>
      ) : (
        factors.map((factor, i) => (
          <div key={i} className="flex flex-col gap-1 py-3.5">
            <span className="flex flex-wrap items-center gap-2">
              <span className="text-sm font-semibold">
                {formatFactorValue(factor.value)} {factor.unit}
              </span>
              {isPlaceholder(factor) && <Chip tone="estimated">{nodeCopy.placeholder}</Chip>}
            </span>
            <span className="text-xs leading-snug text-ink-2">{factor.rowLabel}</span>
            <span className="text-xs leading-snug text-muted-foreground">
              {factor.source} · {factor.year}
            </span>
          </div>
        ))
      )}
    </Group>
  )
}
