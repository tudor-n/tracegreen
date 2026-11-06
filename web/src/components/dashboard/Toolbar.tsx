import { RotateCcwIcon, UploadIcon } from 'lucide-react'
import { type ChangeEvent, useEffect, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { toolbarCopy } from '@/copy/dashboard'
import type { Connection } from '@/hooks/useGraphEvents'
import { type ActionOutcome, fetchDemoTools, importInvoice, loadDemoInvoice, resetDemo } from '@/lib/dashboard-actions'
import { cn } from '@/lib/utils'

// Import, Load demo invoice and Reset demo, stacked at the foot of the sidebar. The outcome is shown as text; the
// graph itself updates from the event stream, never from these responses.

type Action = 'import' | 'demo' | 'reset'

export function Toolbar({ connection }: { connection: Connection }) {
  const [busy, setBusy] = useState<Action | null>(null)
  const [outcome, setOutcome] = useState<ActionOutcome | null>(null)
  const demoTools = useDemoTools(connection)
  const fileInput = useRef<HTMLInputElement>(null)

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
    <div className="flex flex-col gap-1.5">
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
      <div className="flex flex-wrap gap-1.5 md:flex-col">
        <Button
          variant="ghost"
          data-testid="toolbar-demo"
          disabled={busy !== null}
          className="md:justify-start"
          onClick={() => void run('demo', loadDemoInvoice)}
        >
          {busy === 'demo' ? toolbarCopy.demoLoading : toolbarCopy.demo}
        </Button>
        {demoTools && (
          <Button
            variant="ghost"
            data-testid="toolbar-reset"
            disabled={busy !== null}
            className="md:justify-start"
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
        className={cn('min-h-4 px-1 text-xs', outcome?.ok === false ? 'text-destructive' : 'text-muted-foreground')}
      >
        {outcome?.message}
      </p>
    </div>
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
