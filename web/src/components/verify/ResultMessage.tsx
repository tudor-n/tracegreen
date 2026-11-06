import { CheckIcon, CircleAlertIcon, TriangleAlertIcon, WifiOffIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

// How the supplier page ends: the result in large type, and the "please reconfirm" banner that never blocks (D2).

const TONES = {
  success: { Icon: CheckIcon, color: 'bg-verified text-white shadow-[0_12px_28px_-10px_rgb(18_129_79/0.55)]', role: 'status' },
  retry: { Icon: WifiOffIcon, color: 'bg-estimated-tint text-estimated-ink', role: 'alert' },
  final: { Icon: CircleAlertIcon, color: 'bg-hotspot-tint text-hotspot-ink', role: 'alert' },
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
    <section role={role} className="flex flex-col items-center gap-4 py-16 text-center">
      <span className={cn('inline-flex size-[72px] items-center justify-center rounded-full motion-safe:animate-in motion-safe:zoom-in-90 motion-safe:fade-in-0 motion-safe:duration-700', color)}>
        <Icon aria-hidden strokeWidth={2.4} className="size-9" />
      </span>
      <h1 className="mt-2 font-display text-[28px] font-bold tracking-[-0.025em]">{title}</h1>
      <p className="max-w-[30ch] text-[17px] leading-relaxed text-ink-2">{body}</p>
      {children}
    </section>
  )
}

export function ReconfirmBanner({ title, lines }: { title: string; lines: string[] }) {
  return (
    <section
      role="status"
      className="flex gap-3 rounded-2xl bg-estimated-tint p-4 text-estimated-ink"
    >
      <TriangleAlertIcon aria-hidden className="mt-0.5 size-5 shrink-0" />
      <div className="flex flex-col gap-1">
        <h2 className="text-[17px] font-semibold">{title}</h2>
        {lines.map((line) => (
          <p key={line} className="text-base">
            {line}
          </p>
        ))}
      </div>
    </section>
  )
}
