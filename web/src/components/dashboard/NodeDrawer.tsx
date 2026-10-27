import { useState } from 'react'
import type { NodeStatus, NodeView, RequestLinkResponse } from '@/api/client'
import { Row, Rows, Section, Tag } from '@/components/dashboard/DrawerParts'
import { LinkCard } from '@/components/dashboard/LinkCard'
import { SimulatorPanel } from '@/components/dashboard/SimulatorPanel'
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
// was asked for, so another node starts without one. "Simulate" opens and closes the simulator panel (B5) at the top,
// and closing it takes its ghost node off the canvas.

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
  const simulating = useGraphStore((s) => s.openPanels.simulator)
  const setPanel = useGraphStore((s) => s.setPanel)

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
        {simulating && <SimulatorPanel node={node} />}

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
          <Button
            className="flex-1"
            variant={simulating ? 'secondary' : 'outline'}
            aria-pressed={simulating}
            data-testid="drawer-simulate"
            onClick={() => setPanel('simulator', !simulating)}
          >
            {drawerCopy.simulate}
          </Button>
        </div>
      </SheetFooter>
    </>
  )
}
