import { connectionCopy } from '@/copy/dashboard'
import type { Connection } from '@/hooks/useGraphEvents'
import { cn } from '@/lib/utils'

// The state of the live updates from useGraphEvents, shown only when something is wrong: `polling` while the event
// stream is down, or `offline` while the API cannot be reached. While `connecting` (until the first snapshot) and
// `live` there is nothing to say, so the region is empty. Green keeps meaning "supplier-confirmed".

const dot: Record<'polling' | 'offline', string> = {
  polling: 'bg-estimated pulse-dot',
  offline: 'bg-destructive',
}

export function ConnectionBadge({ connection }: { connection: Connection }) {
  // The status region stays mounted and empty while all is well, so a screen reader announces the text when it appears.
  const bad = connection === 'polling' || connection === 'offline'
  return (
    <span
      role="status"
      data-testid="connection-badge"
      data-state={connection}
      title={bad ? connectionCopy.detail[connection] : undefined}
      className="inline-flex h-5 shrink-0 items-center gap-1.5 text-xs font-medium text-ink-2"
    >
      {bad && (
        <>
          <span aria-hidden className={cn('size-[7px] rounded-full', dot[connection])} />
          {connectionCopy.state[connection]}
        </>
      )}
    </span>
  )
}
