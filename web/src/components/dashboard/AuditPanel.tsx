import { CheckIcon, ScrollTextIcon, XIcon } from 'lucide-react'
import { useMemo, useState, type ReactNode } from 'react'
import type { ChainVerification } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { auditPanelCopy } from '@/copy/dashboard'
import { auditCopy } from '@/copy/labels'
import type { Connection } from '@/hooks/useGraphEvents'
import { auditActionLabel, auditActorLabel, chainResultLabel, newestFirst, shortHash } from '@/lib/audit-display'
import { type Answer, verifyAuditChain } from '@/lib/dashboard-actions'
import { formatDateTime, selectLoaded, useGraphStore } from '@/store'

// The tamper-evident audit log, newest entry first: the snapshot's chain plus every `audit.appended` since, both
// applied by the store. Mounted inside the graph area on the left, non-modal like the node drawer, so both can be open
// with the canvas live between them. "Verify chain" asks the API to recompute every hash; its answer is shown until the
// log gains or loses an entry, because it no longer describes the log on screen after that.

/** Opens and closes the audit panel; sits beside the toolbar. */
export function AuditToggle() {
  const open = useGraphStore((s) => s.openPanels.audit)
  const setPanel = useGraphStore((s) => s.setPanel)
  return (
    <Button variant="outline" aria-pressed={open} data-testid="toolbar-audit" onClick={() => setPanel('audit', !open)}>
      <ScrollTextIcon data-icon="inline-start" aria-hidden />
      {auditPanelCopy.open}
    </Button>
  )
}

export function AuditPanel({ container, connection }: { container: HTMLElement; connection: Connection }) {
  const open = useGraphStore((s) => s.openPanels.audit)
  const setPanel = useGraphStore((s) => s.setPanel)
  const loaded = useGraphStore(selectLoaded)
  const audit = useGraphStore((s) => s.audit)
  const entries = useMemo(() => newestFirst(audit), [audit])
  const head = audit.at(-1)?.hash
  const [check, setCheck] = useState<{ head: string | undefined; answer: Answer<ChainVerification> } | null>(null)
  const [verifying, setVerifying] = useState(false)
  // Keyed by the newest hash rather than the array, which every snapshot replaces even when nothing changed.
  const answer = check !== null && check.head === head ? check.answer : null

  const onVerify = async () => {
    setVerifying(true)
    setCheck({ head, answer: await verifyAuditChain() })
    setVerifying(false)
  }

  let body: ReactNode
  if (!loaded) {
    body =
      connection === 'offline' ? (
        <Message tone="error">{auditPanelCopy.offline}</Message>
      ) : (
        <Message>{auditPanelCopy.loading}</Message>
      )
  } else if (entries.length === 0) {
    body = <Message>{auditPanelCopy.empty}</Message>
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
              {auditPanelCopy.hash} {shortHash(entry)}…
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
          <SheetDescription>{auditPanelCopy.description}</SheetDescription>
        </SheetHeader>
        <div className="min-h-0 flex-1 overflow-y-auto">{body}</div>
        <SheetFooter className="border-t">
          {answer !== null &&
            (answer.ok ? (
              <ChainResult result={answer.data} />
            ) : (
              <p role="alert" data-testid="audit-verify-error" className="text-xs text-destructive">
                {answer.message}
              </p>
            ))}
          <Button variant="outline" disabled={verifying} data-testid="audit-verify" onClick={() => void onVerify()}>
            {verifying ? auditPanelCopy.verifying : auditPanelCopy.verify}
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  )
}

/** The API's verdict; the Playwright smoke reads `data-valid`. */
function ChainResult({ result }: { result: ChainVerification }) {
  const Icon = result.valid ? CheckIcon : XIcon
  return (
    <div role="status" data-testid="audit-chain" data-valid={result.valid} className="flex flex-col gap-0.5">
      <p className="flex items-center gap-1.5 text-sm font-medium">
        {chainResultLabel(result)}
        <Icon
          aria-hidden
          className={result.valid ? 'size-4 text-(--status-verified)' : 'size-4 text-destructive'}
        />
      </p>
      <p className="text-xs text-muted-foreground">{auditPanelCopy.checked(result.length)}</p>
    </div>
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
