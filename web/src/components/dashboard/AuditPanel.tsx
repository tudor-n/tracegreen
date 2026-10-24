import { ScrollTextIcon } from 'lucide-react'
import { useMemo, type ReactNode } from 'react'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { auditCopy } from '@/copy/dashboard'
import type { Connection } from '@/hooks/useGraphEvents'
import { auditActionLabel, auditActorLabel, newestFirst, shortHash } from '@/lib/audit-display'
import { formatDateTime, selectLoaded, useGraphStore } from '@/store'

// The tamper-evident audit log, newest entry first: the snapshot's chain plus every `audit.appended` since, both
// applied by the store. Mounted inside the graph area on the left, non-modal like the node drawer, so both can be open
// with the canvas live between them. "Verify chain" stays disabled until POST /api/audit/verify exists (A5/A6).

/** Opens and closes the audit panel; sits beside the toolbar. */
export function AuditToggle() {
  const open = useGraphStore((s) => s.openPanels.audit)
  const setPanel = useGraphStore((s) => s.setPanel)
  return (
    <Button variant="outline" aria-pressed={open} data-testid="toolbar-audit" onClick={() => setPanel('audit', !open)}>
      <ScrollTextIcon data-icon="inline-start" aria-hidden />
      {auditCopy.open}
    </Button>
  )
}

export function AuditPanel({ container, connection }: { container: HTMLElement; connection: Connection }) {
  const open = useGraphStore((s) => s.openPanels.audit)
  const setPanel = useGraphStore((s) => s.setPanel)
  const loaded = useGraphStore(selectLoaded)
  const audit = useGraphStore((s) => s.audit)
  const entries = useMemo(() => newestFirst(audit), [audit])

  let body: ReactNode
  if (!loaded) {
    body =
      connection === 'offline' ? (
        <Message tone="error">{auditCopy.offline}</Message>
      ) : (
        <Message>{auditCopy.loading}</Message>
      )
  } else if (entries.length === 0) {
    body = <Message>{auditCopy.empty}</Message>
  } else {
    body = (
      <ol className="flex flex-col divide-y">
        {entries.map((entry) => (
          <li key={entry.id} data-testid={`audit-entry-${entry.id}`} className="flex flex-col gap-0.5 px-4 py-2.5">
            <div className="flex items-baseline justify-between gap-2">
              <span className="font-medium">{auditActionLabel(entry.action)}</span>
              <span className="text-xs text-muted-foreground tabular-nums">#{entry.id}</span>
            </div>
            <div className="flex items-baseline justify-between gap-2 text-xs text-muted-foreground">
              <span>{auditActorLabel(entry.actor)}</span>
              <time dateTime={entry.ts} className="tabular-nums">
                {formatDateTime(entry.ts)}
              </time>
            </div>
            <p className="font-mono text-[11px] text-muted-foreground" title={entry.hash}>
              {auditCopy.hash} {shortHash(entry)}…
            </p>
          </li>
        ))}
      </ol>
    )
  }

  return (
    <Sheet
      open={open}
      modal={false}
      disablePointerDismissal
      onOpenChange={(next) => {
        if (!next) setPanel('audit', false)
      }}
    >
      <SheetContent
        side="left"
        container={container}
        showOverlay={false}
        data-testid="audit-panel"
        className="absolute gap-0"
      >
        <SheetHeader className="border-b pr-12">
          <SheetTitle>{auditCopy.title}</SheetTitle>
          <SheetDescription>{auditCopy.description}</SheetDescription>
        </SheetHeader>
        <div className="min-h-0 flex-1 overflow-y-auto">{body}</div>
        <SheetFooter className="border-t">
          <Button variant="outline" disabled data-testid="audit-verify">
            {auditCopy.verify}
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  )
}

function Message({ tone, children }: { tone?: 'error'; children: ReactNode }) {
  return (
    <p
      role={tone === 'error' ? 'alert' : 'status'}
      className={tone === 'error' ? 'p-4 text-sm text-destructive' : 'p-4 text-sm text-muted-foreground'}
    >
      {children}
    </p>
  )
}
