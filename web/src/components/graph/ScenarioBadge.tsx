import type { MacBadge } from '@/api/client'
import { macBadgeLabel } from '@/copy/labels'

// The MAC badge of a simulated change, as the API decided it. Its tone follows `data-badge` (index.css); the ghost
// node and the simulator panel both show it, and the Playwright smoke reads the same attribute.

export function ScenarioBadge({ badge }: { badge: MacBadge }) {
  return (
    <span
      data-badge={badge}
      className="scenario-badge shrink-0 rounded-full px-1.5 py-0.5 text-[10px] leading-none font-semibold whitespace-nowrap"
    >
      {macBadgeLabel[badge]}
    </span>
  )
}
