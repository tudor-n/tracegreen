import { InfoIcon } from 'lucide-react'
import type { Kpis } from '@/api/client'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { StatusDot } from '@/components/graph/StatusDot'
import { kpiCopy, nodeCopy } from '@/copy/dashboard'
import { supplierConfirmedShare } from '@/copy/labels'
import { useChangeCount } from '@/hooks/useChangeCount'
import type { Connection } from '@/hooks/useGraphEvents'
import { cn } from '@/lib/utils'
import { CO2E_UNIT, formatKpis } from '@/store'

// Three KPIs exactly as the API last sent them, in the snapshot or on a node event, and the status legend. Nothing is
// computed here; `invoices` is a count of the invoices on the graph.

const TILES = 'grid grid-cols-2 gap-3 min-[1000px]:grid-cols-4'

export function KpiBar({ kpis, invoices, connection }: { kpis: Kpis | null; invoices: number; connection: Connection }) {
  if (kpis === null) {
    return connection === 'offline' ? (
      <p role="alert" data-testid="kpi-bar" className="glass rounded-[var(--r-tile)] px-4 py-3 text-sm text-destructive">
        {kpiCopy.unavailable}
      </p>
    ) : (
      <div aria-hidden data-testid="kpi-bar" className={TILES}>
        {[0, 1, 2, 3].map((i) => (
          <div key={i} className="glass h-[92px] rounded-[var(--r-tile)] motion-safe:animate-pulse" />
        ))}
      </div>
    )
  }

  const figures = formatKpis(kpis)
  return (
    <dl aria-label={kpiCopy.region} data-testid="kpi-bar" className={TILES}>
      <Kpi
        testId="kpi-total"
        label={kpiCopy.total}
        value={figures.total}
        unit={CO2E_UNIT}
        subtitle={kpiCopy.totalSub(kpis.verifiedCount, kpis.nodeCount)}
      />
      <Kpi
        testId="kpi-share"
        label={supplierConfirmedShare.label}
        value={figures.share}
        subtitle={supplierConfirmedShare.subtitle}
        info={supplierConfirmedShare.tooltip}
        meter={kpis.totalKg > 0 ? kpis.primaryShare : undefined}
      />
      <Kpi testId="kpi-spend" label={kpiCopy.spend} value={figures.spend} subtitle={kpiCopy.spendSub(invoices)} />
      <div data-testid="kpi-legend" className="glass flex min-h-[92px] flex-col gap-1.5 rounded-[var(--r-tile)] px-4 pt-[13px] pb-3.5">
        <dt className="text-xs font-semibold text-ink-2">{kpiCopy.status}</dt>
        <dd className="mt-0.5 grid grid-cols-2 gap-x-3 gap-y-2 text-xs text-ink-2">
          {(['VERIFIED', 'PARTIAL', 'ESTIMATED'] as const).map((status) => (
            <span key={status} className="flex items-center gap-[7px]">
              <StatusDot status={status} />
              {nodeCopy.status[status]}
            </span>
          ))}
          <span className="flex items-center gap-[7px]">
            <span aria-hidden className="size-2.5 flex-none rounded-full shadow-[0_0_0_2px_var(--red)]" />
            {kpiCopy.hotspot}
          </span>
        </dd>
      </div>
    </dl>
  )
}

interface KpiProps {
  testId: string
  label: string
  value: string
  unit?: string
  subtitle?: string
  info?: string
  /** A 0–1 share drawn as the glowing progress meter, straight from the API. */
  meter?: number
}

function Kpi({ testId, label, value, unit, subtitle, info, meter }: KpiProps) {
  const changes = useChangeCount(value)
  const fill = meter !== undefined && Number.isFinite(meter) ? Math.min(1, Math.max(0, meter)) : null
  return (
    <div data-testid={testId} className="glass flex min-h-[92px] flex-col gap-1.5 rounded-[var(--r-tile)] px-4 pt-[13px] pb-3.5">
      <dt className="flex items-center gap-1 text-xs font-semibold text-ink-2">
        {label}
        {info !== undefined && (
          <Tooltip>
            <TooltipTrigger
              aria-label={kpiCopy.shareInfo}
              className="inline-flex rounded-full hover:text-foreground"
            >
              <InfoIcon className="size-3.5" aria-hidden />
            </TooltipTrigger>
            <TooltipContent className="max-w-xs text-left">{info}</TooltipContent>
          </Tooltip>
        )}
      </dt>
      <dd className="flex items-baseline gap-1">
        <span
          key={changes}
          className={cn('font-heading text-[28px] leading-none font-semibold tracking-[-0.025em]', changes > 0 && 'number-flash')}
        >
          {value}
        </span>
        {unit !== undefined && <span className="ml-0.5 text-[13px] font-medium text-mute">{unit}</span>}
      </dd>
      {fill !== null && (
        <dd aria-hidden className="meter">
          <span className="meter-fill" style={{ transform: `scaleX(${fill})` }} />
        </dd>
      )}
      {subtitle !== undefined && <dd className="text-xs text-mute">{subtitle}</dd>}
    </div>
  )
}
