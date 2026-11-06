import { useSearchParams } from 'react-router'
import { dashboardTitle } from '@/copy/dashboard'
import { type Lang, verifyCopy } from '@/copy/verify'
import { cn } from '@/lib/utils'

// The header of the supplier-facing pages (/verify/:nodeId and /terms/supplier): the product name and a toggle to the
// other language. The toggle rewrites ?lang= in place (no history entry) and names the other language in that language,
// so either reader can find it.

export function LanguageHeader({ lang, className }: { lang: Lang; className?: string }) {
  const [, setParams] = useSearchParams()
  const copy = verifyCopy[lang].language
  const other: Lang = lang === 'ro' ? 'en' : 'ro'
  const switchLanguage = () =>
    setParams(
      (current) => {
        const next = new URLSearchParams(current)
        next.set('lang', other)
        return next
      },
      { replace: true },
    )

  return (
    <header className={cn('flex items-center justify-between gap-3', className)}>
      <span className="text-base font-semibold tracking-wide">{dashboardTitle}</span>
      <button
        type="button"
        onClick={switchLanguage}
        className="min-h-12 rounded-xl border-2 border-border px-4 text-base font-semibold outline-none hover:bg-muted focus-visible:ring-3 focus-visible:ring-ring/50"
      >
        <span className="sr-only">{copy.switchLabel}: </span>
        <span lang={other}>{copy.switchTo}</span>
      </button>
    </header>
  )
}
