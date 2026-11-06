import type { ChatModels, ChatModelView } from '@/api/client'
import { chatCopy } from '@/copy/chat'

// The chat's model choice (F13). "Automatic" sends no model, so the server asks its chat models in order (AI_CHAT_MODELS,
// else AI_MODELS), or its first local model while none of those is ready; a chosen model answers alone. The list is the
// server's: a model without its key is listed but cannot be chosen, and says why; the local Ollama server's other
// models follow, asked only when picked. Whichever model writes the sentence, the API fills every value in from the data.

/** The select's value for "Automatic". */
export const AUTO = 'auto'

export interface ModelOption {
  value: string
  label: string
  disabled: boolean
}

/** The picker's options: Automatic alone, then the groups that have models, each with its heading. */
export interface ModelOptions {
  auto: ModelOption
  groups: { label: string; options: ModelOption[] }[]
}

/** A model as a reader knows it: the provider's name, then the model's own. */
export function modelName(model: Pick<ChatModelView, 'provider' | 'model'>): string {
  return `${chatCopy.models.providers[model.provider] ?? model.provider} · ${model.model}`
}

function option(m: ChatModelView): ModelOption {
  return {
    value: m.id,
    label: m.ready ? modelName(m) : chatCopy.models.notReady(modelName(m), m.reason),
    disabled: !m.ready,
  }
}

/**
 * Automatic first, then the models Automatic asks, in the server's order (one that is not set up shown with why, not
 * choosable), then the local models. A group with no model is left out.
 */
export function modelOptions(list: ChatModels | null): ModelOptions {
  const models = list?.models ?? []
  const groups = [
    { label: chatCopy.models.autoGroup, options: models.filter((m) => m.automatic).map(option) },
    { label: chatCopy.models.localGroup, options: models.filter((m) => !m.automatic).map(option) },
  ].filter((g) => g.options.length > 0)
  return { auto: { value: AUTO, label: chatCopy.models.auto, disabled: false }, groups }
}

/**
 * The model to send with a question: the chosen one while the list still offers it ready, else null (Automatic). The
 * picker shows the same, so a model that went away since it was chosen never looks chosen.
 */
export function modelToSend(selected: string, list: ChatModels | null): string | null {
  if (selected === AUTO || list === null) return null
  return list.models.some((m) => m.id === selected && m.ready) ? selected : null
}

/**
 * The line under the picker: which models Automatic asks, by name, or why it has nothing to ask. While no model of the
 * server's own is ready, the server marks the first local model for Automatic, so it is named here.
 */
export function modelNote(list: ChatModels | null, sent: string | null): string {
  if (list === null) return chatCopy.models.loading
  if (!list.available) return chatCopy.models.serviceDown
  if (sent !== null) return chatCopy.models.aloneHelp
  const automatic = list.models.filter((m) => m.automatic)
  const ready = automatic.filter((m) => m.ready).map(modelName)
  if (ready.length > 0) return chatCopy.models.autoHelp(ready)
  return automatic.length === 0 ? chatCopy.models.none : chatCopy.models.noneReady
}

/**
 * Whether the note must show under the question box: only when it explains why no AI will answer (the service is
 * down, no model is set up, none has its key). Otherwise it stays the picker's description for screen readers and on
 * hover, so a working chat keeps a quiet composer.
 */
export function modelNoteShown(list: ChatModels | null): boolean {
  if (list === null) return false
  if (!list.available) return true
  return !list.models.some((m) => m.automatic && m.ready)
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
