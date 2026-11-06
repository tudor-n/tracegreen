import { describe, expect, it } from 'vitest'
import type { ChatModels, ChatModelView } from '@/api/client'
import { AUTO, answeredBy, modelName, modelNote, modelNoteShown, modelOptions, modelToSend } from './chat-models'

const model = (overrides: Partial<ChatModelView>): ChatModelView => ({
  id: 'ollama:ministral-3:3b',
  provider: 'ollama',
  model: 'ministral-3:3b',
  ready: true,
  reason: null,
  automatic: true,
  ...overrides,
})

const cloud = model({
  id: 'anthropic:claude-x',
  provider: 'anthropic',
  model: 'claude-x',
  ready: false,
  reason: 'ANTHROPIC_API_KEY not set',
})
const configuredLocal = model({})
const found = model({ id: 'ollama:qwen2.5:14b', model: 'qwen2.5:14b', automatic: false })
const list: ChatModels = { available: true, models: [cloud, configuredLocal, found] }
const auto = { value: AUTO, label: 'Automatic', disabled: false }

describe('modelOptions', () => {
  it('puts Automatic first, then its models in the server order, then the local ones', () => {
    expect(modelOptions(list)).toEqual({
      auto,
      groups: [
        {
          label: 'Used by Automatic',
          options: [
            { value: 'anthropic:claude-x', label: 'Anthropic · claude-x (not set up: ANTHROPIC_API_KEY not set)', disabled: true },
            { value: 'ollama:ministral-3:3b', label: 'Ollama (local) · ministral-3:3b', disabled: false },
          ],
        },
        { label: 'Local models (Ollama)', options: [{ value: 'ollama:qwen2.5:14b', label: 'Ollama (local) · qwen2.5:14b', disabled: false }] },
      ],
    })
  })

  it('leaves out a group with no model, and offers Automatic alone while the list loads or when there is none', () => {
    expect(modelOptions({ available: true, models: [found] }).groups.map((g) => g.label)).toEqual(['Local models (Ollama)'])
    expect(modelOptions(null)).toEqual({ auto, groups: [] })
    expect(modelOptions({ available: true, models: [] })).toEqual({ auto, groups: [] })
  })

  it('shows an unknown provider as it is', () => {
    expect(modelName({ provider: 'mistral', model: 'm-1' })).toBe('mistral · m-1')
  })
})

describe('modelToSend', () => {
  it('sends nothing for Automatic, the id of a ready model, and nothing for one that is not ready or gone', () => {
    expect(modelToSend(AUTO, list)).toBeNull()
    expect(modelToSend('ollama:qwen2.5:14b', list)).toBe('ollama:qwen2.5:14b')
    expect(modelToSend('anthropic:claude-x', list)).toBeNull()
    expect(modelToSend('openai:gone', list)).toBeNull()
    expect(modelToSend('ollama:qwen2.5:14b', null)).toBeNull()
  })
})

describe('modelNote', () => {
  it('names what Automatic asks, or says a picked model is asked alone', () => {
    expect(modelNote(list, null)).toBe('Automatic asks Ollama (local) · ministral-3:3b.')
    const two = { available: true, models: [model({ id: 'openai:a', provider: 'openai', model: 'a' }), configuredLocal] }
    expect(modelNote(two, null)).toBe('Automatic asks OpenAI · a, then Ollama (local) · ministral-3:3b, until one answers.')
    expect(modelNote(list, 'ollama:qwen2.5:14b')).toBe('Only this model is asked.')
  })

  it('names the local model the server marked for Automatic while no model of its own is ready', () => {
    const fallback = { ...found, automatic: true }
    expect(modelNote({ available: true, models: [cloud, fallback] }, null)).toBe('Automatic asks Ollama (local) · qwen2.5:14b.')
  })

  it('says why there is nothing to choose', () => {
    expect(modelNote(null, null)).toBe('Looking for models…')
    expect(modelNote({ available: false, models: [] }, null)).toMatch(/isn't answering/)
    expect(modelNote({ available: true, models: [] }, null)).toMatch(/^No AI model is set up or found/)
    expect(modelNote({ available: true, models: [cloud] }, null)).toMatch(/^No model has its key/)
  })
})

describe('answeredBy', () => {
  it('names the listed model that wrote the answer', () => {
    expect(answeredBy('ollama:qwen2.5:14b', list)).toBe('Ollama (local) · qwen2.5:14b')
  })

  it('names an unlisted model from its id, keeping the colons of its tag', () => {
    expect(answeredBy('ollama:llama3.2:3b', null)).toBe('Ollama (local) · llama3.2:3b')
    expect(answeredBy('openai:model-a', list)).toBe('OpenAI · model-a')
  })

  it('names no model for an answer no model wrote', () => {
    for (const provider of ['template', 'stub', 'none']) expect(answeredBy(provider, list)).toBeNull()
  })
})

describe('modelNoteShown', () => {
  it('shows the note only when no AI will answer', () => {
    expect(modelNoteShown(list)).toBe(false)
    expect(modelNoteShown(null)).toBe(false)
    expect(modelNoteShown({ available: false, models: [] })).toBe(true)
    expect(modelNoteShown({ available: true, models: [] })).toBe(true)
    expect(modelNoteShown({ available: true, models: [cloud, found] })).toBe(true)
  })
})
