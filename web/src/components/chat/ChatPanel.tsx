import { ArrowDownIcon, ArrowUpIcon, ChevronDownIcon, SparklesIcon, SquarePenIcon, XIcon } from 'lucide-react'
import {
  type FormEvent,
  type KeyboardEvent,
  type ReactNode,
  useCallback,
  useEffect,
  useId,
  useLayoutEffect,
  useReducer,
  useRef,
  useState,
} from 'react'
import { useNavigate } from 'react-router'
import type { ChatAnswer, ChatFactView, ChatModels } from '@/api/client'
import { ChatModelPicker } from '@/components/chat/ChatModelPicker'
import { ExplainedTag } from '@/components/shell/Page'
import { Button } from '@/components/ui/button'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { chatCopy, type TemplateId } from '@/copy/chat'
import { answerTag, factLabel, factLink, factValue } from '@/lib/chat-display'
import { AUTO, answeredBy, modelNote, modelNoteShown, modelToSend } from '@/lib/chat-models'
import { chatReducer, historyOf, initialChat, isPending, type Turn } from '@/lib/chat-state'
import { templateAnswer } from '@/lib/chat-templates'
import { askChat, fetchChatModels } from '@/lib/dashboard-actions'
import { cn } from '@/lib/utils'
import { useGraphStore } from '@/store'

// The AI assistant (F13), opened from the sidebar on every screen. It sits where the side panel sits and covers it;
// closing it brings the side panel back as it was. The conversation stays while the app is open. Typed questions go to
// the API, which has the AI write the sentence and fills every value in from the data after checking it; the suggested
// questions are answered from the store's data with a fixed sentence. Every value is a chip that says what it is.
//
// The header and the question box stay where they are; only the conversation scrolls, inside the panel, and its
// scrolling never carries over to the page. A new question brings the conversation to its end; an answer does so only
// for a reader already there, so someone reading an earlier answer is not pulled away (a button offers the way down).

const MAX_QUESTION = 300
const TEMPLATES = Object.keys(chatCopy.questions) as TemplateId[]
/** Within this many pixels of its end, the conversation counts as read to the end and follows new messages. */
const NEAR_END = 64
/** The question box grows with its text up to this height, then scrolls. */
const MAX_INPUT_HEIGHT = 132

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
  const listRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLTextAreaElement>(null)
  const atEnd = useRef(true)
  const [following, setFollowing] = useState(true)
  const [scrolled, setScrolled] = useState(false)
  const pending = isPending(state)
  const [models, setModels] = useState<ChatModels | null>(null)
  const [picked, setPicked] = useState(AUTO)
  const sent = modelToSend(picked, models)
  const noteId = useId()

  // Each opening starts at the end of the conversation, with no scroll shadow (state adjusted while rendering).
  const [shownOpen, setShownOpen] = useState(open)
  if (open !== shownOpen) {
    setShownOpen(open)
    if (open) {
      setFollowing(true)
      setScrolled(false)
    }
  }

  // The model list is read each time the chat opens, so a model set up meanwhile shows; the last list stays while it
  // loads. A list that cannot be read leaves Automatic alone.
  useEffect(() => {
    if (!open) return
    const controller = new AbortController()
    void fetchChatModels(controller.signal).then((result) => {
      if (!controller.signal.aborted) setModels(result.ok ? result.data : { available: false, models: [] })
    })
    return () => controller.abort()
  }, [open])

  // Escape closes the chat before anything else hears it (the side panel under it would close too).
  useEffect(() => {
    if (!open) return
    const onKey = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape' && !event.defaultPrevented) {
        event.preventDefault()
        setPanel('chat', false)
      }
    }
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  }, [open, setPanel])

  // Scrolls the conversation itself, never the page around it (scrollIntoView would move both).
  const scrollToEnd = useCallback((smooth: boolean) => {
    const list = listRef.current
    if (list === null) return
    const still = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    list.scrollTo({ top: list.scrollHeight, behavior: smooth && !still ? 'smooth' : 'auto' })
  }, [])

  // On opening: at the end, and the question box ready for a keyboard (not on a phone, where it would raise the keyboard).
  useLayoutEffect(() => {
    if (!open) return
    atEnd.current = true
    scrollToEnd(false)
    if (window.matchMedia('(pointer: fine)').matches) inputRef.current?.focus()
  }, [open, scrollToEnd])

  const turnCount = state.turns.length
  useEffect(() => {
    if (turnCount === 0) return
    atEnd.current = true
    scrollToEnd(true)
  }, [turnCount, scrollToEnd])

  useEffect(() => {
    if (atEnd.current) scrollToEnd(true)
  }, [state.turns, scrollToEnd])

  // The question box grows with its text, up to a few lines.
  useLayoutEffect(() => {
    const input = inputRef.current
    if (input === null) return
    input.style.height = 'auto'
    input.style.height = `${Math.min(input.scrollHeight, MAX_INPUT_HEIGHT)}px`
  }, [draft, open])

  // Returning nothing keeps the conversation: the component stays mounted while the chat is closed.
  if (!open) return null

  const onScroll = () => {
    const list = listRef.current
    if (list === null) return
    const end = list.scrollHeight - list.scrollTop - list.clientHeight < NEAR_END
    atEnd.current = end
    setFollowing(end)
    setScrolled(list.scrollTop > 2)
  }

  const ask = async (question: string) => {
    const id = nextId.current++
    const history = historyOf(state)
    dispatch({ type: 'asked', id, question, asked: 'ai' })
    const answer = await askChat(question, history, sent)
    dispatch(answer.ok ? { type: 'answered', id, answer: answer.data } : { type: 'failed', id, message: answer.message })
  }

  const askTemplate = (template: TemplateId) => {
    const id = nextId.current++
    dispatch({ type: 'asked', id, question: chatCopy.questions[template], asked: 'template' })
    dispatch({ type: 'answered', id, answer: templateAnswer(template, Object.values(nodes), kpis) })
  }

  const submit = () => {
    const question = draft.trim()
    if (question === '' || pending) return
    setDraft('')
    void ask(question)
  }

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    submit()
  }

  // Enter sends, Shift+Enter starts a new line; a word still being composed (an input method) is left alone.
  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault()
      submit()
    }
  }

  const newChat = () => {
    dispatch({ type: 'cleared' })
    setDraft('')
    inputRef.current?.focus()
  }

  const onLink = (fact: ChatFactView) => {
    const link = factLink(fact)
    if (link === null) return
    if (link.kind === 'node' || link.kind === 'whatIf') {
      // Picking a supplier shows its side panel, and the chat steps aside; a what-if opens it on What if.
      select(link.nodeId)
      if (link.kind === 'whatIf') setPanel('simulator', true)
      void navigate('/suppliers')
    } else {
      setPanel('chat', false)
      void navigate('/audit')
    }
  }

  const noteShown = modelNoteShown(models)

  return (
    <aside
      data-testid="chat-panel"
      aria-label={chatCopy.title}
      className="fixed inset-0 z-40 flex flex-col overflow-hidden bg-card motion-safe:animate-in motion-safe:slide-in-from-bottom-2 motion-safe:duration-300 lg:sticky lg:inset-auto lg:top-0 lg:z-auto lg:h-dvh lg:w-[360px] lg:shrink-0 lg:shadow-[-0.5px_0_0_var(--hairline),-18px_0_40px_-30px_rgb(0_0_0/0.35)] lg:motion-safe:fade-in-0 lg:motion-safe:slide-in-from-bottom-0 lg:motion-safe:slide-in-from-right-6"
    >
      <header
        className={cn(
          'flex shrink-0 items-center gap-3 px-5 pt-5 pb-3.5 transition-shadow duration-200',
          scrolled && 'shadow-[0_0.5px_0_var(--hairline)]',
        )}
      >
        <span className="inline-flex size-9 shrink-0 items-center justify-center rounded-[11px] bg-fill text-primary" aria-hidden>
          <SparklesIcon className="size-[18px]" strokeWidth={1.8} />
        </span>
        <div className="flex min-w-0 flex-1 flex-col">
          <h2 className="font-display text-[17px] leading-tight font-semibold tracking-[-0.01em]">{chatCopy.title}</h2>
          <p className="truncate text-[12px] text-muted-foreground">{chatCopy.subtitle}</p>
        </div>
        {turnCount > 0 && (
          <HeaderButton label={chatCopy.newChat} onClick={newChat} testId="chat-new">
            <SquarePenIcon className="size-[15px]" strokeWidth={2} aria-hidden />
          </HeaderButton>
        )}
        <HeaderButton label={chatCopy.close} onClick={() => setPanel('chat', false)}>
          <XIcon className="size-3.5" strokeWidth={2.6} aria-hidden />
        </HeaderButton>
      </header>

      <div className="relative min-h-0 flex-1">
        <div
          ref={listRef}
          onScroll={onScroll}
          role="log"
          aria-live="polite"
          aria-label={chatCopy.title}
          data-testid="chat-log"
          className="h-full overflow-y-auto overscroll-contain px-5 pt-1 pb-6 [scrollbar-color:var(--fill-strong)_transparent] [scrollbar-width:thin]"
        >
          <div className="flex flex-col gap-6">
            {turnCount === 0 ? (
              <Welcome onPick={askTemplate} disabled={pending} />
            ) : (
              state.turns.map((turn) => (
                <TurnView
                  key={turn.id}
                  turn={turn}
                  onLink={onLink}
                  onPick={askTemplate}
                  onRetry={(question) => void ask(question)}
                  pending={pending}
                  models={models}
                />
              ))
            )}
          </div>
        </div>
        {!following && (
          <button
            type="button"
            aria-label={chatCopy.jumpToLatest}
            onClick={() => scrollToEnd(true)}
            className="absolute bottom-3 left-1/2 inline-flex size-8 -translate-x-1/2 items-center justify-center rounded-full bg-card text-ink-2 shadow-[var(--shadow-raised)] transition-colors outline-none hover:bg-fill focus-visible:ring-2 focus-visible:ring-ring motion-safe:animate-in motion-safe:fade-in-0 motion-safe:zoom-in-95"
          >
            <ArrowDownIcon className="size-4" strokeWidth={2.2} aria-hidden />
          </button>
        )}
      </div>

      <form
        onSubmit={onSubmit}
        className={cn(
          'flex shrink-0 flex-col gap-1.5 px-4 pt-2 pb-[max(1rem,env(safe-area-inset-bottom))] transition-shadow duration-200',
          !following && 'shadow-[0_-0.5px_0_var(--hairline)]',
        )}
      >
        <div className="flex flex-col gap-1.5 rounded-[20px] bg-fill px-3.5 pt-2.5 pb-2 transition-shadow duration-200 focus-within:shadow-[0_0_0_2px_var(--ring)]">
          <label htmlFor="chat-question" className="sr-only">
            {chatCopy.inputLabel}
          </label>
          <textarea
            id="chat-question"
            ref={inputRef}
            data-testid="chat-input"
            rows={1}
            value={draft}
            maxLength={MAX_QUESTION}
            autoComplete="off"
            placeholder={chatCopy.placeholder}
            onChange={(event) => setDraft(event.target.value)}
            onKeyDown={onKeyDown}
            className="block w-full resize-none bg-transparent text-[14px] leading-6 outline-none placeholder:text-muted-foreground"
          />
          <div className="flex items-center gap-2">
            <ChatModelPicker list={models} sent={sent} onChange={setPicked} noteId={noteId} />
            {draft.length >= MAX_QUESTION - 50 && (
              <span className="ml-auto text-[11px] text-muted-foreground tabular-nums" aria-live="polite">
                {draft.length}/{MAX_QUESTION}
              </span>
            )}
            <button
              type="submit"
              data-testid="chat-send"
              aria-label={chatCopy.send}
              disabled={pending || draft.trim() === ''}
              className={cn(
                'inline-flex size-8 shrink-0 items-center justify-center rounded-full bg-primary text-primary-foreground transition-colors duration-200 outline-none hover:bg-primary/90 focus-visible:ring-2 focus-visible:ring-ring disabled:bg-fill-strong disabled:text-muted-foreground',
                draft.length < MAX_QUESTION - 50 && 'ml-auto',
              )}
            >
              <ArrowUpIcon className="size-4" strokeWidth={2.4} aria-hidden />
            </button>
          </div>
        </div>
        {/* One note for the model choice: shown when it explains why no AI will answer, else read out with the picker. */}
        <p id={noteId} className={noteShown ? 'px-2 text-[11.5px] leading-snug text-muted-foreground' : 'sr-only'}>
          {modelNote(models, sent)}
        </p>
        {draft.length >= MAX_QUESTION && <p className="px-2 text-[11.5px] text-muted-foreground">{chatCopy.tooLong(MAX_QUESTION)}</p>}
      </form>
    </aside>
  )
}

function HeaderButton({ label, onClick, testId, children }: { label: string; onClick: () => void; testId?: string; children: ReactNode }) {
  return (
    <Tooltip>
      <TooltipTrigger
        render={<button type="button" />}
        aria-label={label}
        data-testid={testId}
        onClick={onClick}
        className="inline-flex size-[30px] shrink-0 items-center justify-center rounded-full bg-fill text-ink-2 transition-colors outline-none hover:bg-fill-strong focus-visible:ring-2 focus-visible:ring-ring"
      >
        {children}
      </TooltipTrigger>
      <TooltipContent side="bottom">{label}</TooltipContent>
    </Tooltip>
  )
}

/** The empty conversation: what the assistant is for, the promise about values, and four questions to start from. */
function Welcome({ onPick, disabled }: { onPick: (template: TemplateId) => void; disabled: boolean }) {
  return (
    <div className="flex flex-col gap-5 pt-4">
      <div className="flex flex-col gap-1.5">
        <p className="text-[15px] leading-snug font-medium text-balance">{chatCopy.welcome}</p>
        <p className="text-[12.5px] leading-snug text-muted-foreground">{chatCopy.intro}</p>
      </div>
      <Suggestions heading={chatCopy.tryThese} onPick={onPick} disabled={disabled} />
    </div>
  )
}

function TurnView({
  turn,
  onLink,
  onPick,
  onRetry,
  pending,
  models,
}: {
  turn: Turn
  onLink: (fact: ChatFactView) => void
  onPick: (template: TemplateId) => void
  onRetry: (question: string) => void
  pending: boolean
  models: ChatModels | null
}) {
  const { state } = turn
  return (
    <div data-testid={`chat-turn-${turn.id}`} data-phase={state.phase} className="flex flex-col gap-2">
      <p className="max-w-[85%] self-end rounded-[18px] rounded-br-[6px] bg-fill px-3.5 py-2 text-[14px] leading-snug break-words whitespace-pre-wrap">
        {turn.question}
      </p>
      {state.phase === 'pending' && <Typing />}
      {state.phase === 'answered' && (
        <AnswerView answer={state.answer} fromData={state.fromData} onLink={onLink} by={answeredBy(state.answer.provider, models)} />
      )}
      {state.phase === 'notAnswered' && (
        <Suggestions heading={chatCopy.notAnswered} sub={chatCopy.notAnsweredSub} onPick={onPick} disabled={pending} />
      )}
      {state.phase === 'unavailable' && <Suggestions heading={chatCopy.unavailable} onPick={onPick} disabled={pending} />}
      {state.phase === 'error' && (
        <div role="alert" className="flex flex-wrap items-baseline gap-x-2 gap-y-1 text-[13px]">
          <span className="text-destructive">{state.message}</span>
          {turn.asked === 'ai' && (
            <button
              type="button"
              disabled={pending}
              onClick={() => onRetry(turn.question)}
              className="rounded-md font-medium text-primary outline-none hover:underline focus-visible:ring-2 focus-visible:ring-ring disabled:text-muted-foreground"
            >
              {chatCopy.retry}
            </button>
          )}
        </div>
      )}
    </div>
  )
}

/** The answer on its way: three dots in the assistant's place, and the words for a screen reader. */
function Typing() {
  return (
    <div role="status" className="flex w-fit items-center gap-1 rounded-[18px] rounded-bl-[6px] bg-grouped px-3.5 py-3">
      <span className="sr-only">{chatCopy.sending}</span>
      {['[animation-delay:0ms]', '[animation-delay:160ms]', '[animation-delay:320ms]'].map((delay) => (
        <span key={delay} aria-hidden className={cn('size-1.5 rounded-full bg-muted-foreground/70 motion-safe:animate-pulse', delay)} />
      ))}
    </div>
  )
}

/** One answer: whose sentence it is (and which model wrote it), the sentence with its value chips, and the sources. */
function AnswerView({
  answer,
  fromData,
  onLink,
  by,
}: {
  answer: ChatAnswer
  fromData: boolean
  onLink: (fact: ChatFactView) => void
  by: string | null
}) {
  const byId = new Map(answer.facts.map((f) => [f.id, f]))
  const tag = answerTag(answer, fromData)
  return (
    <div
      data-testid="chat-answer"
      data-provider={answer.provider}
      data-kind={tag.kind}
      className="flex flex-col gap-2 rounded-[18px] rounded-bl-[6px] bg-grouped px-3.5 pt-2.5 pb-3"
    >
      <div className="flex min-w-0 flex-wrap items-center gap-x-1.5 gap-y-0.5">
        <ExplainedTag help={tag.help}>{tag.label}</ExplainedTag>
        {!fromData && by !== null && <span className="min-w-0 truncate text-[11px] text-muted-foreground">{chatCopy.models.writtenBy(by)}</span>}
      </div>
      <p className="text-[14px] leading-relaxed break-words">
        {answer.segments.map((segment, i) => {
          const fact = segment.factId === null ? undefined : byId.get(segment.factId)
          if (fact !== undefined) return <FactChip key={i} fact={fact} onLink={onLink} />
          return <span key={i}>{segment.text}</span>
        })}
      </p>
      {answer.facts.length > 0 && <Sources facts={answer.facts} />}
    </div>
  )
}

/** Each value of an answer with what it is, folded away until asked for: the chips already say it on hover. */
function Sources({ facts }: { facts: ChatFactView[] }) {
  const [shown, setShown] = useState(false)
  const id = useId()
  return (
    <div className="flex flex-col gap-2">
      <button
        type="button"
        aria-expanded={shown}
        aria-controls={id}
        onClick={() => setShown((s) => !s)}
        className="inline-flex w-fit items-center gap-1 rounded-md text-[11.5px] font-medium text-muted-foreground transition-colors outline-none hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring"
      >
        {chatCopy.showSources(facts.length)}
        <ChevronDownIcon className={cn('size-3.5 transition-transform duration-200', shown && 'rotate-180')} aria-hidden />
      </button>
      {shown && (
        <ul id={id} aria-label={chatCopy.sources} className="flex flex-col gap-1.5 pt-2 shadow-[0_-0.5px_0_var(--hairline)]">
          {facts.map((fact) => (
            <li key={fact.id} className="flex items-baseline justify-between gap-3 text-[12px]">
              <span className="min-w-0 text-muted-foreground">{factLabel(fact)}</span>
              <span className="text-right font-medium break-words tabular-nums">{factValue(fact)}</span>
            </li>
          ))}
        </ul>
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
      {sub !== undefined && <p className="text-[11.5px] text-muted-foreground">{sub}</p>}
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
