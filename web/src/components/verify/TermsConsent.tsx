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
        'flex min-h-14 cursor-pointer items-start gap-3.5 rounded-[14px] p-4 text-[15px] leading-relaxed transition-[background-color,box-shadow] duration-300 has-disabled:cursor-default has-disabled:opacity-60',
        accepted ? 'bg-verified-tint shadow-[0_0_0_1px_rgb(18_129_79/0.35)]' : 'bg-card shadow-[0_0_0_0.5px_rgb(0_0_0/0.08)]',
      )}
    >
      <input
        type="checkbox"
        data-testid={testId}
        checked={accepted}
        disabled={disabled}
        onChange={(event) => onChange(event.target.checked)}
        className="mt-0.5 size-[22px] shrink-0 accent-primary"
      />
      <span>
        {before}
        {/* A link inside a label opens the link; it does not tick the box. */}
        <a
          href={`/terms/supplier?lang=${lang}`}
          target="_blank"
          rel="noopener noreferrer"
          data-testid={`${testId}-link`}
          className="font-semibold text-verified-ink underline decoration-verified/40 underline-offset-4 hover:decoration-verified"
        >
          {link}
          <span className="sr-only"> ({copy.opensInNewTab})</span>
        </a>
        {after}
      </span>
    </label>
  )
}
