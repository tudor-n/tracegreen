import { connectionCopy } from '@/copy/dashboard'
import type { Connection } from '@/hooks/useGraphEvents'
import { cn } from '@/lib/utils'

// The state of the live updates from useGraphEvents: `connecting` until the first snapshot only, then `live`,
// `polling` while the event stream is down, or `offline` while the API cannot be reached. The Playwright smoke reads
// `data-state`.

const dot: Record<Connection, string> = {
  connecting: 'bg-muted-foreground motion-safe:animate-pulse',
  live: 'bg-(--status-verified)',
  polling: 'bg-(--status-estimated)',
  offline: 'bg-destructive',
}

export function ConnectionBadge({ connection }: { connection: Connection }) {
  return (
    <span
      role="status"
      data-testid="connection-badge"
      data-state={connection}
      title={connectionCopy.detail[connection]}
      className="inline-flex h-5 shrink-0 items-center gap-1.5 rounded-full border px-2 text-xs font-medium text-muted-foreground"
    >
      <span aria-hidden className={cn('size-2 rounded-full', dot[connection])} />
      {connectionCopy.state[connection]}
    </span>
  )
}
