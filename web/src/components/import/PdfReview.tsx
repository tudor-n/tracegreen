import { XIcon } from 'lucide-react'
import { type FormEvent, type Ref, useEffect, useId, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import type { DraftInvoice, PdfDraftView } from '@/api/client'
import { ExplainedTag, Group, Tag } from '@/components/shell/Page'
import { Button } from '@/components/ui/button'
import { AI_EXTRACTED } from '@/copy/labels'
import { pdfCopy } from '@/copy/pdf'
import { confirmPdf } from '@/lib/dashboard-actions'
import {
  addLine,
  flagged,
  INVOICE_DETAILS,
  type InvoiceField,
  initialForm,
  type LineField,
  linePath,
  removeLine,
  SCHEMES,
  SUPPLIER_FIELDS,
  setField,
  setLineField,
  UNIT_CODES,
} from '@/lib/pdf-form'

// The PDF review form (F9): the fields an AI copied from the PDF, for a person to check before anything is imported.
// One dialog over the screen, labelled once; a field the PDF's text does not show is marked until it is changed.
// Confirm imports through the API, which validates the fields like any invoice; Cancel drops the draft.

export function PdfReview({ draft, onClose, onImported }: { draft: PdfDraftView; onClose: () => void; onImported: (label: string) => void }) {
  const titleId = useId()
  const [form, setForm] = useState<DraftInvoice>(() => initialForm(draft.invoice))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const first = useRef<HTMLInputElement>(null)

  useEffect(() => {
    first.current?.focus()
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !event.defaultPrevented) {
        event.preventDefault()
        onClose()
      }
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  }, [onClose])

  const onSubmit = async (event: FormEvent) => {
    event.preventDefault()
    setSaving(true)
    setError(null)
    const answer = await confirmPdf(draft.draftId, form)
    setSaving(false)
    if (answer.ok) onImported(answer.data.node.label)
    else setError(answer.message)
  }

  const original = draft.invoice
  const invoiceInput = (field: InvoiceField, index: number) => (
    <Field
      key={field}
      label={pdfCopy.fields[field]}
      value={form[field]}
      flagged={flagged(draft.found, field, original[field], form[field])}
      onChange={(value) => setForm((f) => setField(f, field, value))}
      inputRef={index === 0 ? first : undefined}
      testId={`pdf-${field}`}
    />
  )

  // On document.body, so no stacking context of the sidebar it is opened from can put the screen above it.
  return createPortal(
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/30 p-0 sm:items-center sm:p-6">
      <form
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        data-testid="pdf-review"
        onSubmit={(event) => void onSubmit(event)}
        className="flex max-h-dvh w-full max-w-xl flex-col gap-4 overflow-y-auto rounded-t-[18px] bg-card px-6 pt-6 pb-5 shadow-card sm:max-h-[90dvh] sm:rounded-[18px]"
      >
        <div className="flex items-start gap-3">
          <div className="flex min-w-0 flex-1 flex-col gap-1.5">
            <h2 id={titleId} className="font-display text-[22px] leading-tight font-semibold tracking-[-0.02em]">
              {pdfCopy.title}
            </h2>
            <div>
              <ExplainedTag help={pdfCopy.extractedHelp}>{AI_EXTRACTED}</ExplainedTag>
            </div>
            <p className="text-[13px] text-muted-foreground">{pdfCopy.intro}</p>
            <p className="flex items-center gap-1.5 text-[13px] text-muted-foreground">
              <Tag tone="warning">{pdfCopy.notFound}</Tag>
              {pdfCopy.legend(pdfCopy.notFound)}
            </p>
          </div>
          <button
            type="button"
            aria-label={pdfCopy.close}
            onClick={onClose}
            className="inline-flex size-[30px] shrink-0 items-center justify-center rounded-full bg-fill text-ink-2 transition-colors outline-none hover:bg-fill-strong focus-visible:ring-2 focus-visible:ring-ring"
          >
            <XIcon className="size-3.5" strokeWidth={2.6} aria-hidden />
          </button>
        </div>

        <Group title={pdfCopy.supplier}>{SUPPLIER_FIELDS.map(invoiceInput)}</Group>
        <Group title={pdfCopy.invoice}>{INVOICE_DETAILS.map((field) => invoiceInput(field, 1))}</Group>

        <div className="flex flex-col gap-2">
          <h3 className="pl-0.5 text-[13px] font-medium text-muted-foreground">{pdfCopy.lines}</h3>
          {form.lines.map((line, i) => {
            const was = original.lines[i]
            const lineInput = (field: LineField) => (
              <Field
                key={field}
                label={pdfCopy.lineFields[field]}
                value={line[field]}
                flagged={was !== undefined && flagged(draft.found, linePath(i, field), was[field], line[field])}
                onChange={(value) => setForm((f) => setLineField(f, i, field, value))}
                testId={`pdf-line-${i}-${field}`}
              />
            )
            return (
              <div key={i} data-testid={`pdf-line-${i}`} className="rounded-[14px] bg-grouped px-4 pb-2 [&>*+*]:shadow-[inset_0_0.5px_0_var(--hairline)]">
                <div className="flex items-center justify-between py-2">
                  <span className="text-[13px] font-medium">{pdfCopy.line(i + 1)}</span>
                  <button
                    type="button"
                    onClick={() => setForm((f) => removeLine(f, i))}
                    className="rounded-sm text-[13px] font-medium text-destructive outline-none hover:underline focus-visible:ring-2 focus-visible:ring-ring"
                  >
                    {pdfCopy.removeLine(i + 1)}
                  </button>
                </div>
                {lineInput('description')}
                {lineInput('quantity')}
                <Choice
                  label={pdfCopy.lineFields.unitCode}
                  value={line.unitCode}
                  options={[['', pdfCopy.pickUnit], ...UNIT_CODES.map((u) => [u, pdfCopy.units[u] ?? u] as [string, string])]}
                  onChange={(value) => setForm((f) => setLineField(f, i, 'unitCode', value))}
                  testId={`pdf-line-${i}-unitCode`}
                />
                {lineInput('amount')}
                {lineInput('classificationCode')}
                <Choice
                  label={pdfCopy.lineFields.classificationScheme}
                  value={line.classificationScheme}
                  options={SCHEMES.map((s) => [s, pdfCopy.schemes[s] ?? s] as [string, string])}
                  onChange={(value) => setForm((f) => setLineField(f, i, 'classificationScheme', value))}
                  testId={`pdf-line-${i}-classificationScheme`}
                />
              </div>
            )
          })}
          <button
            type="button"
            onClick={() => setForm(addLine)}
            className="self-start rounded-sm px-0.5 text-[13px] font-medium text-primary outline-none hover:underline focus-visible:ring-2 focus-visible:ring-ring"
          >
            {pdfCopy.addLine}
          </button>
        </div>

        {error !== null && (
          <p role="alert" className="text-[13px] text-destructive">
            {error}
          </p>
        )}
        <div className="sticky bottom-0 -mx-6 -mb-5 flex justify-end gap-2 bg-card px-6 pt-2 pb-5">
          <Button type="button" variant="ghost" onClick={onClose} disabled={saving}>
            {pdfCopy.cancel}
          </Button>
          <Button type="submit" data-testid="pdf-confirm" disabled={saving}>
            {saving ? pdfCopy.confirming : pdfCopy.confirm}
          </Button>
        </div>
      </form>
    </div>,
    document.body,
  )
}

function Field({
  label,
  value,
  flagged,
  onChange,
  inputRef,
  testId,
}: {
  label: string
  value: string
  flagged: boolean
  onChange: (value: string) => void
  inputRef?: Ref<HTMLInputElement>
  testId: string
}) {
  const id = useId()
  return (
    <div className="flex flex-col gap-1 py-2 text-sm" data-flagged={flagged}>
      <label htmlFor={id} className="flex items-center gap-1.5 text-ink-2">
        {label}
        {flagged && <Tag tone="warning">{pdfCopy.notFound}</Tag>}
      </label>
      <input
        id={id}
        ref={inputRef}
        data-testid={testId}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="h-9 rounded-lg bg-card px-2.5 font-medium outline-none focus-visible:ring-2 focus-visible:ring-ring"
      />
    </div>
  )
}

function Choice({
  label,
  value,
  options,
  onChange,
  testId,
}: {
  label: string
  value: string
  options: [string, string][]
  onChange: (value: string) => void
  testId: string
}) {
  const id = useId()
  return (
    <div className="flex flex-col gap-1 py-2 text-sm">
      <label htmlFor={id} className="text-ink-2">
        {label}
      </label>
      <select
        id={id}
        data-testid={testId}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="h-9 rounded-lg bg-card px-2 font-medium outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        {options.map(([v, text]) => (
          <option key={v} value={v}>
            {text}
          </option>
        ))}
      </select>
    </div>
  )
}
