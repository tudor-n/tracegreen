import { useEffect, useReducer } from 'react'
import { useSearchParams } from 'react-router'
import { Wordmark } from '@/components/brand/Wordmark'
import { DeliveryCard } from '@/components/supplier/DeliveryCard'
import { Button } from '@/components/ui/button'
import { LanguageHeader } from '@/components/verify/LanguageHeader'
import { ResultMessage } from '@/components/verify/ResultMessage'
import { TermsConsent } from '@/components/verify/TermsConsent'
import { supplierCopy } from '@/copy/supplier'
import { type Lang, verifyCopy } from '@/copy/verify'
import { supplierApi } from '@/lib/supplier-api'
import {
  answerBody,
  confirmedCount,
  initialSupplierState,
  isOpenForAnswers,
  supplierReducer,
  termsAccepted,
} from '@/lib/supplier-state'
import { resolveLang } from '@/lib/verify-state'

// The supplier page (F12): one link, every delivery from this supplier to the buyer who sent it, each confirmed with the
// two questions of /verify and sent once the supplier ticked the supplier terms: one box above the list, never ticked
// for them, as on /verify. It reads the token from ?t= and the language from ?lang=. Like /verify it sets no cookies
// and uses no storage, login, email, camera, clipboard or personal data, and nothing here logs the URL or the token.
// Strings come from web/src/copy/supplier.ts (and the questions from ./verify.ts), the decisions from
// web/src/lib/supplier-state.ts and the requests from web/src/lib/supplier-api.ts; this page performs and renders them.

export default function Supplier() {
  const [params] = useSearchParams()
  const token = params.get('t')
  const lang = resolveLang(params.get('lang'))
  const copy = supplierCopy[lang]
  const questions = verifyCopy[lang]
  const [state, dispatch] = useReducer(supplierReducer, token, initialSupplierState)

  useEffect(() => {
    document.documentElement.lang = lang
  }, [lang])

  // The list: on first load, on a retry, and on "reload the list". A reply that arrives after the page moved on is dropped.
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

  // The open delivery's answers, once per send: the reducer marks the draft as sending once and ignores taps until it ends,
  // and sends nothing unless the box is ticked for the terms on screen, whose SHA-256 goes with the answers.
  const sending = state.phase === 'ready' && state.draft?.sending === true ? state.draft : null
  const accepted = state.phase === 'ready' ? state.accepted : null
  useEffect(() => {
    if (sending === null || token === null || accepted === null) return
    let current = true
    const { nodeId } = sending
    void supplierApi.answer(answerBody(token, sending, accepted)).then((reply) => {
      if (!current) return
      dispatch(
        reply.ok
          ? { kind: 'answered', nodeId, result: reply.data }
          : { kind: 'answerFailed', nodeId, failure: reply.failure },
      )
    })
    return () => {
      current = false
    }
  }, [sending, token, accepted])

  const ticked = state.phase === 'ready' && termsAccepted(state)

  return (
    <main
      data-testid="supplier-page"
      data-phase={state.phase}
      data-result={state.phase === 'failed' ? state.failure : undefined}
      className="mx-auto flex min-h-dvh w-full max-w-md flex-col gap-6 px-4 pt-3 pb-8"
    >
      <LanguageHeader lang={lang} />

      {state.phase === 'loading' && (
        <p role="status" aria-busy="true" className="flex flex-col items-center gap-4 py-24 text-center text-[17px] text-muted-foreground">
          <Wordmark size={28} variant="loader" label={null} />
          {questions.loading}
        </p>
      )}

      {state.phase === 'ready' && (
        <>
          <div className="flex flex-col gap-2 px-1">
            <h1 className="font-display text-[28px] leading-tight font-bold tracking-[-0.025em]">{copy.title(state.view.buyerName)}</h1>
            <p data-testid="supplier-progress" className="text-[15px] font-semibold">
              {state.view.supplierLabel} · {progressLine(copy, state.view)}
            </p>
            <p className="text-[15px] leading-relaxed text-ink-2">{copy.intro}</p>
          </div>

          {/* One box for the page, shown while a delivery is still open for answers; each answer carries its SHA-256. */}
          {state.view.items.some(isOpenForAnswers) && (
            <TermsConsent
              acceptance={state.view.terms[lang]}
              lang={lang}
              accepted={ticked}
              disabled={state.draft?.sending === true}
              copy={questions.terms}
              onChange={(value) => dispatch({ kind: 'terms', accepted: value })}
              testId="supplier-terms"
            />
          )}

          {state.view.items.length === 0 ? (
            <p role="status" className="rounded-[18px] bg-card p-6 text-center text-[17px] text-muted-foreground shadow-card">
              {copy.empty}
            </p>
          ) : (
            <ul className="flex flex-col gap-3">
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
                    termsAccepted={ticked}
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
            <Button className="mt-2 h-[52px] w-full text-[17px]" onClick={() => dispatch({ kind: 'reload' })}>
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
