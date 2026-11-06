import { LayoutGridIcon, type LucideIcon, ScrollTextIcon, WaypointsIcon } from 'lucide-react'
import { NavLink } from 'react-router'
import { Wordmark } from '@/components/brand/Wordmark'
import { ConnectionBadge } from '@/components/dashboard/ConnectionBadge'
import { Toolbar } from '@/components/dashboard/Toolbar'
import { auditPanelCopy, shellCopy } from '@/copy/dashboard'
import type { Connection } from '@/hooks/useGraphEvents'
import { cn } from '@/lib/utils'
import { useGraphStore } from '@/store'

// The v2 sidebar: the wordmark, one link per screen, the live-updates badge, and the invoice actions at the bottom.
// On a narrow screen it sits above the content as a band. `toolbar-audit` is a frozen test hook (RUNBOOK).

export function Sidebar({ connection }: { connection: Connection }) {
  const supplierCount = useGraphStore((s) => Object.keys(s.nodes).length)
  return (
    <nav
      aria-label={shellCopy.sections}
      className="flex flex-col gap-5 bg-sidebar px-3.5 py-5 shadow-[inset_0_-0.5px_0_var(--hairline)] md:sticky md:top-0 md:h-dvh md:w-56 md:shrink-0 md:shadow-[inset_-0.5px_0_0_var(--hairline)]"
    >
      <div className="flex items-center justify-between gap-3 px-2.5 pt-1">
        <NavLink to="/" aria-label="TraceGreen" className="rounded-md">
          <Wordmark size={20} label={null} />
        </NavLink>
        <ConnectionBadge connection={connection} />
      </div>
      <ul className="flex flex-row flex-wrap gap-0.5 md:flex-col">
        <Item to="/" icon={LayoutGridIcon} label={shellCopy.overview} end />
        <Item to="/suppliers" icon={WaypointsIcon} label={shellCopy.suppliers} count={supplierCount} />
        <Item to="/audit" icon={ScrollTextIcon} label={auditPanelCopy.open} testId="toolbar-audit" />
      </ul>
      <div className="md:mt-auto">
        <Toolbar connection={connection} />
      </div>
    </nav>
  )
}

interface ItemProps {
  to: string
  icon: LucideIcon
  label: string
  count?: number
  end?: boolean
  testId?: string
}

function Item({ to, icon: Icon, label, count, end, testId }: ItemProps) {
  return (
    <li>
      <NavLink
        to={to}
        end={end}
        data-testid={testId}
        className={({ isActive }) =>
          cn(
            'flex h-9 items-center gap-2.5 rounded-[9px] px-2.5 text-sm transition-colors duration-200',
            isActive ? 'bg-sidebar-accent font-semibold text-foreground' : 'font-medium text-ink-2 hover:bg-fill-strong',
          )
        }
      >
        <Icon className="size-[17px]" strokeWidth={1.7} aria-hidden />
        <span className="flex-1">{label}</span>
        {count !== undefined && count > 0 && <span className="text-xs font-normal text-muted-foreground">{count}</span>}
      </NavLink>
    </li>
  )
}
