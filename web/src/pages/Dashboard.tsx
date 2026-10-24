import { Loader2Icon } from 'lucide-react'
import { type ReactNode, useState } from 'react'
import { AuditPanel, AuditToggle } from '@/components/dashboard/AuditPanel'
import { ConnectionBadge } from '@/components/dashboard/ConnectionBadge'
import { KpiBar } from '@/components/dashboard/KpiBar'
import { NodeDrawer } from '@/components/dashboard/NodeDrawer'
import { Toolbar } from '@/components/dashboard/Toolbar'
import { GraphCanvas } from '@/components/graph/GraphCanvas'
import { TooltipProvider } from '@/components/ui/tooltip'
import { canvasCopy, dashboardTitle } from '@/copy/dashboard'
import { type GraphStoreActions, useGraphEvents } from '@/hooks/useGraphEvents'
import { cn } from '@/lib/utils'
import { graphStore, selectLoaded, useGraphStore } from '@/store'

// The buyer's dashboard: toolbar, KPI bar and supplier graph, kept in sync with the API for as long as it is mounted.

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
  const supplierCount = useGraphStore((s) => Object.keys(s.nodes).length)
  // The drawer mounts inside the graph area, so it never covers the toolbar or the KPI bar.
  const [graphArea, setGraphArea] = useState<HTMLElement | null>(null)

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
    graph = <GraphCanvas />
  }

  return (
    <TooltipProvider>
      <div className="flex h-dvh flex-col">
        <header className="flex flex-wrap items-start justify-between gap-3 border-b px-4 py-3">
          <div>
            <div className="flex items-center gap-2">
              <h1 className="text-lg font-semibold">{dashboardTitle}</h1>
              <ConnectionBadge connection={connection} />
            </div>
            {buyer !== null && <p className="text-sm text-muted-foreground">{buyer.name}</p>}
          </div>
          <div className="flex items-start gap-2">
            <AuditToggle />
            <Toolbar connection={connection} />
          </div>
        </header>
        <div className="border-b px-4 py-3">
          <KpiBar kpis={kpis} connection={connection} />
        </div>
        <main ref={setGraphArea} className="relative min-h-0 flex-1 overflow-hidden" data-testid="graph">
          {graph}
        </main>
      </div>
      {graphArea !== null && (
        <>
          <AuditPanel container={graphArea} connection={connection} />
          <NodeDrawer container={graphArea} />
        </>
      )}
    </TooltipProvider>
  )
}

function GraphMessage({ children, tone, busy = false }: { children: ReactNode; tone?: 'error'; busy?: boolean }) {
  return (
    <div
      role={tone === 'error' ? 'alert' : 'status'}
      className={cn(
        'flex h-full items-center justify-center gap-2 p-6 text-center text-sm',
        tone === 'error' ? 'text-destructive' : 'text-muted-foreground',
      )}
    >
      {busy && <Loader2Icon className="size-4 motion-safe:animate-spin" aria-hidden />}
      {children}
    </div>
  )
}
