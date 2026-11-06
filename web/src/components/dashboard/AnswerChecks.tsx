import { ChevronRightIcon, CircleAlertIcon } from 'lucide-react'
import { Link } from 'react-router'
import type { NodeView } from '@/api/client'
import { answersCopy } from '@/copy/dashboard'
import { ANSWERS_LIST_PATH, answersBody } from '@/lib/trust-display'
import { useGraphStore } from '@/store'

// The Overview's counter of supplier answers waiting for a person (F7): a reduction nothing backs yet, a contradiction
// or a weak score. It opens the supplier list filtered to them, the most kilograms at risk first, with the side panel
// closed. Nothing when no answer waits. Test hook: `overview-answers` with `data-count`.

export function AnswerChecks({ nodes, kgAtRisk }: { nodes: readonly NodeView[]; kgAtRisk: number }) {
  const select = useGraphStore((s) => s.select)
  if (nodes.length === 0) return null
  return (
    <Link
      to={ANSWERS_LIST_PATH}
      data-testid="overview-answers"
      data-count={nodes.length}
      onClick={() => select(null)}
      className="flex items-center gap-4 rounded-[18px] bg-estimated-tint px-6 py-5 text-estimated-ink shadow-card transition-[box-shadow] outline-none hover:shadow-raised focus-visible:ring-3 focus-visible:ring-ring"
    >
      <CircleAlertIcon className="size-5 shrink-0" strokeWidth={2.2} aria-hidden />
      <span className="flex min-w-0 flex-1 flex-col gap-0.5">
        <span className="text-[17px] font-semibold tracking-[-0.015em]">{answersCopy.count(nodes.length)}</span>
        <span className="text-sm">{answersBody(kgAtRisk)}</span>
      </span>
      <ChevronRightIcon className="size-5 shrink-0" aria-hidden />
    </Link>
  )
}
