import { useEffect, useReducer } from 'react'
import { useSearchParams } from 'react-router'
import { DeliveryCard } from '@/components/supplier/DeliveryCard'
import { Button } from '@/components/ui/button'
import { ResultMessage } from '@/components/verify/ResultMessage'
import { dashboardTitle } from '@/copy/dashboard'
import { supplierCopy } from '@/copy/supplier'
import { type Lang, verifyCopy } from '@/copy/verify'
import { supplierApi } from '@/lib/supplier-api'
import { answerBody, confirmedCount, initialSupplierState, supplierReducer } from '@/lib/supplier-state'
import { resolveLang } from '@/lib/verify-state'

// The supplier page (F12): one link, every delivery from this supplier to the buyer who sent it, each confirmed with the
// two questions of /verify. It reads the token from ?t= and the language from ?lang=. Like /verify it sets no cookies
// and uses no storage, login, email, camera, clipboard or personal data, and nothing here logs the URL or the token.
// Strings come from web/src/copy/supplier.ts (and the questions from ./verify.ts), the decisions from
// web/src/lib/supplier-state.ts and the requests from web/src/lib/supplier-api.ts; this page performs and renders them.

export default function Supplier() {
  const [params, setParams] = useSearchParams()
  const token = params.get('t')
  const lang = resolveLang(params.get('lang'))
  const copy = supplierCopy[lang]
  const questions = verifyCopy[lang]
  const [state, dispatch] = useReducer(supplierReducer, token, initialSupplierState)

  useEffect(() => {
    document.documentElement.lang = lang
  }, [lang])

  // The list: on first load, on a retry, and again after an answer. A reply that arrives after the page moved on is dropped.
  const wantsList = state.phase === 'loading' || (state.phase === 'ready' && state.stale)
  useEffect(() => {
    if (!wantsList || token === null) return
    let current = true
    void supplierApi.view(token).then((reply) => {
      if (!current) return
      dispatch(reply.ok ? { kind: 'loaded', view: reply.data } : { kind: 'loadFailed', failure: reply.failure })
    })
    return () => {
      current = false
    }
  }, [wantsList, token])

  // The open delivery's answers, once per send: the reducer marks the draft as sending once and ignores taps until it ends.
  const sending = state.phase === 'ready' && state.draft?.sending === true ? state.draft : null
  useEffect(() => {
    if (sending === null || token === null) return
    let current = true
    const { nodeId } = sending
    void supplierApi.answer(answerBody(token, sending)).then((reply) => {
      if (!current) return
      dispatch(
        reply.ok
          ? { kind: 'answered', nodeId, outcome: reply.data }
          : { kind: 'answerFailed', nodeId, failure: reply.failure },
      )
    })
    return () => {
      current = false
    }
  }, [sending, token])

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
    <main
      data-testid="supplier-page"
      data-phase={state.phase}
      data-result={state.phase === 'failed' ? state.failure : undefined}
      className="mx-auto flex min-h-dvh w-full max-w-md flex-col gap-5 px-4 py-5"
    >
      <header className="flex items-center justify-between gap-3">
        <span className="text-base font-semibold tracking-wide">{dashboardTitle}</span>
        <button
          type="button"
          onClick={switchLanguage}
          className="min-h-12 rounded-xl border-2 border-border px-4 text-base font-semibold outline-none hover:bg-muted focus-visible:ring-3 focus-visible:ring-ring/50"
        >
          <span className="sr-only">{questions.language.switchLabel}: </span>
          <span lang={other}>{questions.language.switchTo}</span>
        </button>
      </header>

      {state.phase === 'loading' && (
        <p role="status" aria-busy="true" className="py-16 text-center text-lg text-muted-foreground">
          {questions.loading}
        </p>
      )}

      {state.phase === 'ready' && (
        <>
          <div className="flex flex-col gap-2">
            <h1 className="text-2xl font-bold">{copy.title(state.view.buyerName)}</h1>
            <p data-testid="supplier-progress" className="text-base font-semibold">
              {state.view.supplierLabel} · {progressLine(copy, state.view)}
            </p>
            <p className="text-base">{copy.intro}</p>
          </div>

          {state.view.items.length === 0 ? (
            <p role="status" className="rounded-xl border bg-card p-6 text-center text-lg text-muted-foreground">
              {copy.empty}
            </p>
          ) : (
            <ul className="flex flex-col gap-3 pb-4">
              {state.view.items.map((item) => {
                const draft = state.draft?.nodeId === item.nodeId ? state.draft : null
                return (
                  <DeliveryCard
                    key={item.nodeId}
                    item={item}
                    lang={lang}
                    copy={copy}
                    questions={questions}
                    draft={draft}
                    busy={state.draft?.sending === true}
                    warnings={state.warnings[item.nodeId]}
                    onOpen={() => dispatch({ kind: 'open', nodeId: item.nodeId })}
                    onClose={() => dispatch({ kind: 'close' })}
                    onTransport={(value) => dispatch({ kind: 'transport', value })}
                    onEnergy={(value) => dispatch({ kind: 'energy', value })}
                    onSend={() => dispatch({ kind: 'send' })}
                    onReload={() => dispatch({ kind: 'reload' })}
                  />
                )
              })}
            </ul>
          )}
        </>
      )}

      {state.phase === 'failed' && (
        <ResultMessage
          tone={state.retryable ? 'retry' : 'final'}
          title={copy.results[state.failure].title}
          body={copy.results[state.failure].body}
        >
          {state.retryable && (
            <Button className="mt-2 h-14 w-full rounded-xl text-lg" onClick={() => dispatch({ kind: 'reload' })}>
              {questions.retry}
            </Button>
          )}
        </ResultMessage>
      )}
    </main>
  )
}

function progressLine(copy: (typeof supplierCopy)[Lang], view: Parameters<typeof confirmedCount>[0]): string {
  const { confirmed, total } = confirmedCount(view)
  return copy.progress(confirmed, total)
}
