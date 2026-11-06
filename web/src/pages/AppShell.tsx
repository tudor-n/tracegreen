import { Outlet } from 'react-router'
import { ChatPanel } from '@/components/chat/ChatPanel'
import { Sidebar } from '@/components/shell/Sidebar'
import { TooltipProvider } from '@/components/ui/tooltip'
import { type GraphStoreActions, useGraphEvents } from '@/hooks/useGraphEvents'
import type { ShellContext } from '@/hooks/useShell'
import { graphStore } from '@/store'

// The buyer's app: the sidebar and one screen beside it (Overview, Suppliers, Audit log). The store stays in sync
// with the API for as long as the shell is mounted, so switching screens keeps the graph, the selection and the
// connection; each screen reads the connection state through useShell. The chat (F13) opens beside any screen and
// keeps its conversation while the shell is mounted.

/** The sync drives the app store through these two actions only (docs/plan/README.md §3). */
const sync: GraphStoreActions = {
  hydrate: (snapshot) => graphStore.getState().hydrate(snapshot),
  applyEvent: (event) => graphStore.getState().applyEvent(event),
}

export default function AppShell() {
  const connection = useGraphEvents(sync)
  return (
    <TooltipProvider>
      <div className="flex min-h-dvh flex-col md:flex-row">
        <Sidebar connection={connection} />
        <Outlet context={{ connection } satisfies ShellContext} />
        <ChatPanel />
      </div>
    </TooltipProvider>
  )
}
