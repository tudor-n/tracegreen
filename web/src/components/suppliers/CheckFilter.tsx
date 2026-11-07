import { Link } from 'react-router'
import { answersCopy, reviewCopy } from '@/copy/dashboard'

// The line above the supplier tree while it lists only the categories to check (F6) or the supplier answers to check
// (F7), each opened from its Overview counter, with the way back to every supplier. Test hooks: `suppliers-filter` with
// `data-filter`, `suppliers-filter-clear`.

export function CheckFilter({ kind = 'category' }: { kind?: 'category' | 'answers' }) {
  const copy = kind === 'answers' ? answersCopy : reviewCopy
  return (
    <p data-testid="suppliers-filter" data-filter={kind} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-1 text-[13px] text-ink-2">
      {copy.filtered}
      <Link to="/suppliers" data-testid="suppliers-filter-clear" className="font-medium text-verified-ink hover:text-verified">
        {copy.showAll}
      </Link>
    </p>
  )
}
