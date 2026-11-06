import type { ChatModels, ChatModelView } from '@/api/client'
import { chatCopy } from '@/copy/chat'

// The chat's model choice (F13). "Automatic" sends no model, so the server asks its chat models in order (AI_CHAT_MODELS,
// else AI_MODELS); a chosen model answers alone. The list is the server's: a model without its key is listed but cannot
// be chosen, and says why. Whichever model writes the sentence, the API fills every value in from the data.

/** The select's value for "Automatic". */
export const AUTO = 'auto'

export interface ModelOption {
  value: string
  label: string
  disabled: boolean
}

/** A model as a reader knows it: the provider's name, then the model's own. */
export function modelName(model: Pick<ChatModelView, 'provider' | 'model'>): string {
  return `${chatCopy.models.providers[model.provider] ?? model.provider} · ${model.model}`
}

/** Automatic first, then the server's models in its order; one that is not set up is shown with why, not choosable. */
export function modelOptions(list: ChatModels | null): ModelOption[] {
  const models = (list?.models ?? []).map((m) => ({
    value: m.id,
    label: m.ready ? modelName(m) : chatCopy.models.notReady(modelName(m), m.reason),
    disabled: !m.ready,
  }))
  return [{ value: AUTO, label: chatCopy.models.auto, disabled: false }, ...models]
}

/**
 * The model to send with a question: the chosen one while the list still offers it ready, else null (Automatic). The
 * picker shows the same, so a model that went away since it was chosen never looks chosen.
 */
export function modelToSend(selected: string, list: ChatModels | null): string | null {
  if (selected === AUTO || list === null) return null
  return list.models.some((m) => m.id === selected && m.ready) ? selected : null
}

/** The line under the picker: what the choice does, or why there is nothing to choose. */
export function modelNote(list: ChatModels | null, sent: string | null): string {
  if (list === null) return chatCopy.models.loading
  if (!list.available) return chatCopy.models.serviceDown
  if (list.models.length === 0) return chatCopy.models.none
  if (!list.models.some((m) => m.ready)) return chatCopy.models.noneReady
  return sent === null ? chatCopy.models.autoHelp : chatCopy.models.aloneHelp
}

/** Which model wrote an answer, from the answer's provider; null when no model wrote it (templates, no AI). */
export function answeredBy(provider: string, list: ChatModels | null): string | null {
  if (provider === 'template' || provider === 'stub' || provider === 'none') return null
  const listed = list?.models.find((m) => m.id === provider)
  if (listed !== undefined) return modelName(listed)
  // An id is <provider>:<model>; the model part may hold colons of its own (Ollama tags).
  const colon = provider.indexOf(':')
  return colon > 0 ? modelName({ provider: provider.slice(0, colon), model: provider.slice(colon + 1) }) : provider
}
