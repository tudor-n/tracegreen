import { Link } from 'react-router'
import { reviewCopy } from '@/copy/dashboard'

// The line above the supplier tree while it lists only the categories to check (F6, the Overview's counter), with the
// way back to every supplier. Test hooks: `suppliers-filter`, `suppliers-filter-clear`.

export function CheckFilter() {
  return (
    <p data-testid="suppliers-filter" className="flex flex-wrap items-center gap-x-3 gap-y-1 px-1 text-[13px] text-ink-2">
      {reviewCopy.filtered}
      <Link to="/suppliers" data-testid="suppliers-filter-clear" className="font-medium text-verified-ink hover:text-verified">
        {reviewCopy.showAll}
      </Link>
    </p>
  )
}
