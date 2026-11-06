import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

// Small building blocks shared by the simulator panel: a titled section and a quiet tag such as "illustrative".

export function Section({ title, tag, children }: { title: string; tag?: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-1.5">
      <h3 className="flex items-center gap-1.5 text-[13px] font-medium text-muted-foreground">
        {title}
        {tag !== undefined && <Tag>{tag}</Tag>}
      </h3>
      {children}
    </section>
  )
}

export function Rows({ children }: { children: ReactNode }) {
  return <dl className="flex flex-col divide-y">{children}</dl>
}

export function Row({ label, tag, children }: { label: string; tag?: string; children: ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-3 py-1.5">
      <dt className="flex items-center gap-1.5 text-muted-foreground">
        {label}
        {tag !== undefined && <Tag>{tag}</Tag>}
      </dt>
      <dd className="text-right tabular-nums">{children}</dd>
    </div>
  )
}

export function Tag({ tone, children }: { tone?: 'warning'; children: ReactNode }) {
  return (
    <span
      className={cn(
        'shrink-0 rounded-md px-1.5 py-px text-[11px] font-medium',
        tone === 'warning' ? 'bg-estimated-tint font-semibold text-estimated-ink' : 'bg-fill text-muted-foreground',
      )}
    >
      {children}
    </span>
  )
}
