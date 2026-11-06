import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'
import type { SupplierTerms as Terms } from '@/api/client'
import { Wordmark } from '@/components/brand/Wordmark'
import { Button } from '@/components/ui/button'
import { LanguageHeader } from '@/components/verify/LanguageHeader'
import { ResultMessage } from '@/components/verify/ResultMessage'
import { TermsSections } from '@/components/verify/TermsSections'
import { verifyCopy } from '@/copy/verify'
import { termsApi } from '@/lib/terms-api'
import { resolveLang } from '@/lib/verify-state'

// The supplier terms at a stable address without a token (/terms/supplier, ?lang= as on the supplier page), opened from
// the link in the supplier page's checkbox statement, so a supplier can read, print and save them before accepting. It shows the version and the SHA-256 the audit log records.
// The text comes from the API and is rendered as text.

type Load = { phase: 'loading' } | { phase: 'ready'; terms: Terms } | { phase: 'failed' }

export default function SupplierTerms() {
  const [params] = useSearchParams()
  const lang = resolveLang(params.get('lang'))
  const copy = verifyCopy[lang]
  const [load, setLoad] = useState<Load>({ phase: 'loading' })

  useEffect(() => {
    document.documentElement.lang = lang
  }, [lang])

  useEffect(() => {
    if (load.phase !== 'loading') return
    let current = true
    void termsApi.supplier().then((reply) => {
      if (current) setLoad(reply.ok ? { phase: 'ready', terms: reply.data } : { phase: 'failed' })
    })
    return () => {
      current = false
    }
  }, [load.phase])

  return (
    <main
      data-testid="terms-page"
      data-phase={load.phase}
      data-version={load.phase === 'ready' ? load.terms.version : undefined}
      className="mx-auto flex min-h-dvh w-full max-w-2xl flex-col gap-6 px-4 pt-3 pb-10"
    >
      <LanguageHeader lang={lang} className="print:hidden" />

      {load.phase === 'loading' && (
        <p role="status" aria-busy="true" className="flex flex-col items-center gap-4 py-24 text-center text-[17px] text-muted-foreground">
          <Wordmark size={28} variant="loader" label={null} />
          {copy.termsPage.loading}
        </p>
      )}

      {load.phase === 'failed' && (
        <ResultMessage tone="retry" title={copy.termsPage.failed.title} body={copy.termsPage.failed.body}>
          <Button className="mt-2 h-[52px] w-full text-[17px]" onClick={() => setLoad({ phase: 'loading' })}>
            {copy.retry}
          </Button>
        </ResultMessage>
      )}

      {load.phase === 'ready' && (
        <article className="flex flex-col gap-6">
          <div className="flex flex-col gap-1.5 px-1">
            <h1 className="font-display text-[28px] leading-tight font-bold tracking-[-0.025em]">{load.terms[lang].title}</h1>
            <p className="text-[13px] text-muted-foreground">{copy.terms.version(load.terms.version)}</p>
            <p className="text-[13px] break-all text-muted-foreground">
              {copy.termsPage.fingerprint}: <span className="font-mono text-[12px]">{load.terms.sha256}</span>
            </p>
          </div>
          <ul className="flex list-disc flex-col gap-2 rounded-[18px] bg-card py-4 pr-5 pl-9 text-[15px] leading-relaxed shadow-card marker:text-verified print:shadow-none">
            {load.terms[lang].keyPoints.map((point) => (
              <li key={point}>{point}</li>
            ))}
          </ul>
          <div className="rounded-[18px] bg-card p-6 shadow-card print:p-0 print:shadow-none">
            <TermsSections sections={load.terms[lang].sections} />
          </div>
          <Button variant="outline" className="h-[52px] w-full text-[17px] print:hidden" onClick={() => window.print()}>
            {copy.termsPage.print}
          </Button>
        </article>
      )}
    </main>
  )
}
