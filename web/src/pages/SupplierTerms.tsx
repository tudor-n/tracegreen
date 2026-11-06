import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'
import type { SupplierTerms as Terms } from '@/api/client'
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
      className="mx-auto flex min-h-dvh w-full max-w-2xl flex-col gap-6 px-4 py-5"
    >
      <LanguageHeader lang={lang} className="print:hidden" />

      {load.phase === 'loading' && (
        <p role="status" aria-busy="true" className="py-16 text-center text-lg text-muted-foreground">
          {copy.termsPage.loading}
        </p>
      )}

      {load.phase === 'failed' && (
        <ResultMessage tone="retry" title={copy.termsPage.failed.title} body={copy.termsPage.failed.body}>
          <Button className="mt-2 h-14 w-full rounded-xl text-lg" onClick={() => setLoad({ phase: 'loading' })}>
            {copy.retry}
          </Button>
        </ResultMessage>
      )}

      {load.phase === 'ready' && (
        <article className="flex flex-col gap-5">
          <div className="flex flex-col gap-1">
            <h1 className="text-2xl font-bold">{load.terms[lang].title}</h1>
            <p className="text-sm text-muted-foreground">{copy.terms.version(load.terms.version)}</p>
            <p className="text-sm break-all text-muted-foreground">
              {copy.termsPage.fingerprint}: <span className="font-mono">{load.terms.sha256}</span>
            </p>
          </div>
          <ul className="flex list-disc flex-col gap-1.5 pl-5 text-base">
            {load.terms[lang].keyPoints.map((point) => (
              <li key={point}>{point}</li>
            ))}
          </ul>
          <TermsSections sections={load.terms[lang].sections} />
          <Button className="h-14 w-full rounded-xl text-lg print:hidden" onClick={() => window.print()}>
            {copy.termsPage.print}
          </Button>
        </article>
      )}
    </main>
  )
}
