import { Loader2Icon } from 'lucide-react'
import type { ComponentProps, ReactNode } from 'react'
import { cn } from '@/lib/utils'

// The building blocks every v2 screen shares: the large title, the white grouped surface, a grouped list with
// hairlines between rows, and the one-line loading, empty and error messages.

export function PageHeader({ title, subtitle, children }: { title: string; subtitle?: string; children?: ReactNode }) {
  return (
    <header className="flex flex-wrap items-end justify-between gap-4">
      <div className="flex flex-col gap-1.5">
        <h1 className="font-display text-[34px] leading-tight font-bold tracking-[-0.025em]">{title}</h1>
        {subtitle !== undefined && <p className="text-[15px] text-muted-foreground">{subtitle}</p>}
      </div>
      {children}
    </header>
  )
}

/** A white card on the grey ground: one hairline and one soft shadow, never a border plus a shadow. */
export function Surface({ className, ...props }: ComponentProps<'section'>) {
  return <section className={cn('rounded-[18px] bg-card p-6 shadow-card', className)} {...props} />
}

/** A grey inset group inside a surface or panel, its rows divided by hairlines. */
export function Group({ title, children, className }: { title?: string; children: ReactNode; className?: string }) {
  return (
    <div className={cn('flex flex-col gap-2', className)}>
      {title !== undefined && <h3 className="pl-0.5 text-[13px] font-medium text-muted-foreground">{title}</h3>}
      <div className="rounded-[14px] bg-grouped px-4 [&>*+*]:shadow-[inset_0_0.5px_0_var(--hairline)]">{children}</div>
    </div>
  )
}

/** One row of a Group: label on the left, value on the right, and an optional note (such as "illustrative") under it. */
export function GroupRow({ label, note, children }: { label: string; note?: string; children: ReactNode }) {
  return (
    <div className="flex min-h-11 items-center justify-between gap-3 py-2 text-sm">
      <span className="shrink-0 text-ink-2">{label}</span>
      <span className="flex flex-col items-end text-right">
        <span className="leading-snug font-medium break-words">{children}</span>
        {note !== undefined && <span className="text-[11px] text-muted-foreground">{note}</span>}
      </span>
    </div>
  )
}

/** Loading, empty or error, said in one line. */
export function PageMessage({ children, tone, busy = false }: { children: ReactNode; tone?: 'error'; busy?: boolean }) {
  return (
    <p
      role={tone === 'error' ? 'alert' : 'status'}
      className={cn(
        'flex items-center gap-2 py-6 text-sm',
        tone === 'error' ? 'text-destructive' : 'text-muted-foreground',
      )}
    >
      {busy && <Loader2Icon className="size-4 motion-safe:animate-spin" aria-hidden />}
      {children}
    </p>
  )
}

/** A capsule tag, such as a status, a hotspot or an AI label. */
export function Chip({ tone = 'neutral', children }: { tone?: ChipTone; children: ReactNode }) {
  return (
    <span className={cn('inline-flex h-6 shrink-0 items-center gap-1.5 rounded-full px-2.5 text-xs font-semibold', chipTone[tone])}>
      {children}
    </span>
  )
}

export type ChipTone = 'neutral' | 'estimated' | 'verified' | 'hotspot'

const chipTone: Record<ChipTone, string> = {
  neutral: 'bg-fill font-medium text-ink-2',
  estimated: 'bg-estimated-tint text-estimated-ink',
  verified: 'bg-verified-tint text-verified-ink',
  hotspot: 'bg-hotspot-tint text-hotspot-ink',
}
