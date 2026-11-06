import { CheckIcon } from 'lucide-react'
import { type ReactNode, useId } from 'react'
import { cn } from '@/lib/utils'

// One question on the supplier page: its options as a grouped list with a tick on the pressed one, and "skip" as a
// text button under it, exactly one of them pressed. Sized for one thumb at 360 px (every option 56 px high).

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
  /** The group's `data-testid`; each option gets `<testId>-<value>`, and skip `<testId>-skip`. */
  testId?: string
}

export function AnswerGroup<T extends string>({
  question,
  options,
  selected,
  skipLabel,
  disabled,
  onChange,
  testId,
}: AnswerGroupProps<T>) {
  const headingId = useId()
  const skipped = selected === null
  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-2">
      <h2 id={headingId} className="px-1 pb-1 text-[17px] leading-snug font-semibold tracking-[-0.015em]">
        {question}
      </h2>
      <div
        role="group"
        aria-labelledby={headingId}
        data-testid={testId}
        className={cn(
          'overflow-hidden rounded-[14px] bg-card shadow-[0_0_0_0.5px_rgb(0_0_0/0.08)] transition-opacity duration-300',
          skipped && 'opacity-60',
        )}
      >
        {options.map((option, i) => (
          <Choice
            key={option.value}
            pressed={selected === option.value}
            disabled={disabled}
            first={i === 0}
            testId={testId && `${testId}-${option.value}`}
            onClick={() => onChange(option.value)}
          >
            {option.label}
          </Choice>
        ))}
      </div>
      <button
        type="button"
        data-testid={testId && `${testId}-skip`}
        aria-pressed={skipped}
        disabled={disabled}
        onClick={() => onChange(null)}
        className={cn(
          'min-h-11 self-end rounded-full px-3 text-[15px] transition-colors disabled:opacity-60',
          skipped ? 'font-semibold text-foreground' : 'font-medium text-verified-ink hover:bg-fill',
        )}
      >
        {skipLabel}
      </button>
    </section>
  )
}

interface ChoiceProps {
  pressed: boolean
  disabled: boolean
  first: boolean
  testId?: string
  onClick: () => void
  children: ReactNode
}

function Choice({ pressed, disabled, first, testId, onClick, children }: ChoiceProps) {
  return (
    <button
      type="button"
      data-testid={testId}
      aria-pressed={pressed}
      disabled={disabled}
      onClick={onClick}
      className={cn(
        // The list clips its corners (overflow-hidden), so the focus outline is drawn inside the option.
        'flex min-h-14 w-full items-center gap-3 px-4 text-left text-[17px] transition-colors focus-visible:outline-offset-[-3px] active:bg-[#ededf0] disabled:opacity-60',
        !first && 'shadow-[inset_0_0.5px_0_var(--hairline)]',
        pressed && 'font-medium',
      )}
    >
      <span className="flex-1">{children}</span>
      <CheckIcon aria-hidden strokeWidth={2.6} className={cn('size-5 shrink-0 text-verified', pressed ? 'opacity-100' : 'opacity-0')} />
    </button>
  )
}
