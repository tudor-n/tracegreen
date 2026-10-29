import { CircleAlertIcon, CircleCheckIcon, TriangleAlertIcon, WifiOffIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

// How the supplier page ends: the result in large type, and the "please reconfirm" banner that never blocks (D2).

const TONES = {
  success: { Icon: CircleCheckIcon, color: 'text-[var(--status-verified)]', role: 'status' },
  retry: { Icon: WifiOffIcon, color: 'text-[var(--status-estimated)]', role: 'alert' },
  final: { Icon: CircleAlertIcon, color: 'text-destructive', role: 'alert' },
} as const

interface ResultMessageProps {
  tone: keyof typeof TONES
  title: string
  body: string
  /** A retry button, when the failure allows one. */
  children?: ReactNode
}

export function ResultMessage({ tone, title, body, children }: ResultMessageProps) {
  const { Icon, color, role } = TONES[tone]
  return (
    <section role={role} className="flex flex-col items-center gap-3 py-8 text-center">
      <Icon aria-hidden className={cn('size-16', color)} />
      <h1 className="text-2xl font-bold">{title}</h1>
      <p className="text-lg">{body}</p>
      {children}
    </section>
  )
}

export function ReconfirmBanner({ title, lines }: { title: string; lines: string[] }) {
  return (
    <section
      role="status"
      className="flex gap-3 rounded-xl border-2 border-[var(--status-estimated)] bg-[color-mix(in_srgb,var(--status-estimated)_12%,var(--background))] p-4"
    >
      <TriangleAlertIcon aria-hidden className="mt-0.5 size-6 shrink-0 text-[var(--status-estimated)]" />
      <div className="flex flex-col gap-1">
        <h2 className="text-lg font-semibold">{title}</h2>
        {lines.map((line) => (
          <p key={line} className="text-base">
            {line}
          </p>
        ))}
      </div>
    </section>
  )
}
