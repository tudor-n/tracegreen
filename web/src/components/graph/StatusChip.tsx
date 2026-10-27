import type { NodeStatus } from '@/api/client'
import { nodeCopy } from '@/copy/dashboard'

// Status as a dot and a word, so colour is never the only cue: amber estimated, striped partial, glowing green
// verified (index.css, "status language").

export function StatusDot({ status }: { status: NodeStatus }) {
  return <span className="status-dot" data-status={status} aria-hidden />
}

export function StatusChip({ status }: { status: NodeStatus }) {
  return (
    <span className="status-chip" data-status={status}>
      <StatusDot status={status} />
      {nodeCopy.status[status]}
    </span>
  )
}
