import { RotateCcwIcon, UploadIcon } from 'lucide-react'
import { type ChangeEvent, useRef, useState } from 'react'
import type { PdfDraftView } from '@/api/client'
import { PdfReview } from '@/components/import/PdfReview'
import { Button } from '@/components/ui/button'
import { toolbarCopy } from '@/copy/dashboard'
import type { Connection } from '@/hooks/useGraphEvents'
import { useDemoTools } from '@/hooks/useDemoTools'
import { type ActionOutcome, extractPdf, importInvoice, loadDemoInvoice, resetDemo } from '@/lib/dashboard-actions'
import { isPdf } from '@/lib/pdf-form'
import { cn } from '@/lib/utils'

// Import, Load demo invoice and Reset demo, stacked at the foot of the sidebar. The outcome is shown as text; the
// graph itself updates from the event stream, never from these responses.

type Action = 'import' | 'demo' | 'reset'

export function Toolbar({ connection }: { connection: Connection }) {
  const [busy, setBusy] = useState<Action | null>(null)
  const [outcome, setOutcome] = useState<ActionOutcome | null>(null)
  // Asked again whenever the API becomes reachable: it may have restarted with other settings.
  const demoTools = useDemoTools(connection === 'live' || connection === 'polling')
  const fileInput = useRef<HTMLInputElement>(null)
  const [review, setReview] = useState<PdfDraftView | null>(null)

  const run = async (action: Action, request: () => Promise<ActionOutcome>) => {
    setBusy(action)
    setOutcome(null)
    setOutcome(await request())
    setBusy(null)
  }

  const onFile = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    event.target.value = '' // so picking the same file again fires another change
    if (!file) return
    // A PDF is read into a draft for a person to check (F9); an XML is imported as it is.
    if (isPdf(file)) {
      void run('import', async () => {
        const answer = await extractPdf(file)
        if (!answer.ok) return answer
        setReview(answer.data)
        return { ok: true, message: toolbarCopy.pdfReady }
      })
    } else {
      void run('import', () => importInvoice(file))
    }
  }

  return (
    <div className="flex flex-col gap-1.5">
      <input
        ref={fileInput}
        type="file"
        accept=".xml,application/xml,text/xml,.pdf,application/pdf"
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
      {review !== null && (
        <PdfReview
          draft={review}
          onClose={() => {
            setReview(null)
            setOutcome(null)
          }}
          onImported={(label) => {
            setReview(null)
            setOutcome({ ok: true, message: toolbarCopy.imported(label) })
          }}
        />
      )}
    </div>
  )
}
