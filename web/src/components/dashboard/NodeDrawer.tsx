import { useState, type ReactNode } from 'react'
import type { NodeStatus, NodeView, RequestLinkResponse } from '@/api/client'
import { LinkCard } from '@/components/dashboard/LinkCard'
import { FactorInfo } from '@/components/graph/FactorInfo'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { drawerCopy, energySourceCopy, nodeCopy, transportModeCopy } from '@/copy/dashboard'
import { ILLUSTRATIVE, uncertaintyLabel } from '@/copy/labels'
import { requestLink } from '@/lib/dashboard-actions'
import { aiMappedLabel, isPlaceholder, provenanceLine, shownFigure } from '@/lib/node-display'
import { cn } from '@/lib/utils'
import {
  formatDate,
  formatDays,
  formatEuros,
  formatKm,
  formatTonnesCo2e,
  selectSelectedNode,
  useGraphStore,
} from '@/store'

// The selected supplier in full: status, ruling-3 provenance line, ruling-4 uncertainty, every number with its "i",
// and the lane figures labelled illustrative. It is mounted inside the graph area (`container`), so the toolbar and
// the KPI bar stay visible and usable above it. Non-modal and without an overlay, so the canvas stays live beside it:
// clicking another node shows that one, clicking the pane, the close button or Escape closes it, and a reset that
// removes the node closes it too (hydrateState). Every number comes from the API as it is; nothing is computed here.
// "Request data" asks for a verification link and shows it in the QR card at the top; the link belongs to the node it
// was asked for, so another node starts without one. "Simulate" (B5) stays disabled until its endpoint exists.

const statusDot: Record<NodeStatus, string> = {
  ESTIMATED: 'bg-(--status-estimated)',
  PARTIAL: 'bg-[linear-gradient(135deg,var(--status-partial-a)_50%,var(--status-partial-b)_50%)]',
  VERIFIED: 'bg-(--status-verified)',
}

export function NodeDrawer({ container }: { container: HTMLElement }) {
  const selected = useGraphStore(selectSelectedNode)
  const open = useGraphStore((s) => s.openPanels.drawer) && selected !== null
  const select = useGraphStore((s) => s.select)
  // Keeps the last node on screen while the drawer slides out, after the selection is already gone.
  const [shown, setShown] = useState<NodeView | null>(selected)
  if (selected !== null && selected !== shown) setShown(selected)

  return (
    <Sheet
      open={open}
      modal={false}
      disablePointerDismissal
      onOpenChange={(next) => {
        if (!next) select(null)
      }}
    >
      <SheetContent container={container} showOverlay={false} data-testid="node-drawer" className="absolute gap-0">
        {shown !== null && <DrawerBody key={shown.id} node={shown} />}
      </SheetContent>
    </Sheet>
  )
}

function DrawerBody({ node }: { node: NodeView }) {
  const figure = shownFigure(node)
  const provenance = provenanceLine(node)
  const ai = aiMappedLabel(node.classification)
  const answered = node.transportMode !== null || node.energySource !== null
  const [link, setLink] = useState<RequestLinkResponse | null>(null)
  const [requesting, setRequesting] = useState(false)
  const [linkError, setLinkError] = useState<string | null>(null)

  const onRequest = async () => {
    setRequesting(true)
    setLinkError(null)
    const answer = await requestLink(node.id)
    if (answer.ok) setLink(answer.data)
    else setLinkError(answer.message)
    setRequesting(false)
  }

  return (
    <>
      <SheetHeader className="border-b pr-12">
        <SheetTitle>{node.label}</SheetTitle>
        <SheetDescription>
          {node.country} · {node.classification.categoryLabel}
        </SheetDescription>
        <p data-testid="drawer-status" data-status={node.status} className="mt-1 flex items-center gap-1.5 text-xs font-medium">
          <span aria-hidden className={cn('size-2.5 rounded-full', statusDot[node.status])} />
          {nodeCopy.status[node.status]}
        </p>
      </SheetHeader>

      <div className="flex min-h-0 flex-1 flex-col gap-5 overflow-y-auto p-4">
        {link !== null && <LinkCard link={link} />}

        <Section title={drawerCopy.emissions}>
          <p className="flex items-center gap-1.5">
            <span className="text-2xl font-semibold tabular-nums">{formatTonnesCo2e(node.carbonKg)}</span>
            <FactorInfo factors={figure.factors} />
            {figure.factors.some(isPlaceholder) && <Tag tone="warning">{nodeCopy.placeholder}</Tag>}
          </p>
          {provenance !== null && (
            <p data-testid="drawer-provenance" className="text-xs break-words text-muted-foreground">
              {provenance}
            </p>
          )}
          <p data-testid="drawer-uncertainty" data-uncertainty={node.uncertainty} className="text-xs font-medium">
            {uncertaintyLabel[node.uncertainty]}
          </p>
          {figure !== node.spend && (
            <Rows>
              <Row label={drawerCopy.spendBefore}>
                <span className="inline-flex items-center gap-1.5 whitespace-nowrap">
                  {formatTonnesCo2e(node.spend.kg)}
                  <FactorInfo factors={node.spend.factors} />
                </span>
              </Row>
            </Rows>
          )}
        </Section>

        {answered && (
          <Section title={drawerCopy.answers}>
            <Rows>
              <Row label={drawerCopy.transport}>
                {node.transportMode === null ? drawerCopy.skipped : transportModeCopy[node.transportMode]}
              </Row>
              <Row label={drawerCopy.energy}>
                {node.energySource === null ? drawerCopy.skipped : energySourceCopy[node.energySource]}
              </Row>
            </Rows>
          </Section>
        )}

        <Section title={drawerCopy.classification}>
          <p className="flex flex-wrap items-center gap-1.5">
            {node.classification.categoryLabel}
            {ai !== null && <Tag>{ai}</Tag>}
          </p>
          <p className="text-xs text-muted-foreground">{drawerCopy.invoiceLine}</p>
          {/* Invoice text is data: rendered as text, clamped, and in full on hover. */}
          <p className="line-clamp-3 text-xs break-words" title={node.classification.lineDescription}>
            {node.classification.lineDescription}
          </p>
        </Section>

        <Section title={drawerCopy.invoice}>
          <Rows>
            <Row label={drawerCopy.invoiceNumber}>{node.invoice.number}</Row>
            <Row label={drawerCopy.issued}>{formatDate(node.invoice.issueDate)}</Row>
            <Row label={drawerCopy.spend}>{formatEuros(node.spendEur)}</Row>
          </Rows>
        </Section>

        <Section title={drawerCopy.lane} tag={ILLUSTRATIVE}>
          <Rows>
            <Row label={drawerCopy.route}>
              {node.lane.origin} → {node.lane.dest}
            </Row>
            <Row label={drawerCopy.mode}>{transportModeCopy[node.lane.mode]}</Row>
            <Row label={drawerCopy.distance}>{formatKm(node.lane.distanceKm)}</Row>
            <Row label={drawerCopy.cost} tag={ILLUSTRATIVE}>
              <span data-testid="drawer-cost">{formatEuros(node.costEur)}</span>
            </Row>
            <Row label={drawerCopy.leadTime} tag={ILLUSTRATIVE}>
              <span data-testid="drawer-lead-time">{formatDays(node.leadDays)}</span>
            </Row>
          </Rows>
        </Section>
      </div>

      <SheetFooter className="border-t">
        {linkError !== null && (
          <p role="alert" data-testid="drawer-request-error" className="text-xs text-destructive">
            {linkError}
          </p>
        )}
        <div className="flex gap-2">
          <Button className="flex-1" disabled={requesting} data-testid="drawer-request" onClick={() => void onRequest()}>
            {requesting ? drawerCopy.requesting : drawerCopy.requestData}
          </Button>
          <Button className="flex-1" variant="outline" disabled data-testid="drawer-simulate">
            {drawerCopy.simulate}
          </Button>
        </div>
      </SheetFooter>
    </>
  )
}

function Section({ title, tag, children }: { title: string; tag?: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-1.5">
      <h3 className="flex items-center gap-1.5 text-xs font-semibold tracking-wide text-muted-foreground uppercase">
        {title}
        {tag !== undefined && <Tag>{tag}</Tag>}
      </h3>
      {children}
    </section>
  )
}

function Rows({ children }: { children: ReactNode }) {
  return <dl className="flex flex-col divide-y">{children}</dl>
}

function Row({ label, tag, children }: { label: string; tag?: string; children: ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-3 py-1.5">
      <dt className="flex items-center gap-1.5 text-muted-foreground">
        {label}
        {tag !== undefined && <Tag>{tag}</Tag>}
      </dt>
      <dd className="text-right tabular-nums">{children}</dd>
    </div>
  )
}

function Tag({ tone, children }: { tone?: 'warning'; children: ReactNode }) {
  return (
    <span
      className={cn(
        'shrink-0 rounded px-1 text-[10px] font-medium tracking-normal normal-case',
        tone === 'warning' ? 'bg-destructive/10 font-semibold text-destructive' : 'bg-muted text-muted-foreground',
      )}
    >
      {children}
    </span>
  )
}
