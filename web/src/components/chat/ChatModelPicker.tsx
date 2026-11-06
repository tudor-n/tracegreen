import { ChevronDownIcon } from 'lucide-react'
import type { ChatModels } from '@/api/client'
import { chatCopy } from '@/copy/chat'
import { AUTO, modelNote, modelOptions } from '@/lib/chat-models'

// The AI assistant's model choice (F13), inside the question box: Automatic, the models it asks (a model without its key
// listed with why and not choosable, so a teammate sees what to set up), and the local Ollama models. A native select,
// so it works with the keyboard and on a phone as it is; the note that explains the choice is the panel's (noteId).

export function ChatModelPicker({
  list,
  sent,
  onChange,
  noteId,
}: {
  /** The server's list; null while it loads. */
  list: ChatModels | null
  /** The model the next question goes to; null for Automatic. */
  sent: string | null
  onChange: (value: string) => void
  /** The element that says what the choice does, read out with the picker. */
  noteId: string
}) {
  const options = modelOptions(list)
  return (
    <span className="relative inline-flex min-w-0">
      <label htmlFor="chat-model" className="sr-only">
        {chatCopy.models.label}
      </label>
      <select
        id="chat-model"
        data-testid="chat-model"
        data-phase={list === null ? 'loading' : list.available ? 'ready' : 'unavailable'}
        value={sent ?? AUTO}
        disabled={list === null}
        title={modelNote(list, sent)}
        aria-describedby={noteId}
        onChange={(event) => onChange(event.target.value)}
        className="h-7 max-w-[13.5rem] min-w-0 appearance-none truncate rounded-full [field-sizing:content] bg-card py-0 pr-7 pl-2.5 text-[12px] font-medium text-ink-2 shadow-[inset_0_0_0_0.5px_var(--hairline)] transition-colors outline-none hover:bg-grouped focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-60"
      >
        <option value={options.auto.value}>{options.auto.label}</option>
        {options.groups.map((group) => (
          <optgroup key={group.label} label={group.label}>
            {group.options.map((option) => (
              <option key={option.value} value={option.value} disabled={option.disabled}>
                {option.label}
              </option>
            ))}
          </optgroup>
        ))}
      </select>
      <ChevronDownIcon className="pointer-events-none absolute top-1/2 right-2 size-3.5 -translate-y-1/2 text-muted-foreground" aria-hidden />
    </span>
  )
}
