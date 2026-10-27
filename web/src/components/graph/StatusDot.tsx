import type { NodeStatus } from '@/api/client'

// The 10px status dot: amber estimated, striped partial, glowing green verified (index.css). Always paired with a word
// where it is used, so colour is never the only cue.

export function StatusDot({ status }: { status: NodeStatus }) {
  return <span className="status-dot" data-status={status} aria-hidden />
}
