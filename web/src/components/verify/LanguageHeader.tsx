import { useSearchParams } from 'react-router'
import { Wordmark } from '@/components/brand/Wordmark'
import { type Lang, verifyCopy } from '@/copy/verify'
import { cn } from '@/lib/utils'

// The header of the supplier-facing pages (/verify/:nodeId, /supplier and /terms/supplier): the Logo 5 wordmark and a capsule toggle to the
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
    <header className={cn('flex min-h-14 items-center justify-between gap-3 pl-1', className)}>
      <Wordmark size={17} />
      <button
        type="button"
        onClick={switchLanguage}
        className="min-h-11 rounded-full bg-fill-strong px-4 text-[13px] font-medium outline-none transition-colors hover:bg-[#d8d8de] focus-visible:ring-3 focus-visible:ring-ring"
      >
        <span className="sr-only">{copy.switchLabel}: </span>
        <span lang={other}>{copy.switchTo}</span>
      </button>
    </header>
  )
}
