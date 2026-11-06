import type { ChatModels } from '@/api/client'
import { chatCopy } from '@/copy/chat'
import { AUTO, modelNote, modelOptions } from '@/lib/chat-models'

// The chat's model choice (F13), above the question box: Automatic, or one of the server's chat models. A model without
// its key is listed with why and cannot be chosen, so a teammate sees what to set up. A native select: it works with
// the keyboard and on a phone as it is.

export function ChatModelPicker({
  list,
  sent,
  onChange,
}: {
  /** The server's list; null while it loads. */
  list: ChatModels | null
  /** The model the next question goes to; null for Automatic. */
  sent: string | null
  onChange: (value: string) => void
}) {
  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center gap-2">
        <label htmlFor="chat-model" className="shrink-0 text-[12px] font-medium text-ink-2">
          {chatCopy.models.label}
        </label>
        <select
          id="chat-model"
          data-testid="chat-model"
          data-phase={list === null ? 'loading' : list.available ? 'ready' : 'unavailable'}
          value={sent ?? AUTO}
          disabled={list === null}
          aria-describedby="chat-model-note"
          onChange={(event) => onChange(event.target.value)}
          className="h-8 min-w-0 flex-1 rounded-full bg-fill px-3 text-[13px] outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-60"
        >
          {modelOptions(list).map((option) => (
            <option key={option.value} value={option.value} disabled={option.disabled}>
              {option.label}
            </option>
          ))}
        </select>
      </div>
      <p id="chat-model-note" className="px-1 text-[11px] text-muted-foreground">
        {modelNote(list, sent)}
      </p>
    </div>
  )
}
