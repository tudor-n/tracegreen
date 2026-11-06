import { BuildingIcon } from 'lucide-react'
import { Fragment, useEffect, useState } from 'react'
import type { AlternativeView, Buyer, NodeStatus, NodeView } from '@/api/client'
import { ScenarioBadge } from '@/components/graph/ScenarioBadge'
import { nodeCopy, scenarioCopy, suppliersCopy } from '@/copy/dashboard'
import { isPlaceholder, justVerified, shownFigure } from '@/lib/node-display'
import { categoryCheckLabel } from '@/lib/review-display'
import { scenarioTitle } from '@/lib/scenario-display'
import { cn } from '@/lib/utils'
import { CO2E_UNIT, edgeWidth, formatTonnes, formatTonnesDelta, type ScenarioPreview } from '@/store'

// The supplier tree (F2, v2 design): one row per supplier, largest emitter at the top, and a supply line from each
// row into the buyer. Line width follows the node's tonnes (the store's edgeWidth), colour follows its status, and a
// PARTIAL line carries a dashed green overlay. A simulated change (B5) shows as a dashed "What if" row under its
// supplier with a dotted line of its own. In a narrow tree (a phone, or beside the panel on a small laptop) the lines
// and the buyer card step aside and the rows remain; in a wide one the lines stretch so the tree fills the card. Rows are buttons: a click selects the supplier and opens the side panel.
// Frozen test hooks (RUNBOOK): `node-<id>` with `data-status`, `ghost-<id>` with `data-badge`, `ghost-<id>-saved`.
// The classification review's flag (F6): `node-<id>-check`.

const ROW_H = 72
const GHOST_H = 64
const LINE_W = 96
const LINE_GAP = 2
/** How long a row keeps `data-flash` after its supplier confirms; matches the row-sweep animation in index.css. */
const FLASH_MS = 1400

interface TreeProps {
  nodes: readonly NodeView[]
  hotspots: Set<string>
  buyer: Buyer | null
  total: string | null
  selectedId: string | null
  preview: ScenarioPreview | null
  onSelect: (nodeId: string) => void
}

export function SupplierTree({ nodes, hotspots, buyer, total, selectedId, preview, onSelect }: TreeProps) {
  const lines = supplyLines(nodes, preview)
  const height = lines.at(-1)?.bottom ?? 0
  return (
    <section aria-label={suppliersCopy.title} className="@container rounded-[18px] bg-card px-3 pt-3 pb-4 shadow-card">
      {/* Rows and lines are both centred on the taller of the two (the rows, or the buyer card with one or two suppliers),
          so each line starts level with its row: the drawing is exactly as tall as the rows. */}
      <div className="flex items-center">
        <ul className="flex min-w-0 flex-1 flex-col">
          {nodes.map((node) => (
            <Fragment key={node.id}>
              <li>
                <SupplierRow
                  node={node}
                  hotspot={hotspots.has(node.id)}
                  selected={node.id === selectedId}
                  onSelect={() => onSelect(node.id)}
                />
              </li>
              {preview?.nodeId === node.id && (
                <li>
                  <GhostRow preview={preview} alternatives={node.alternatives} />
                </li>
              )}
            </Fragment>
          ))}
        </ul>
        <div aria-hidden className="hidden w-[clamp(240px,38%,520px)] shrink-0 items-center @min-[560px]:flex">
          {/* The drawing is LINE_W wide and stretched to the space there is; strokes keep their width. */}
          <svg
            width="100%"
            height={height}
            viewBox={`0 0 ${LINE_W} ${height}`}
            preserveAspectRatio="none"
            className="block min-w-24 flex-1 overflow-visible"
          >
            {lines.map((line) =>
              line.kind === 'ghost' ? (
                <path key={line.key} d={line.d} className="supply-line-ghost" vectorEffect="non-scaling-stroke" />
              ) : (
                <g key={line.key} opacity={selectedId === null || selectedId === line.key ? 1 : 0.45} className="transition-opacity duration-500">
                  <path d={line.d} className="supply-line" data-status={line.status} strokeWidth={line.width} vectorEffect="non-scaling-stroke" />
                  {line.status === 'PARTIAL' && (
                    <path d={line.d} className="supply-line-partial" strokeWidth={line.width} vectorEffect="non-scaling-stroke" />
                  )}
                </g>
              ),
            )}
          </svg>
          <BuyerCard buyer={buyer} total={total} />
        </div>
      </div>
      <StatusKey />
    </section>
  )
}

interface Line {
  key: string
  kind: 'supplier' | 'ghost'
  status: NodeStatus
  d: string
  width: number
  /** The bottom edge of the row the line starts from, so the last one gives the tree's height. */
  bottom: number
}

/**
 * One line per row, from the row's middle on the left to its slot on the buyer card on the right. The slots are
 * stacked by width around the buyer's middle, so the lines meet like a bundle instead of crossing.
 */
function supplyLines(nodes: readonly NodeView[], preview: ScenarioPreview | null): Line[] {
  const rows: Omit<Line, 'd'>[] = []
  let y = 0
  const starts: number[] = []
  for (const node of nodes) {
    starts.push(y + ROW_H / 2)
    y += ROW_H
    rows.push({ key: node.id, kind: 'supplier', status: node.status, width: edgeWidth(node.carbonKg), bottom: y })
    if (preview?.nodeId === node.id) {
      starts.push(y + GHOST_H / 2)
      y += GHOST_H
      rows.push({ key: `${node.id}-ghost`, kind: 'ghost', status: node.status, width: 1.5, bottom: y })
    }
  }
  const bundle = rows.reduce((sum, r) => sum + r.width, 0) + LINE_GAP * Math.max(0, rows.length - 1)
  let slot = y / 2 - bundle / 2
  return rows.map((row, i) => {
    const y0 = starts[i]
    const y1 = slot + row.width / 2
    slot += row.width + LINE_GAP
    const d = `M0 ${y0} C${LINE_W * 0.55} ${y0} ${LINE_W * 0.45} ${y1.toFixed(2)} ${LINE_W} ${y1.toFixed(2)}`
    return { ...row, d }
  })
}

function SupplierRow({ node, hotspot, selected, onSelect }: { node: NodeView; hotspot: boolean; selected: boolean; onSelect: () => void }) {
  const flashing = useVerifiedFlash(node.status)
  const placeholder = shownFigure(node).factors.some(isPlaceholder)
  const check = categoryCheckLabel(node.classification)
  return (
    <button
      type="button"
      data-testid={`node-${node.id}`}
      data-status={node.status}
      data-hotspot={hotspot}
      data-flash={flashing || undefined}
      aria-pressed={selected}
      onClick={onSelect}
      style={{ height: ROW_H }}
      className={cn(
        'supplier-row relative flex w-full items-center gap-3.5 overflow-hidden rounded-xl px-3.5 text-left transition-colors duration-200 motion-safe:animate-in motion-safe:fade-in-0 motion-safe:slide-in-from-left-6 motion-safe:duration-700',
        selected ? 'bg-[#ededf0]' : 'hover:bg-grouped',
      )}
    >
      <span aria-hidden data-status={node.status} className="status-dot size-2.5 shrink-0 rounded-full" />
      <span className="flex min-w-0 flex-1 flex-col gap-0.5">
        <span className="truncate text-[15px] font-semibold" title={node.label}>
          {node.label}
        </span>
        <span className="truncate text-[13px] text-muted-foreground">
          {/* First on the line, so a narrow row truncates the rest and never the flag (F6). */}
          {check !== null && (
            <span data-testid={`node-${node.id}-check`} className="font-semibold text-estimated-ink">
              {check} ·{' '}
            </span>
          )}
          {nodeCopy.status[node.status]}
          {hotspot && ` · ${nodeCopy.hotspot}`}
          {placeholder && <span className="text-estimated-ink"> · {nodeCopy.placeholder}</span>}
        </span>
      </span>
      <span className="shrink-0 text-right text-lg font-semibold tracking-[-0.02em]">
        {formatTonnes(node.carbonKg)} <span className="text-xs font-medium tracking-normal text-muted-foreground">{CO2E_UNIT}</span>
      </span>
    </button>
  )
}

/** The simulated change under its supplier, as the API answered it; never selectable. */
function GhostRow({ preview, alternatives }: { preview: ScenarioPreview; alternatives: readonly AlternativeView[] }) {
  const { nodeId, change, result } = preview
  const title = scenarioTitle(change, alternatives)
  return (
    <div
      data-testid={`ghost-${nodeId}`}
      data-badge={result.massUnknown ? undefined : result.badge}
      style={{ height: GHOST_H - 8 }}
      className="my-1 ml-6 flex items-center gap-3 rounded-xl border-[1.5px] border-dashed border-muted-foreground/50 px-3.5 motion-safe:animate-in motion-safe:fade-in-0 motion-safe:slide-in-from-top-2 motion-safe:duration-500"
    >
      <span className="flex min-w-0 flex-1 flex-col gap-0.5">
        <span className="text-xs font-medium text-muted-foreground">
          {scenarioCopy.whatIf}
          {preview.savedId !== undefined && <span data-testid={`ghost-${nodeId}-saved`}> · {scenarioCopy.saved}</span>}
        </span>
        <span className="truncate text-sm font-medium" title={title}>
          {title}
        </span>
      </span>
      {result.massUnknown ? (
        <span className="text-xs text-muted-foreground">{scenarioCopy.massUnknownShort}</span>
      ) : (
        <>
          <ScenarioBadge badge={result.badge} />
          <span className="flex shrink-0 flex-col items-end">
            <span className="text-[15px] font-semibold">
              {formatTonnes(result.carbonKg)} <span className="text-xs font-medium text-muted-foreground">{CO2E_UNIT}</span>
            </span>
            <span className="text-xs text-muted-foreground">
              {scenarioCopy.versusNow(`${formatTonnesDelta(result.delta.carbonKg)} ${CO2E_UNIT}`)}
            </span>
          </span>
        </>
      )}
    </div>
  )
}

function BuyerCard({ buyer, total }: { buyer: Buyer | null; total: string | null }) {
  return (
    <div className="flex w-36 flex-col gap-2 rounded-2xl bg-card p-3.5 shadow-raised">
      <span className="inline-flex size-[30px] items-center justify-center rounded-[9px] bg-grouped text-ink-2">
        <BuildingIcon className="size-4" strokeWidth={1.7} aria-hidden />
      </span>
      <span className="text-[13px] leading-snug font-semibold">{buyer?.name ?? nodeCopy.buyer}</span>
      {buyer !== null && (
        <span className="text-xs text-muted-foreground">
          {nodeCopy.buyer} · {buyer.city}, {buyer.country}
        </span>
      )}
      {total !== null && (
        <span className="text-[17px] font-semibold tracking-[-0.02em]">
          {total} <span className="text-xs font-medium tracking-normal text-muted-foreground">{CO2E_UNIT}</span>
        </span>
      )}
    </div>
  )
}

function StatusKey() {
  const statuses: NodeStatus[] = ['ESTIMATED', 'PARTIAL', 'VERIFIED']
  return (
    <ul aria-label={suppliersCopy.statusKey} className="mt-1.5 flex flex-wrap gap-x-5 gap-y-2 px-3.5 pt-3.5 text-xs text-ink-2 shadow-[inset_0_0.5px_0_var(--hairline)]">
      {statuses.map((status) => (
        <li key={status} className="inline-flex items-center gap-2">
          <span aria-hidden data-status={status} className="status-dot size-2 rounded-full" />
          {nodeCopy.status[status]}
        </li>
      ))}
    </ul>
  )
}

/**
 * True for FLASH_MS after the status turns VERIFIED. The previous status is kept in state and compared while
 * rendering, so the first render never flashes and a node that was verified already never flashes again.
 */
function useVerifiedFlash(status: NodeStatus): boolean {
  const [previous, setPrevious] = useState(status)
  const [flashing, setFlashing] = useState(false)
  if (status !== previous) {
    setPrevious(status)
    setFlashing(justVerified(previous, status))
  }
  useEffect(() => {
    if (!flashing) return
    const timer = setTimeout(() => setFlashing(false), FLASH_MS)
    return () => clearTimeout(timer)
  }, [flashing])
  return flashing
}
