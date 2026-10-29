import { useEffect, useReducer } from 'react'
import { useSearchParams } from 'react-router'
import { Button } from '@/components/ui/button'
import { AnswerGroup } from '@/components/verify/AnswerGroup'
import { ReconfirmBanner, ResultMessage } from '@/components/verify/ResultMessage'
import { dashboardTitle } from '@/copy/dashboard'
import { type Lang, verifyCopy } from '@/copy/verify'
import { fixtureApi } from '@/lib/verify-fixture'
import {
  canSubmit,
  formatIssueDate,
  initialVerifyState,
  offeredEnergy,
  offeredModes,
  reconfirmLines,
  resolveLang,
  submitBody,
  type VerifyApi,
  verifyReducer,
} from '@/lib/verify-state'

// The supplier's phone page. It reads the token from ?t= and the language from ?lang=, loads the link's context,
// offers both questions pre-selected and sends the answers. No cookies, storage, login, email, camera, clipboard or
// personal data, and nothing here logs the URL or the token. Strings come from web/src/copy/verify.ts; the decisions
// from web/src/lib/verify-state.ts, which this page only performs and renders.

// C5 step 2 replaces the fixture with the API through web/src/api/client.ts.
const api: VerifyApi = fixtureApi

export default function Verify() {
  const [params, setParams] = useSearchParams()
  const token = params.get('t')
  const lang = resolveLang(params.get('lang'))
  const copy = verifyCopy[lang]
  const [state, dispatch] = useReducer(verifyReducer, token, initialVerifyState)

  useEffect(() => {
    document.documentElement.lang = lang
  }, [lang])

  // The context request, on first load and on retry. A reply that arrives after the page moved on is dropped.
  useEffect(() => {
    if (state.phase !== 'loading' || token === null) return
    let current = true
    void api.context(token).then((reply) => {
      if (!current) return
      dispatch(reply.ok ? { kind: 'contextLoaded', context: reply.data } : { kind: 'failed', failure: reply.failure })
    })
    return () => {
      current = false
    }
  }, [state.phase, token])

  // The answers, once per submit or retry: the reducer enters 'submitting' once and ignores taps until it leaves.
  useEffect(() => {
    if (state.phase !== 'submitting' || token === null) return
    let current = true
    void api.submit(submitBody(token, state.answers)).then((reply) => {
      if (!current) return
      dispatch(reply.ok ? { kind: 'submitted', outcome: reply.data } : { kind: 'failed', failure: reply.failure })
    })
    return () => {
      current = false
    }
  }, [state, token])

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
    <main className="mx-auto flex min-h-dvh w-full max-w-md flex-col gap-6 px-4 py-5">
      <header className="flex items-center justify-between gap-3">
        <span className="text-base font-semibold tracking-wide">{dashboardTitle}</span>
        <button
          type="button"
          onClick={switchLanguage}
          className="min-h-12 rounded-xl border-2 border-border px-4 text-base font-semibold outline-none hover:bg-muted focus-visible:ring-3 focus-visible:ring-ring/50"
        >
          <span className="sr-only">{copy.language.switchLabel}: </span>
          <span lang={other}>{copy.language.switchTo}</span>
        </button>
      </header>

      {state.phase === 'loading' && (
        <p role="status" aria-busy="true" className="py-16 text-center text-lg text-muted-foreground">
          {copy.loading}
        </p>
      )}

      {(state.phase === 'ready' || state.phase === 'submitting') && (
        <>
          <div className="flex flex-col gap-2">
            <h1 className="text-2xl font-bold">{copy.title}</h1>
            <p className="text-base">{copy.intro(state.context.buyerName)}</p>
          </div>

          <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1.5 rounded-xl border bg-card p-4 text-base">
            <dt className="text-muted-foreground">{copy.labels.supplier}</dt>
            <dd className="font-semibold">{state.context.supplierLabel}</dd>
            <dt className="text-muted-foreground">{copy.labels.item}</dt>
            <dd className="break-words">{state.context.itemDescription}</dd>
            <dt className="text-muted-foreground">{copy.labels.invoice}</dt>
            <dd>
              {state.context.invoiceNumber} · {formatIssueDate(state.context.issueDate, lang)}
            </dd>
            <dt className="text-muted-foreground">{copy.labels.route}</dt>
            <dd>
              {state.context.origin} → {state.context.dest}
            </dd>
          </dl>

          <AnswerGroup
            question={copy.transport.question}
            options={offeredModes(state.context).map((mode) => ({ value: mode, label: copy.transport.options[mode] }))}
            selected={state.answers.transport}
            skipLabel={copy.skip}
            disabled={state.phase === 'submitting'}
            onChange={(value) => dispatch({ kind: 'transport', value })}
          />
          <AnswerGroup
            question={copy.energy.question}
            options={offeredEnergy(state.context).map((source) => ({ value: source, label: copy.energy.options[source] }))}
            selected={state.answers.energy}
            skipLabel={copy.skip}
            disabled={state.phase === 'submitting'}
            onChange={(value) => dispatch({ kind: 'energy', value })}
          />

          <div className="flex flex-col gap-2 pb-4">
            {!canSubmit(state.answers) && (
              <p role="status" className="text-center text-base text-muted-foreground">
                {copy.answerOne}
              </p>
            )}
            <Button
              className="h-14 w-full rounded-xl text-lg"
              disabled={state.phase === 'submitting' || !canSubmit(state.answers)}
              onClick={() => dispatch({ kind: 'submit' })}
            >
              {state.phase === 'submitting' ? copy.submitting : copy.submit}
            </Button>
          </div>
        </>
      )}

      {state.phase === 'done' && (
        <>
          <ResultMessage tone="success" title={copy.results.thanks.title} body={copy.results.thanks.body} />
          {state.warnings.length > 0 && (
            <ReconfirmBanner
              title={copy.reconfirm.title}
              lines={reconfirmLines(state.warnings, copy.reconfirm.warnings)}
            />
          )}
        </>
      )}

      {state.phase === 'failed' && (
        <ResultMessage
          tone={state.retry === null ? 'final' : 'retry'}
          title={copy.results[state.failure].title}
          body={copy.results[state.failure].body}
        >
          {state.retry !== null && (
            <Button className="mt-2 h-14 w-full rounded-xl text-lg" onClick={() => dispatch({ kind: 'retry' })}>
              {copy.retry}
            </Button>
          )}
        </ResultMessage>
      )}
    </main>
  )
}
