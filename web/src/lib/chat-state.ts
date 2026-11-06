import type { ChatAnswer } from '@/api/client'

// The chat panel's conversation (F13), as a pure reducer: each question becomes a turn, answered once.

export type TurnState =
  | { phase: 'pending' }
  | { phase: 'answered'; answer: ChatAnswer; fromData: boolean }
  | { phase: 'notAnswered' }
  | { phase: 'unavailable' }
  | { phase: 'error'; message: string }

export interface Turn {
  id: number
  question: string
  /** Typed questions go to the AI; the suggested ones are answered from the data. */
  asked: 'ai' | 'template'
  state: TurnState
}

export interface ChatState {
  turns: Turn[]
}

export const initialChat: ChatState = { turns: [] }

/** `id` comes from the caller, so it can dispatch the answer to the same turn. */
export type ChatAction =
  | { type: 'asked'; id: number; question: string; asked: Turn['asked'] }
  | { type: 'answered'; id: number; answer: ChatAnswer }
  | { type: 'failed'; id: number; message: string }

/** At most this many turns stay on screen; older ones scroll away for good. */
export const MAX_TURNS = 20

export function chatReducer(state: ChatState, action: ChatAction): ChatState {
  switch (action.type) {
    case 'asked': {
      const turn: Turn = { id: action.id, question: action.question, asked: action.asked, state: { phase: 'pending' } }
      return { turns: [...state.turns, turn].slice(-MAX_TURNS) }
    }
    case 'answered':
      return update(state, action.id, toState(action.answer))
    case 'failed':
      return update(state, action.id, { phase: 'error', message: action.message })
  }
}

function toState(answer: ChatAnswer): TurnState {
  switch (answer.status) {
    case 'answered':
      return { phase: 'answered', answer, fromData: answer.provider === 'template' }
    case 'notAnswered':
      return { phase: 'notAnswered' }
    case 'unavailable':
      return { phase: 'unavailable' }
  }
}

function update(state: ChatState, id: number, next: TurnState): ChatState {
  const turns = state.turns.map((t) => (t.id === id && t.state.phase === 'pending' ? { ...t, state: next } : t))
  return { turns }
}

/** The questions sent as history with the next one: the last three typed ones, never an answer. */
export function historyOf(state: ChatState): string[] {
  return state.turns.filter((t) => t.asked === 'ai').map((t) => t.question).slice(-3)
}

/** Whether a question is still being answered, so the input waits. */
export function isPending(state: ChatState): boolean {
  return state.turns.some((t) => t.state.phase === 'pending')
}
