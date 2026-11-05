import { CheckIcon } from 'lucide-react'
import { type ReactNode, useId } from 'react'
import { cn } from '@/lib/utils'

// One question on the supplier page: its options and "skip" as large toggle buttons, exactly one of them pressed.
// Sized for one thumb at 360 px (every target at least 56 px high) and for the stage camera (strong pressed state).

export interface AnswerOption<T extends string> {
  value: T
  label: string
}

interface AnswerGroupProps<T extends string> {
  question: string
  options: AnswerOption<T>[]
  /** The chosen option, or null when the question is skipped. */
  selected: T | null
  skipLabel: string
  disabled: boolean
  onChange: (value: T | null) => void
}

export function AnswerGroup<T extends string>({
  question,
  options,
  selected,
  skipLabel,
  disabled,
  onChange,
}: AnswerGroupProps<T>) {
  const headingId = useId()
  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-3">
      <h2 id={headingId} className="text-lg leading-snug font-semibold">
        {question}
      </h2>
      <div role="group" aria-labelledby={headingId} className="grid grid-cols-2 gap-2">
        {options.map((option) => (
          <Choice
            key={option.value}
            pressed={selected === option.value}
            disabled={disabled}
            onClick={() => onChange(option.value)}
          >
            {option.label}
          </Choice>
        ))}
        <Choice pressed={selected === null} disabled={disabled} skip onClick={() => onChange(null)}>
          {skipLabel}
        </Choice>
      </div>
    </section>
  )
}

interface ChoiceProps {
  pressed: boolean
  disabled: boolean
  skip?: boolean
  onClick: () => void
  children: ReactNode
}

function Choice({ pressed, disabled, skip = false, onClick, children }: ChoiceProps) {
  return (
    <button
      type="button"
      aria-pressed={pressed}
      disabled={disabled}
      onClick={onClick}
      className={cn(
        'flex min-h-14 items-center justify-center gap-2 rounded-xl border-2 px-3 py-2 text-center text-base leading-tight font-semibold transition-colors outline-none focus-visible:ring-3 focus-visible:ring-ring/50 disabled:opacity-60',
        pressed
          ? 'border-primary bg-primary text-primary-foreground'
          : skip
            ? 'border-dashed border-border bg-background text-muted-foreground hover:bg-muted'
            : 'border-border bg-background hover:bg-muted',
      )}
    >
      {pressed && <CheckIcon aria-hidden className="size-5 shrink-0" />}
      <span>{children}</span>
    </button>
  )
}
