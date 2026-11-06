import { ChevronDownIcon } from 'lucide-react'
import { type ReactNode, useId, useState } from 'react'
import { Button } from '@/components/ui/button'
import type { ProofStrings } from '@/copy/verify'
import {
  CERT_TYPES,
  docTypesFor,
  type EvidenceAnswers,
  type EvidenceDraft,
  type EvidenceField,
  type EvidenceProblems,
  isEmptyEvidence,
  problemLines,
  proofApplies,
  shownCertType,
  shownDocType,
} from '@/lib/evidence'
import { cn } from '@/lib/utils'
import { demoTags } from '@/lib/verify-state'

// The optional "Add proof" section under the two questions (F7), on /verify and on each open delivery of /supplier. It
// starts collapsed and empty: untouched, the answers go as two taps. Open, it shows the most usual document for the
// transport answer as the label of one number field (with a small "other document" control when more than one fits)
// and the dispatch date on one line under it, both hidden while transport is skipped; for a renewable answer, the same
// for the certificate. Nothing is filled in for the supplier, except by "Fill demo values", which the page offers only
// while the API runs with demo tools, and whose numbers carry a "demo values" tag. A field's problem is written under it
// in the supplier's language (rules in web/src/lib/evidence.ts; the API stays the authority). When the API refuses the
// proof sent, a line under the fields says so and the section opens. Sized for a phone: 52 px number fields, 44 px
// secondary controls, and 16 px text or more in every control so a phone does not zoom in.

interface ProofSectionProps {
  copy: ProofStrings
  evidence: EvidenceDraft
  answers: EvidenceAnswers
  problems: EvidenceProblems
  /** The dispatch date's window for the date picker (dispatchDateBounds). */
  dateBounds: { min: string | undefined; max: string | undefined }
  disabled: boolean
  /** The line shown when the API refused the proof sent, or null. */
  refused: string | null
  /** True when the API runs with demo tools (useDemoTools): only then is "Fill demo values" offered. */
  demo: boolean
  onDemo: () => void
  /**
   * The test hooks' prefix: `<testId>-toggle`, `-doc-type` (the "other document" control), `-doc-id`, `-dispatch`,
   * `-cert-type`, `-cert-id`, `-error`, `-refused`, `-demo` and `-demo-tag`.
   */
  testId: string
  onChange: (field: EvidenceField, value: string) => void
}

export function ProofSection({
  copy,
  evidence,
  answers,
  problems,
  dateBounds,
  disabled,
  refused,
  demo,
  onDemo,
  testId,
  onChange,
}: ProofSectionProps) {
  const regionId = useId()
  // Collapsed on a fresh page; open when the proof is kept from before a reload, so the supplier sees what is there.
  const [open, setOpen] = useState(() => !isEmptyEvidence(evidence) || refused !== null)
  // A refusal opens the section (state adjusted while rendering, on the prop's change, as React recommends over an effect).
  const [shownRefusal, setShownRefusal] = useState(refused)
  if (refused !== shownRefusal) {
    setShownRefusal(refused)
    if (refused !== null) setOpen(true)
  }
  if (!proofApplies(answers)) return null

  const docType = shownDocType(evidence.docType, answers.transport)
  const certType = shownCertType(evidence.certType, answers.energy)
  const lines = problemLines(problems, docType, copy.problems)
  const otherDocs = docTypesFor(answers.transport).filter((type) => type !== docType)
  const otherCerts = CERT_TYPES.filter((type) => type !== certType)
  const tags = demoTags(evidence, answers)
  const tag = (field: 'docId' | 'certId') =>
    tags[field] && (
      <span
        data-testid={`${testId}-demo-tag`}
        data-field={field}
        className="rounded-full bg-fill px-2 py-0.5 text-[13px] font-medium text-ink-2"
      >
        {copy.demoTag}
      </span>
    )

  return (
    <section className="flex flex-col gap-2">
      <button
        type="button"
        aria-expanded={open}
        aria-controls={regionId}
        data-testid={`${testId}-toggle`}
        onClick={() => setOpen((value) => !value)}
        className="flex min-h-[52px] w-full items-center justify-between gap-3 rounded-[14px] px-1 text-left text-[17px] font-semibold tracking-[-0.015em] text-verified-ink transition-colors hover:bg-fill focus-visible:outline-offset-2"
      >
        <span>{copy.toggle}</span>
        <ChevronDownIcon
          aria-hidden
          strokeWidth={2.4}
          className={cn('size-5 shrink-0 transition-transform duration-300', open && 'rotate-180')}
        />
      </button>

      <div id={regionId} hidden={!open}>
        <div className="flex flex-col gap-4 rounded-[14px] bg-card p-4 shadow-[0_0_0_0.5px_rgb(0_0_0/0.08)]">
          <p className="text-[15px] leading-relaxed text-ink-2">{copy.why}</p>

          {docType !== null && (
            <NumberField
              label={copy.docTypes[docType]}
              tag={tag('docId')}
              other={
                otherDocs.length === 0 ? null : (
                  <OtherChoice
                    label={copy.otherDoc}
                    options={otherDocs.map((type) => [type, copy.docTypes[type]])}
                    disabled={disabled}
                    testId={`${testId}-doc-type`}
                    onChange={(value) => onChange('docType', value)}
                  />
                )
              }
              value={evidence.docId}
              problem={lines.docId}
              field="docId"
              maxLength={40}
              disabled={disabled}
              testId={testId}
              inputTestId={`${testId}-doc-id`}
              onChange={(value) => onChange('docId', value)}
            >
              <DateLine
                label={copy.dispatchDate}
                value={evidence.dispatchDate}
                problem={lines.dispatchDate}
                bounds={dateBounds}
                disabled={disabled}
                testId={testId}
                inputTestId={`${testId}-dispatch`}
                onChange={(value) => onChange('dispatchDate', value)}
              />
            </NumberField>
          )}

          {certType !== null && (
            <NumberField
              label={copy.certTypes[certType]}
              tag={tag('certId')}
              other={
                otherCerts.length === 0 ? null : (
                  <OtherChoice
                    label={copy.otherCert}
                    options={otherCerts.map((type) => [type, copy.certTypes[type]])}
                    disabled={disabled}
                    testId={`${testId}-cert-type`}
                    onChange={(value) => onChange('certType', value)}
                  />
                )
              }
              value={evidence.certId}
              problem={lines.certId}
              field="certId"
              maxLength={80}
              disabled={disabled}
              testId={testId}
              inputTestId={`${testId}-cert-id`}
              onChange={(value) => onChange('certId', value)}
            />
          )}

          {demo && (
            <Button
              type="button"
              variant="outline"
              data-testid={`${testId}-demo`}
              className="h-11 w-full text-[16px]"
              disabled={disabled}
              onClick={onDemo}
            >
              {copy.demoFill}
            </Button>
          )}
        </div>
      </div>

      {refused !== null && (
        <p
          role="alert"
          data-testid={`${testId}-refused`}
          className="rounded-lg bg-estimated-tint px-3 py-2 text-[15px] leading-snug text-estimated-ink"
        >
          {refused}
        </p>
      )}
    </section>
  )
}

const FIELD =
  'h-[52px] w-full rounded-[12px] bg-grouped px-3 text-[17px] text-foreground outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-60 aria-invalid:shadow-[0_0_0_1.5px_var(--status-estimated)]'

function ProblemLine({ id, problem, field, testId }: { id: string; problem: string; field: EvidenceField; testId: string }) {
  return (
    <p id={id} data-testid={`${testId}-error`} data-field={field} className="px-1 text-[15px] leading-snug text-estimated-ink">
      {problem}
    </p>
  )
}

interface NumberFieldProps {
  /** The document or certificate type, which labels the field. */
  label: string
  tag: ReactNode
  /** The "other document" control, or null when no other type fits. */
  other: ReactNode | null
  value: string
  problem: string | null
  /** Names the field on its problem line (`data-field`), as the error hooks share one test id. */
  field: EvidenceField
  maxLength: number
  disabled: boolean
  /** The section's test prefix, for the problem line's `<testId>-error`. */
  testId: string
  inputTestId: string
  onChange: (value: string) => void
  /** A secondary line under the field (the dispatch date). */
  children?: ReactNode
}

function NumberField({
  label,
  tag,
  other,
  value,
  problem,
  field,
  maxLength,
  disabled,
  testId,
  inputTestId,
  onChange,
  children,
}: NumberFieldProps) {
  const id = useId()
  const problemId = useId()
  return (
    <div className="flex flex-col gap-1.5">
      {/* The type, its tag and the "other" control on one line, the control wrapping under them on a narrow card. */}
      <div className="flex min-h-11 flex-wrap items-center gap-x-2 gap-y-1 px-1">
        <label htmlFor={id} className="text-[15px] font-semibold">
          {label}
        </label>
        {tag}
        {other !== null && <span className="ml-auto">{other}</span>}
      </div>
      <input
        id={id}
        data-testid={inputTestId}
        value={value}
        maxLength={maxLength}
        disabled={disabled}
        autoComplete="off"
        // A document number is stored uppercase; a certificate id is kept as written.
        autoCapitalize={field === 'docId' ? 'characters' : 'off'}
        spellCheck={false}
        aria-invalid={problem !== null}
        aria-describedby={problem === null ? undefined : problemId}
        onChange={(event) => onChange(event.target.value)}
        className={FIELD}
      />
      {problem !== null && <ProblemLine id={problemId} problem={problem} field={field} testId={testId} />}
      {children}
    </div>
  )
}

interface OtherChoiceProps {
  label: string
  options: [string, string][]
  disabled: boolean
  testId: string
  onChange: (value: string) => void
}

/**
 * A small "Other document" pill over a transparent native select that lists the other types: picking one changes the
 * field's label. The pill is sized by its own words (a visible select would take its longest option's width and push
 * past a phone's edge); the select on top keeps the browser's own picker, keyboard and screen-reader behaviour.
 */
function OtherChoice({ label, options, disabled, testId, onChange }: OtherChoiceProps) {
  return (
    <span className="relative inline-flex h-11 items-center gap-1 rounded-full bg-fill pr-2.5 pl-3 text-[16px] font-medium text-verified-ink has-[select:disabled]:opacity-60 has-focus-visible:ring-2 has-focus-visible:ring-ring">
      {label}
      <ChevronDownIcon aria-hidden strokeWidth={2.4} className="size-4" />
      <select
        aria-label={label}
        data-testid={testId}
        value=""
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
        className="absolute inset-0 cursor-pointer opacity-0 disabled:cursor-default"
      >
        <option value="" disabled hidden>
          {label}
        </option>
        {options.map(([value, text]) => (
          <option key={value} value={value}>
            {text}
          </option>
        ))}
      </select>
    </span>
  )
}

interface DateLineProps {
  label: string
  value: string
  problem: string | null
  bounds: { min: string | undefined; max: string | undefined }
  disabled: boolean
  testId: string
  inputTestId: string
  onChange: (value: string) => void
}

/** The dispatch date, secondary: its label and a compact date input on one line under the number. */
function DateLine({ label, value, problem, bounds, disabled, testId, inputTestId, onChange }: DateLineProps) {
  const id = useId()
  const problemId = useId()
  return (
    <>
      <div className="flex min-h-11 flex-wrap items-center justify-between gap-x-3 gap-y-1 px-1">
        <label htmlFor={id} className="text-[15px] whitespace-nowrap text-ink-2">
          {label}
        </label>
        <input
          id={id}
          type="date"
          data-testid={inputTestId}
          value={value}
          min={bounds.min}
          max={bounds.max}
          disabled={disabled}
          aria-invalid={problem !== null}
          aria-describedby={problem === null ? undefined : problemId}
          onChange={(event) => onChange(event.target.value)}
          className="h-11 rounded-[10px] bg-grouped px-2 text-[16px] text-foreground outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-60 aria-invalid:shadow-[0_0_0_1.5px_var(--status-estimated)]"
        />
      </div>
      {problem !== null && <ProblemLine id={problemId} problem={problem} field="dispatchDate" testId={testId} />}
    </>
  )
}
