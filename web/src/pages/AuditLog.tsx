import { CheckIcon, ShieldCheckIcon, XIcon } from 'lucide-react'
import { type ReactNode, useMemo, useState } from 'react'
import type { ChainVerification } from '@/api/client'
import { PageHeader, PageMessage, Surface } from '@/components/shell/Page'
import { Button } from '@/components/ui/button'
import { auditPanelCopy } from '@/copy/dashboard'
import { auditCopy } from '@/copy/labels'
import { auditActionLabel, auditActorLabel, chainResultLabel, newestFirst, shortHash } from '@/lib/audit-display'
import { cn } from '@/lib/utils'
import { type Answer, verifyAuditChain } from '@/lib/dashboard-actions'
import { formatDateTime, selectLoaded, useGraphStore } from '@/store'
import { useShell } from '@/hooks/useShell'

// "Can I trust it?": the tamper-evident audit log on a screen of its own. The chain check sits at the top, the entries
// below it, newest first: the snapshot's chain plus every `audit.appended` since, both applied by the store. "Verify
// chain" asks the API to recompute every hash; its answer is shown until the log gains or loses an entry, because it
// no longer describes the log on screen after that. `audit-panel`, `audit-verify` and `audit-chain` are frozen hooks.

export default function AuditLog() {
  const { connection } = useShell()
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
        <PageMessage tone="error">{auditPanelCopy.offline}</PageMessage>
      ) : (
        <PageMessage busy>{auditPanelCopy.loading}</PageMessage>
      )
  } else if (entries.length === 0) {
    body = <PageMessage>{auditPanelCopy.empty}</PageMessage>
  } else {
    body = (
      <ol className="rounded-[18px] bg-card px-6 shadow-card">
        {entries.map((entry, i) => (
          <li
            key={entry.id}
            data-testid={`audit-entry-${entry.id}`}
            className={cn(
              'flex flex-wrap items-center gap-x-5 gap-y-1.5 py-4',
              i > 0 && 'shadow-[inset_0_0.5px_0_var(--hairline)]',
            )}
          >
            <div className="flex min-w-0 flex-[1_1_300px] flex-col gap-0.5">
              <span className="text-[15px] font-semibold">{auditActionLabel(entry.action)}</span>
              <span className="text-[13px] text-muted-foreground">
                {auditActorLabel(entry.actor)} · #{entry.id}
              </span>
            </div>
            <div className="flex flex-col items-end gap-1">
              <time dateTime={entry.ts} className="text-[13px] text-ink-2">
                {formatDateTime(entry.ts)}
              </time>
              <span className="font-mono text-[11px] text-muted-foreground" title={entry.hash}>
                {auditPanelCopy.hash} {shortHash(entry)}…
              </span>
            </div>
          </li>
        ))}
      </ol>
    )
  }

  return (
    <main data-testid="audit-panel" className="min-w-0 flex-1 px-5 py-8 md:px-12 md:py-11">
      <div className="flex max-w-[820px] flex-col gap-6">
        <PageHeader title={auditCopy.title} />
        <p className="-mt-4 max-w-[62ch] text-[15px] leading-relaxed text-muted-foreground">{auditPanelCopy.description}</p>
        <Surface className="flex flex-wrap items-center gap-x-5 gap-y-3 py-5">
          <ChainState answer={answer} verifying={verifying} />
          <Button variant="outline" disabled={verifying || !loaded} data-testid="audit-verify" onClick={() => void onVerify()}>
            {verifying ? auditPanelCopy.verifying : auditPanelCopy.verify}
          </Button>
        </Surface>
        {body}
      </div>
    </main>
  )
}

/** The API's verdict, or a prompt to ask for one; the Playwright smoke reads `audit-chain`'s `data-valid`. */
function ChainState({ answer, verifying }: { answer: Answer<ChainVerification> | null; verifying: boolean }) {
  if (answer !== null && !answer.ok) {
    return (
      <p role="alert" data-testid="audit-verify-error" className="flex-[1_1_260px] text-sm text-destructive">
        {answer.message}
      </p>
    )
  }
  const result = answer?.data ?? null
  const Icon = result === null ? ShieldCheckIcon : result.valid ? CheckIcon : XIcon
  const tone =
    result === null ? 'bg-fill text-muted-foreground' : result.valid ? 'bg-verified-tint text-verified-ink' : 'bg-hotspot-tint text-hotspot-ink'
  return (
    <div className="flex flex-[1_1_260px] items-center gap-4">
      <span className={cn('inline-flex size-10 shrink-0 items-center justify-center rounded-full', tone)}>
        <Icon className="size-[19px]" aria-hidden />
      </span>
      {result === null ? (
        <p role="status" className="text-sm text-ink-2">
          {verifying ? auditPanelCopy.verifying : auditPanelCopy.notChecked}
        </p>
      ) : (
        <div role="status" data-testid="audit-chain" data-valid={result.valid} className="flex flex-col gap-0.5">
          <span className="text-[17px] font-semibold tracking-[-0.015em]">{chainResultLabel(result)}</span>
          <span className="text-[13px] text-muted-foreground">{auditPanelCopy.checked(result.length)}</span>
        </div>
      )}
    </div>
  )
}
