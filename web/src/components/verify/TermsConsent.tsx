import type { TermsText } from '@/api/client'
import type { Lang, VerifyStrings } from '@/copy/verify'
import { cn } from '@/lib/utils'
import { TermsSections } from './TermsSections'

// The supplier terms above the submit button: the key points, the full text one tap away, a printable copy at a stable
// address without the link's token, and the checkbox the supplier ticks themselves. Its statement names the clauses that
// need express acceptance (Civil Code art. 1203); it starts unticked and the page never ticks it.

interface TermsConsentProps {
  terms: TermsText
  version: string
  lang: Lang
  accepted: boolean
  disabled: boolean
  copy: VerifyStrings['terms']
  onChange: (accepted: boolean) => void
}

export function TermsConsent({ terms, version, lang, accepted, disabled, copy, onChange }: TermsConsentProps) {
  return (
    <section aria-labelledby="verify-terms-title" className="flex flex-col gap-3 rounded-xl border bg-card p-4">
      <div className="flex flex-col gap-0.5">
        <h2 id="verify-terms-title" className="text-lg leading-snug font-semibold">
          {terms.title}
        </h2>
        <p className="text-sm text-muted-foreground">{copy.version(version)}</p>
      </div>

      <ul className="flex list-disc flex-col gap-1.5 pl-5 text-base">
        {terms.keyPoints.map((point) => (
          <li key={point}>{point}</li>
        ))}
      </ul>

      <details data-testid="verify-terms-read" className="group rounded-lg border px-3 py-2">
        <summary className="flex min-h-12 cursor-pointer items-center text-base font-semibold underline-offset-4 group-open:mb-2 hover:underline">
          {copy.read}
        </summary>
        <TermsSections sections={terms.sections} />
      </details>

      {/* A separate page, so a printout or a saved copy never carries the link's token (no-referrer keeps it out of the request too). */}
      <a
        href={`/terms/supplier?lang=${lang}`}
        target="_blank"
        rel="noopener noreferrer"
        className="flex min-h-12 items-center text-base font-semibold text-primary underline underline-offset-4"
      >
        {copy.printable}
      </a>

      <label
        className={cn(
          'flex min-h-14 cursor-pointer items-start gap-3 rounded-xl border-2 p-3 text-base leading-snug has-disabled:cursor-default has-disabled:opacity-60',
          accepted ? 'border-primary bg-primary/5' : 'border-border',
        )}
      >
        <input
          type="checkbox"
          data-testid="verify-terms"
          checked={accepted}
          disabled={disabled}
          onChange={(event) => onChange(event.target.checked)}
          className="mt-0.5 size-6 shrink-0 accent-primary"
        />
        <span>{terms.acceptance}</span>
      </label>
    </section>
  )
}
