import type { CSSProperties, ReactNode } from 'react'
import { Backdrop } from '@/components/brand/Backdrop'
import { Wordmark } from '@/components/brand/Wordmark'
import { KpiBar } from '@/components/dashboard/KpiBar'
import { Sidebar } from '@/components/dashboard/Sidebar'
import { Toolbar } from '@/components/dashboard/Toolbar'
import { GraphCanvas } from '@/components/graph/GraphCanvas'
import { TooltipProvider } from '@/components/ui/tooltip'
import { canvasCopy, dashboardTitle, topBarCopy } from '@/copy/dashboard'
import { type GraphStoreActions, useGraphEvents } from '@/hooks/useGraphEvents'
import { invoiceCount, invoicePeriod } from '@/lib/node-display'
import { cn } from '@/lib/utils'
import { graphStore, selectLoaded, useGraphStore } from '@/store'

// The buyer's dashboard: sidebar, top bar with the actions, KPI tiles and the supplier graph, kept in sync with the API for as long as it is mounted.

/** The sync drives the app store through these two actions only (docs/plan/README.md §3). */
const sync: GraphStoreActions = {
  hydrate: (snapshot) => graphStore.getState().hydrate(snapshot),
  applyEvent: (event) => graphStore.getState().applyEvent(event),
}

export default function Dashboard() {
  const connection = useGraphEvents(sync)
  const loaded = useGraphStore(selectLoaded)
  const kpis = useGraphStore((s) => s.kpis)
  const buyer = useGraphStore((s) => s.buyer)
  const nodes = useGraphStore((s) => s.nodes)
  const supplierCount = Object.keys(nodes).length

  let graph: ReactNode
  if (!loaded) {
    graph =
      connection === 'offline' ? (
        <GraphMessage tone="error">{canvasCopy.offline}</GraphMessage>
      ) : (
        <GraphMessage busy>{canvasCopy.loading}</GraphMessage>
      )
  } else if (supplierCount === 0) {
    graph = <GraphMessage>{canvasCopy.empty}</GraphMessage>
  } else {
    graph = (
      <>
        <GraphCanvas />
        <span className="glass pointer-events-none absolute bottom-3 left-3.5 z-10 inline-flex h-7 items-center rounded-full px-3 text-xs text-ink-2">
          {canvasCopy.legend}
        </span>
      </>
    )
  }
  const nodeList = Object.values(nodes)
  const invoices = invoiceCount(nodeList)

  // The supplier-confirmed share as the API sent it drives the backdrop and the buyer's glow; nothing is computed.
  const share = kpis !== null && Number.isFinite(kpis.primaryShare) ? Math.min(1, Math.max(0, kpis.primaryShare)) : 0
  const windowStyle = { '--g': share } as CSSProperties

  return (
    <TooltipProvider>
      <div className="tg-window isolate flex h-dvh gap-3 p-3" style={windowStyle}>
        <Backdrop clean={share > 0} />
        <Sidebar className="hidden min-[1360px]:flex" />
        <div className="flex min-w-0 flex-1 flex-col gap-3">
          <header className="glass flex h-[52px] flex-none items-center gap-3 rounded-[var(--r-tile)] pr-2.5 pl-[18px]">
            <span className="flex-none min-[1360px]:hidden">
              <Wordmark size={18} variant="static" label={dashboardTitle} />
            </span>
            <div className="flex min-w-0 flex-1 flex-col leading-tight">
              <h1 className="text-sm font-bold">{topBarCopy.title(invoicePeriod(nodeList))}</h1>
              {buyer !== null && (
                <p className="truncate text-xs text-mute">{topBarCopy.subtitle(buyer.name, supplierCount, invoices)}</p>
              )}
            </div>
            <Toolbar connection={connection} />
          </header>
          <KpiBar kpis={kpis} invoices={invoices} connection={connection} />
          <main
            className="work-surface relative min-h-0 flex-1 overflow-hidden rounded-[var(--r-panel)]"
            data-testid="graph"
          >
            {graph}
          </main>
        </div>
      </div>
    </TooltipProvider>
  )
}

function GraphMessage({ children, tone, busy = false }: { children: ReactNode; tone?: 'error'; busy?: boolean }) {
  return (
    <div
      role={tone === 'error' ? 'alert' : 'status'}
      className={cn(
        'flex h-full items-center justify-center p-6 text-center text-sm',
        busy ? 'flex-col gap-4' : 'gap-2',
        tone === 'error' ? 'text-destructive' : 'text-muted-foreground',
      )}
    >
      {busy && <Wordmark size={28} variant="loader" label={null} />}
      {children}
    </div>
  )
}
