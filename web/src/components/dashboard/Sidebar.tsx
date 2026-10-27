import { Wordmark } from '@/components/brand/Wordmark'
import { StatusDot } from '@/components/graph/StatusDot'
import { dashboardTitle, nodeCopy, sidebarCopy } from '@/copy/dashboard'
import { supplierConfirmedShare } from '@/copy/labels'
import { cn } from '@/lib/utils'
import { formatKpis, formatTonnes, sortedNodes, useGraphStore } from '@/store'

// The glass sidebar of the buyer dashboard: the wordmark, the buyer, every supplier as a row (largest emitter first;
// a click selects it on the canvas, the selected row is solid green whatever the supplier's status) and the
// supplier-confirmed share as a meter. Every number is the API's, formatted.

export function Sidebar({ className }: { className?: string }) {
  const buyer = useGraphStore((s) => s.buyer)
  const nodes = useGraphStore((s) => s.nodes)
  const kpis = useGraphStore((s) => s.kpis)
  const selectedId = useGraphStore((s) => s.selectedNodeId)
  const select = useGraphStore((s) => s.select)
  const rows = sortedNodes(nodes).sort((a, b) => b.carbonKg - a.carbonKg)
  const share = kpis !== null && kpis.totalKg > 0 ? kpis.primaryShare : null

  return (
    <aside
      aria-label={sidebarCopy.region}
      className={cn('glass w-[248px] flex-none flex-col gap-0.5 overflow-y-auto rounded-[var(--r-panel)] px-2.5 pt-4 pb-3.5', className)}
    >
      <div className="px-2 pt-0.5 pb-3.5">
        <Wordmark size={22} variant="intro" label={dashboardTitle} />
      </div>
      {buyer !== null && (
        <div className="mx-0.5 mb-2 flex flex-col gap-px rounded-[var(--r-group)] bg-white/70 px-3 py-2.5 shadow-[0_0_0_0.5px_rgb(0_0_0/0.08)]">
          <b className="text-[13px] font-semibold">{buyer.name}</b>
          <span className="text-xs text-mute">{sidebarCopy.buyerLine(buyer.city)}</span>
        </div>
      )}
      <p className="mx-2.5 mt-3.5 mb-1 text-[11px] font-semibold text-ink-2">{sidebarCopy.suppliers}</p>
      {rows.length === 0 && <p className="mx-2.5 text-xs text-mute">{sidebarCopy.noSuppliers}</p>}
      {rows.map((node) => {
        const selected = node.id === selectedId
        return (
          <button
            key={node.id}
            type="button"
            aria-pressed={selected}
            data-testid={`sidebar-${node.id}`}
            onClick={() => select(selected ? null : node.id)}
            className={cn(
              'nav-row flex h-8 w-full items-center gap-2.5 rounded-[9px] px-2.5 text-left text-[13px]',
              selected && 'nav-row-selected',
            )}
          >
            <StatusDot status={node.status} />
            <span className="min-w-0 flex-1 truncate">{node.label}</span>
            <span className="nav-row-value text-xs">{nodeCopy.tonnes(formatTonnes(node.carbonKg))}</span>
            <span className="sr-only">, {nodeCopy.status[node.status]}</span>
          </button>
        )
      })}
      <div className="mt-auto flex flex-col gap-2 px-2.5 pt-3.5">
        {share !== null && kpis !== null && (
          <>
            <div className="flex justify-between text-xs text-ink-2">
              <span>{supplierConfirmedShare.label}</span>
              <b className="font-semibold text-ink">{formatKpis(kpis).share}</b>
            </div>
            <span aria-hidden className="meter">
              <span className="meter-fill" style={{ transform: `scaleX(${Math.min(1, Math.max(0, share))})` }} />
            </span>
          </>
        )}
      </div>
    </aside>
  )
}
