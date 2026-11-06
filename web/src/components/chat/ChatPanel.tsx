import { XIcon } from 'lucide-react'
import { type FormEvent, useEffect, useReducer, useRef, useState } from 'react'
import { useNavigate } from 'react-router'
import type { ChatAnswer, ChatFactView } from '@/api/client'
import { ExplainedTag, PageMessage } from '@/components/shell/Page'
import { Button } from '@/components/ui/button'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { chatCopy, type TemplateId } from '@/copy/chat'
import { AI_WRITTEN, FROM_YOUR_DATA } from '@/copy/labels'
import { factLabel, factLink, factValue } from '@/lib/chat-display'
import { chatReducer, historyOf, initialChat, isPending, type Turn } from '@/lib/chat-state'
import { templateAnswer } from '@/lib/chat-templates'
import { askChat } from '@/lib/dashboard-actions'
import { cn } from '@/lib/utils'
import { useGraphStore } from '@/store'

// The data chat (F13), opened from the sidebar on every screen. It sits where the side panel sits and covers it;
// closing it brings the side panel back as it was. The conversation stays while the app is open. Typed questions go to
// the API, which has the AI write the sentence and fills every value in from the data after checking it; the suggested
// questions are answered from the store's data with a fixed sentence. Every value is a chip that says what it is.

const MAX_QUESTION = 300
const TEMPLATES = Object.keys(chatCopy.questions) as TemplateId[]

export function ChatPanel() {
  const open = useGraphStore((s) => s.openPanels.chat)
  const setPanel = useGraphStore((s) => s.setPanel)
  const select = useGraphStore((s) => s.select)
  const nodes = useGraphStore((s) => s.nodes)
  const kpis = useGraphStore((s) => s.kpis)
  const navigate = useNavigate()
  const [state, dispatch] = useReducer(chatReducer, initialChat)
  const [draft, setDraft] = useState('')
  const nextId = useRef(1)
  const end = useRef<HTMLDivElement>(null)
  const pending = isPending(state)

  // Escape closes the chat before anything else hears it (the side panel under it would close too).
  useEffect(() => {
    if (!open) return
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !event.defaultPrevented) {
        event.preventDefault()
        setPanel('chat', false)
      }
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  }, [open, setPanel])

  useEffect(() => {
    if (open) end.current?.scrollIntoView({ block: 'nearest' })
  }, [open, state.turns])

  // Returning nothing keeps the conversation: the component stays mounted while the chat is closed.
  if (!open) return null

  const ask = async (question: string) => {
    const id = nextId.current++
    const history = historyOf(state)
    dispatch({ type: 'asked', id, question, asked: 'ai' })
    const answer = await askChat(question, history)
    dispatch(answer.ok ? { type: 'answered', id, answer: answer.data } : { type: 'failed', id, message: answer.message })
  }

  const askTemplate = (template: TemplateId) => {
    const id = nextId.current++
    dispatch({ type: 'asked', id, question: chatCopy.questions[template], asked: 'template' })
    dispatch({ type: 'answered', id, answer: templateAnswer(template, Object.values(nodes), kpis) })
  }

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    const question = draft.trim()
    if (question === '' || pending) return
    setDraft('')
    void ask(question)
  }

  const onLink = (fact: ChatFactView) => {
    const link = factLink(fact)
    if (link === null) return
    if (link.kind === 'node') {
      select(link.nodeId)
      void navigate('/suppliers')
    } else {
      setPanel('chat', false)
      void navigate('/audit')
    }
  }

  return (
    <aside
      data-testid="chat-panel"
      aria-label={chatCopy.title}
      className="fixed inset-0 z-40 flex flex-col gap-4 overflow-y-auto bg-card px-6 pt-6 pb-6 motion-safe:animate-in motion-safe:fade-in-0 motion-safe:slide-in-from-right-6 motion-safe:duration-300 lg:sticky lg:inset-auto lg:top-0 lg:z-auto lg:h-dvh lg:w-[360px] lg:shrink-0 lg:shadow-[-0.5px_0_0_var(--hairline),-18px_0_40px_-30px_rgb(0_0_0/0.35)]"
    >
      <div className="flex items-start gap-3">
        <div className="flex min-w-0 flex-1 flex-col gap-1.5">
          <h2 className="font-display text-[22px] leading-tight font-semibold tracking-[-0.02em]">{chatCopy.title}</h2>
          <p className="text-[13px] text-muted-foreground">{chatCopy.intro}</p>
        </div>
        <button
          type="button"
          aria-label={chatCopy.close}
          onClick={() => setPanel('chat', false)}
          className="inline-flex size-[30px] shrink-0 items-center justify-center rounded-full bg-fill text-ink-2 transition-colors outline-none hover:bg-fill-strong focus-visible:ring-2 focus-visible:ring-ring"
        >
          <XIcon className="size-3.5" strokeWidth={2.6} aria-hidden />
        </button>
      </div>

      <div role="log" aria-live="polite" className="flex flex-1 flex-col gap-4">
        {state.turns.length === 0 && <Suggestions heading={chatCopy.tryThese} onPick={askTemplate} disabled={pending} />}
        {state.turns.map((turn) => (
          <TurnView key={turn.id} turn={turn} onLink={onLink} onPick={askTemplate} pending={pending} />
        ))}
        <div ref={end} />
      </div>

      <form onSubmit={onSubmit} className="flex flex-col gap-1.5">
        <label htmlFor="chat-question" className="sr-only">
          {chatCopy.inputLabel}
        </label>
        <div className="flex items-center gap-2">
          <input
            id="chat-question"
            data-testid="chat-input"
            value={draft}
            maxLength={MAX_QUESTION}
            autoComplete="off"
            placeholder={chatCopy.placeholder}
            onChange={(event) => setDraft(event.target.value)}
            className="h-10 min-w-0 flex-1 rounded-full bg-fill px-4 text-sm outline-none placeholder:text-muted-foreground focus-visible:ring-2 focus-visible:ring-ring"
          />
          <Button type="submit" data-testid="chat-send" disabled={pending || draft.trim() === ''} className="h-10 rounded-full">
            {chatCopy.send}
          </Button>
        </div>
        {draft.length >= MAX_QUESTION && <p className="px-4 text-[11px] text-muted-foreground">{chatCopy.tooLong(MAX_QUESTION)}</p>}
      </form>
    </aside>
  )
}

function TurnView({
  turn,
  onLink,
  onPick,
  pending,
}: {
  turn: Turn
  onLink: (fact: ChatFactView) => void
  onPick: (template: TemplateId) => void
  pending: boolean
}) {
  const { state } = turn
  return (
    <div data-testid={`chat-turn-${turn.id}`} data-phase={state.phase} className="flex flex-col gap-2">
      <p className="max-w-[85%] self-end rounded-2xl bg-fill px-3.5 py-2 text-sm break-words">{turn.question}</p>
      {state.phase === 'pending' && <PageMessage busy>{chatCopy.sending}</PageMessage>}
      {state.phase === 'answered' && <AnswerView answer={state.answer} fromData={state.fromData} onLink={onLink} />}
      {state.phase === 'notAnswered' && (
        <Suggestions heading={chatCopy.notAnswered} sub={chatCopy.tryThese} onPick={onPick} disabled={pending} />
      )}
      {state.phase === 'unavailable' && <Suggestions heading={chatCopy.unavailable} onPick={onPick} disabled={pending} />}
      {state.phase === 'error' && (
        <p role="alert" className="text-[13px] text-destructive">
          {state.message}
        </p>
      )}
    </div>
  )
}

/** One answer: whose sentence it is, the sentence with its value chips, and what each value is. */
function AnswerView({ answer, fromData, onLink }: { answer: ChatAnswer; fromData: boolean; onLink: (fact: ChatFactView) => void }) {
  const byId = new Map(answer.facts.map((f) => [f.id, f]))
  return (
    <div data-testid="chat-answer" className="flex flex-col gap-2.5 rounded-2xl bg-grouped p-3.5">
      <div>
        <ExplainedTag help={fromData ? chatCopy.fromDataHelp : chatCopy.aiWrittenHelp}>{fromData ? FROM_YOUR_DATA : AI_WRITTEN}</ExplainedTag>
      </div>
      <p className="text-sm leading-relaxed break-words">
        {answer.segments.map((segment, i) => {
          const fact = segment.factId === null ? undefined : byId.get(segment.factId)
          if (fact !== undefined) return <FactChip key={i} fact={fact} onLink={onLink} />
          return <span key={i}>{segment.text}</span>
        })}
      </p>
      {answer.facts.length > 0 && (
        <div className="flex flex-col gap-1">
          <h3 className="text-[11px] font-medium text-muted-foreground">{chatCopy.sources}</h3>
          <ul className="flex flex-col gap-0.5">
            {answer.facts.map((fact) => (
              <li key={fact.id} className="flex flex-col text-xs">
                <span className="text-[11px] text-muted-foreground">{factLabel(fact)}</span>
                <span className="font-medium break-words">{factValue(fact)}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}

/**
 * A value in the sentence: the formatted value, with what it is on hover or focus, linking to its source. A span, not a
 * button, so a long value (a provenance line) wraps with the sentence instead of standing as a block.
 */
function FactChip({ fact, onLink }: { fact: ChatFactView; onLink: (fact: ChatFactView) => void }) {
  const label = factLabel(fact)
  const value = factValue(fact)
  const link = factLink(fact)
  const follow = () => onLink(fact)
  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <span
            tabIndex={0}
            role={link === null ? undefined : 'link'}
            onClick={link === null ? undefined : follow}
            onKeyDown={(event) => {
              if (link !== null && (event.key === 'Enter' || event.key === ' ')) {
                event.preventDefault()
                follow()
              }
            }}
          />
        }
        data-testid={`chat-fact-${fact.id}`}
        aria-label={`${label}: ${value}${link === null ? '' : `. ${link.label}`}`}
        className={cn(
          'mx-0.5 rounded-md bg-card px-1.5 py-px font-semibold [box-decoration-break:clone] shadow-[inset_0_0_0_0.5px_var(--hairline)] outline-none focus-visible:ring-2 focus-visible:ring-ring',
          link !== null && 'cursor-pointer hover:bg-fill',
        )}
      >
        {value}
      </TooltipTrigger>
      <TooltipContent side="top" className="max-w-xs text-left">
        {label}
      </TooltipContent>
    </Tooltip>
  )
}

function Suggestions({
  heading,
  sub,
  onPick,
  disabled,
}: {
  heading: string
  sub?: string
  onPick: (template: TemplateId) => void
  disabled: boolean
}) {
  return (
    <div className="flex flex-col gap-2">
      <p className="text-[13px] text-ink-2">{heading}</p>
      {sub !== undefined && <p className="text-[11px] text-muted-foreground">{sub}</p>}
      <div className="flex flex-wrap gap-1.5">
        {TEMPLATES.map((template) => (
          <Button
            key={template}
            variant="outline"
            size="sm"
            data-testid={`chat-suggest-${template}`}
            disabled={disabled}
            onClick={() => onPick(template)}
            className="h-auto min-h-8 rounded-full py-1 text-left whitespace-normal"
          >
            {chatCopy.questions[template]}
          </Button>
        ))}
      </div>
    </div>
  )
}
