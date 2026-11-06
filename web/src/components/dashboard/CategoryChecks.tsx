import { ChevronRightIcon, CircleAlertIcon } from 'lucide-react'
import { Link } from 'react-router'
import { reviewCopy } from '@/copy/dashboard'
import { CHECK_LIST_PATH } from '@/lib/review-display'
import { useGraphStore } from '@/store'

// The Overview's counter of categories waiting for a person (F6). It opens the supplier list filtered to them, with the
// side panel closed, so the next click is a row. Nothing when no category waits. Test hook: `overview-checks` with
// `data-count`.

export function CategoryChecks({ count }: { count: number }) {
  const select = useGraphStore((s) => s.select)
  if (count === 0) return null
  return (
    <Link
      to={CHECK_LIST_PATH}
      data-testid="overview-checks"
      data-count={count}
      onClick={() => select(null)}
      className="flex items-center gap-4 rounded-[18px] bg-estimated-tint px-6 py-5 text-estimated-ink shadow-card transition-[box-shadow] outline-none hover:shadow-raised focus-visible:ring-3 focus-visible:ring-ring"
    >
      <CircleAlertIcon className="size-5 shrink-0" strokeWidth={2.2} aria-hidden />
      <span className="flex min-w-0 flex-1 flex-col gap-0.5">
        <span className="text-[17px] font-semibold tracking-[-0.015em]">{reviewCopy.count(count)}</span>
        <span className="text-sm">{reviewCopy.countBody}</span>
      </span>
      <ChevronRightIcon className="size-5 shrink-0" aria-hidden />
    </Link>
  )
}
