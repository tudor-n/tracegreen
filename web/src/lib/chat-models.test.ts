import { describe, expect, it } from 'vitest'
import type { ChatModels, ChatModelView } from '@/api/client'
import { AUTO, answeredBy, modelName, modelNote, modelOptions, modelToSend } from './chat-models'

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
          label: 'Set up for Automatic',
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
  it('says what the choice does', () => {
    expect(modelNote(list, null)).toMatch(/^Automatic asks each ready model/)
    expect(modelNote(list, 'ollama:qwen2.5:14b')).toMatch(/^Only this model is asked/)
  })

  it('points to the local models when Automatic has nothing ready', () => {
    expect(modelNote({ available: true, models: [found] }, null)).toMatch(/^Automatic has no model set up yet.*Pick a local model/)
    expect(modelNote({ available: true, models: [cloud, found] }, null)).toMatch(/Pick a local model/)
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
