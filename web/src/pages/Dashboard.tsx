import type { ReactNode } from 'react'
import { Wordmark } from '@/components/brand/Wordmark'
import { KpiBar } from '@/components/dashboard/KpiBar'
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
            <h1>
              <Wordmark size={22} variant="intro" label={dashboardTitle} />
            </h1>
            {buyer !== null && <p className="text-sm text-muted-foreground">{buyer.name}</p>}
          </div>
          <Toolbar connection={connection} />
        </header>
        <div className="border-b px-4 py-3">
          <KpiBar kpis={kpis} connection={connection} />
        </div>
        <main className="relative min-h-0 flex-1" data-testid="graph">
          {graph}
        </main>
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
