import { describe, expect, it } from 'vitest'
import type { ChatAnswer } from '@/api/client'
import { chatReducer, historyOf, initialChat, isPending, MAX_TURNS } from './chat-state'

const answer = (status: ChatAnswer['status'], provider = 'openai:model-a'): ChatAnswer => ({
  status,
  segments: status === 'answered' ? [{ text: 'x', factId: null }] : [],
  facts: [],
  provider,
})

describe('chatReducer', () => {
  it('adds a pending turn and settles it once', () => {
    let state = chatReducer(initialChat, { type: 'asked', id: 1, question: 'Who?', asked: 'ai' })
    expect(isPending(state)).toBe(true)
    state = chatReducer(state, { type: 'answered', id: 1, answer: answer('answered') })
    expect(state.turns[0].state).toEqual({ phase: 'answered', answer: answer('answered'), fromData: false })
    expect(isPending(state)).toBe(false)
    // A second answer for the same turn changes nothing.
    expect(chatReducer(state, { type: 'failed', id: 1, message: 'late' }).turns[0].state.phase).toBe('answered')
  })

  it('reads the API status: not answered and unavailable are never shown as answers', () => {
    const asked = chatReducer(initialChat, { type: 'asked', id: 1, question: 'Q', asked: 'ai' })
    expect(chatReducer(asked, { type: 'answered', id: 1, answer: answer('notAnswered') }).turns[0].state).toEqual({ phase: 'notAnswered' })
    expect(chatReducer(asked, { type: 'answered', id: 1, answer: answer('unavailable', 'stub') }).turns[0].state).toEqual({
      phase: 'unavailable',
    })
  })

  it('marks a template answer as answered from the data', () => {
    const asked = chatReducer(initialChat, { type: 'asked', id: 1, question: 'Top?', asked: 'template' })
    const state = chatReducer(asked, { type: 'answered', id: 1, answer: answer('answered', 'template') })
    expect(state.turns[0].state).toMatchObject({ phase: 'answered', fromData: true })
  })

  it('keeps the last turns only', () => {
    let state = initialChat
    for (let id = 1; id <= MAX_TURNS + 5; id++) state = chatReducer(state, { type: 'asked', id, question: `${id}`, asked: 'ai' })
    expect(state.turns).toHaveLength(MAX_TURNS)
    expect(state.turns[0].id).toBe(6)
  })
})

describe('historyOf', () => {
  it('is the last three typed questions, never a suggested one or an answer', () => {
    let state = initialChat
    ;['one', 'two', 'three', 'four'].forEach((question, i) => {
      state = chatReducer(state, { type: 'asked', id: i + 1, question, asked: 'ai' })
    })
    state = chatReducer(state, { type: 'asked', id: 9, question: 'Which supplier emits the most?', asked: 'template' })
    expect(historyOf(state)).toEqual(['two', 'three', 'four'])
  })
})
