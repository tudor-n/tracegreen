import type { TermsAcceptance } from '@/api/client'
import type { Lang, VerifyStrings } from '@/copy/verify'
import { cn } from '@/lib/utils'

// The supplier terms above the submit button: one checkbox the supplier ticks themselves, whose statement names the
// clauses that need express acceptance (Civil Code art. 1203) and links the words "Supplier Terms" to the full text.
// The link opens the printable page in a new tab, so the answers and the tick stay put, and a printout or saved copy
// never carries the link's token (no-referrer keeps it out of the request too). The box starts unticked and the page
// never ticks it. The supplier page (/supplier, F12) shows the same box once, above its list of deliveries.

interface TermsConsentProps {
  /** The checkbox statement in the language shown: `before + link + after`, `link` being the link to the full text. */
  acceptance: TermsAcceptance
  lang: Lang
  accepted: boolean
  disabled: boolean
  copy: VerifyStrings['terms']
  onChange: (accepted: boolean) => void
  /** The checkbox's test hook; its link's is `<testId>-link`. */
  testId?: string
}

export function TermsConsent({
  acceptance,
  lang,
  accepted,
  disabled,
  copy,
  onChange,
  testId = 'verify-terms',
}: TermsConsentProps) {
  const { before, link, after } = acceptance
  return (
    <label
      className={cn(
        'flex min-h-14 cursor-pointer items-start gap-3 rounded-xl border-2 p-3 text-base leading-snug has-disabled:cursor-default has-disabled:opacity-60',
        accepted ? 'border-primary bg-primary/5' : 'border-border',
      )}
    >
      <input
        type="checkbox"
        data-testid={testId}
        checked={accepted}
        disabled={disabled}
        onChange={(event) => onChange(event.target.checked)}
        className="mt-0.5 size-6 shrink-0 accent-primary"
      />
      <span>
        {before}
        {/* A link inside a label opens the link; it does not tick the box. */}
        <a
          href={`/terms/supplier?lang=${lang}`}
          target="_blank"
          rel="noopener noreferrer"
          data-testid={`${testId}-link`}
          className="font-semibold text-primary underline underline-offset-4"
        >
          {link}
          <span className="sr-only"> ({copy.opensInNewTab})</span>
        </a>
        {after}
      </span>
    </label>
  )
}
