import { RotateCcwIcon, UploadIcon } from 'lucide-react'
import { type ChangeEvent, useEffect, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { toolbarCopy } from '@/copy/dashboard'
import type { Connection } from '@/hooks/useGraphEvents'
import { type ActionOutcome, fetchDemoTools, importInvoice, loadDemoInvoice, resetDemo } from '@/lib/dashboard-actions'
import { cn } from '@/lib/utils'

// Import, Load demo invoice and Reset demo, in the top bar. The outcome arrives as a floating notification that slides in
// from the right and leaves after a few seconds; the graph itself updates from the event stream, never from these
// responses.

type Action = 'import' | 'demo' | 'reset'

const NOTICE_MS = 6000

export function Toolbar({ connection }: { connection: Connection }) {
  const [busy, setBusy] = useState<Action | null>(null)
  const [outcome, setOutcome] = useState<ActionOutcome | null>(null)
  const demoTools = useDemoTools(connection)
  const fileInput = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (outcome === null) return
    const timer = setTimeout(() => setOutcome(null), NOTICE_MS)
    return () => clearTimeout(timer)
  }, [outcome])

  const run = async (action: Action, request: () => Promise<ActionOutcome>) => {
    setBusy(action)
    setOutcome(null)
    setOutcome(await request())
    setBusy(null)
  }

  const onFile = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    event.target.value = '' // so picking the same file again fires another change
    if (file) void run('import', () => importInvoice(file))
  }

  return (
    <>
      <div className="flex flex-none items-center gap-2">
        <input
          ref={fileInput}
          type="file"
          accept=".xml,application/xml,text/xml"
          aria-label={toolbarCopy.fileInput}
          tabIndex={-1}
          className="sr-only"
          data-testid="import-file"
          onChange={onFile}
        />
        <Button data-testid="toolbar-import" disabled={busy !== null} onClick={() => fileInput.current?.click()}>
          <UploadIcon data-icon="inline-start" aria-hidden />
          {busy === 'import' ? toolbarCopy.importing : toolbarCopy.import}
        </Button>
        <Button
          variant="secondary"
          data-testid="toolbar-demo"
          disabled={busy !== null}
          onClick={() => void run('demo', loadDemoInvoice)}
        >
          {busy === 'demo' ? toolbarCopy.demoLoading : toolbarCopy.demo}
        </Button>
        {demoTools && (
          <Button
            variant="ghost"
            data-testid="toolbar-reset"
            disabled={busy !== null}
            onClick={() => void run('reset', resetDemo)}
          >
            <RotateCcwIcon data-icon="inline-start" aria-hidden />
            {busy === 'reset' ? toolbarCopy.resetting : toolbarCopy.reset}
          </Button>
        )}
      </div>
      <p
        role="status"
        data-testid="toolbar-message"
        className={cn(
          outcome !== null && 'notice surface-floating',
          outcome?.ok === false ? 'text-destructive' : 'text-ink',
        )}
      >
        {outcome?.message}
      </p>
    </>
  )
}

/** Health's `demoTools`, asked again whenever the API becomes reachable (it may have restarted with other settings). */
function useDemoTools(connection: Connection): boolean {
  const [enabled, setEnabled] = useState(false)
  const reachable = connection === 'live' || connection === 'polling'
  useEffect(() => {
    if (!reachable) return
    const aborts = new AbortController()
    void fetchDemoTools(aborts.signal).then((on) => {
      if (!aborts.signal.aborted) setEnabled(on)
    })
    return () => aborts.abort()
  }, [reachable])
  return enabled
}
