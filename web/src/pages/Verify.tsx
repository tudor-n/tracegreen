import { type ReactNode, useEffect, useReducer } from 'react'
import { useSearchParams } from 'react-router'
import { Wordmark } from '@/components/brand/Wordmark'
import { Button } from '@/components/ui/button'
import { AnswerGroup } from '@/components/verify/AnswerGroup'
import { LanguageHeader } from '@/components/verify/LanguageHeader'
import { ReconfirmBanner, ResultMessage } from '@/components/verify/ResultMessage'
import { TermsConsent } from '@/components/verify/TermsConsent'
import { verifyCopy } from '@/copy/verify'
import { verifyApi } from '@/lib/verify-api'
import {
  canSend,
  canSubmit,
  formatIssueDate,
  initialVerifyState,
  offeredEnergy,
  offeredModes,
  reconfirmLines,
  resolveLang,
  submitBody,
  verifyReducer,
} from '@/lib/verify-state'

// The supplier's phone page. It reads the token from ?t= and the language from ?lang=, loads the link's context,
// offers both questions pre-selected, asks for the supplier terms with an unticked checkbox whose statement links to
// the full text, and sends the answers with the terms' SHA-256 once the supplier ticked it. No cookies, storage, login, email, camera, clipboard or
// personal data, and nothing here logs the URL or the token. Strings come from web/src/copy/verify.ts, the decisions
// from web/src/lib/verify-state.ts and the requests from web/src/lib/verify-api.ts; this page performs and renders them.

export default function Verify() {
  const [params] = useSearchParams()
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
    void verifyApi.context(token).then((reply) => {
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
    void verifyApi.submit(submitBody(token, state.answers, state.context.terms.sha256)).then((reply) => {
      if (!current) return
      dispatch(reply.ok ? { kind: 'submitted', outcome: reply.data } : { kind: 'failed', failure: reply.failure })
    })
    return () => {
      current = false
    }
  }, [state, token])

  // How the page ended, for the Playwright smoke: `thanks` or the failure, never the token.
  let result: string | undefined
  if (state.phase === 'done') result = 'thanks'
  else if (state.phase === 'failed') result = state.failure

  // While sending, the box was ticked: the reducer sends nothing otherwise.
  const accepted = state.phase === 'submitting' || (state.phase === 'ready' && state.accepted)

  return (
    <main
      data-testid="verify-page"
      data-phase={state.phase}
      data-result={result}
      className="mx-auto flex min-h-dvh w-full max-w-md flex-col gap-6 px-4 pt-3"
    >
      <LanguageHeader lang={lang} />

      {state.phase === 'loading' && (
        <p role="status" aria-busy="true" className="flex flex-col items-center gap-4 py-24 text-center text-[17px] text-muted-foreground">
          <Wordmark size={28} variant="loader" label={null} />
          {copy.loading}
        </p>
      )}

      {(state.phase === 'ready' || state.phase === 'submitting') && (
        <>
          <div className="flex flex-col gap-2 px-1">
            <h1 className="font-display text-[28px] leading-tight font-bold tracking-[-0.025em]">{copy.title}</h1>
            <p className="text-[15px] leading-relaxed text-ink-2">{copy.intro(state.context.buyerName)}</p>
          </div>

          <dl className="rounded-[14px] bg-card px-4 text-[15px] shadow-[0_0_0_0.5px_rgb(0_0_0/0.08)] [&>div+div]:shadow-[inset_0_0.5px_0_var(--hairline)]">
            <Fact label={copy.labels.supplier}>{state.context.supplierLabel}</Fact>
            <Fact label={copy.labels.item}>{state.context.itemDescription}</Fact>
            <Fact label={copy.labels.invoice}>
              {state.context.invoiceNumber} · {formatIssueDate(state.context.issueDate, lang)}
            </Fact>
            <Fact label={copy.labels.route}>
              {state.context.origin} → {state.context.dest}
            </Fact>
          </dl>

          <AnswerGroup
            question={copy.transport.question}
            options={offeredModes(state.context).map((mode) => ({ value: mode, label: copy.transport.options[mode] }))}
            selected={state.answers.transport}
            skipLabel={copy.skip}
            testId="verify-transport"
            disabled={state.phase === 'submitting'}
            onChange={(value) => dispatch({ kind: 'transport', value })}
          />
          <AnswerGroup
            question={copy.energy.question}
            options={offeredEnergy(state.context).map((source) => ({ value: source, label: copy.energy.options[source] }))}
            selected={state.answers.energy}
            skipLabel={copy.skip}
            testId="verify-energy"
            disabled={state.phase === 'submitting'}
            onChange={(value) => dispatch({ kind: 'energy', value })}
          />

          <TermsConsent
            acceptance={state.context.terms[lang]}
            lang={lang}
            accepted={accepted}
            disabled={state.phase === 'submitting'}
            copy={copy.terms}
            onChange={(accepted) => dispatch({ kind: 'terms', accepted })}
          />

          <div className="sticky bottom-0 -mx-4 mt-auto flex flex-col gap-2 bg-background/85 px-4 pt-3 pb-7 shadow-[0_-0.5px_0_var(--hairline)] backdrop-blur-xl backdrop-saturate-[1.8]">
            {!canSubmit(state.answers) ? (
              <p role="status" className="text-center text-[15px] text-ink-2">
                {copy.answerOne}
              </p>
            ) : (
              !accepted && (
                <p role="status" className="text-center text-[15px] text-ink-2">
                  {copy.terms.tickToSubmit}
                </p>
              )
            )}
            <Button
              data-testid="verify-submit"
              className="h-[52px] w-full text-[17px]"
              disabled={state.phase === 'submitting' || !canSend(state.answers, accepted)}
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
            <Button className="mt-2 h-[52px] w-full text-[17px]" onClick={() => dispatch({ kind: 'retry' })}>
              {copy.retry}
            </Button>
          )}
        </ResultMessage>
      )}
    </main>
  )
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-4 py-3">
      <dt className="shrink-0 text-muted-foreground">{label}</dt>
      <dd className="text-right leading-snug font-medium break-words">{children}</dd>
    </div>
  )
}
