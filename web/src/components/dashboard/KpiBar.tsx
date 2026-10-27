import { InfoIcon } from 'lucide-react'
import type { Kpis } from '@/api/client'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { kpiCopy } from '@/copy/dashboard'
import { useChangeCount } from '@/hooks/useChangeCount'
import { supplierConfirmedShare } from '@/copy/labels'
import type { Connection } from '@/hooks/useGraphEvents'
import { cn } from '@/lib/utils'
import { CO2E_UNIT, formatKpis } from '@/store'

// The four KPIs exactly as the API last sent them, in the snapshot or on a node event. Nothing is computed here.

export function KpiBar({ kpis, connection }: { kpis: Kpis | null; connection: Connection }) {
  if (kpis === null) {
    return connection === 'offline' ? (
      <p role="alert" data-testid="kpi-bar" className="glass rounded-[var(--r-tile)] px-4 py-3 text-sm text-destructive">
        {kpiCopy.unavailable}
      </p>
    ) : (
      <div aria-hidden data-testid="kpi-bar" className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {[0, 1, 2, 3].map((i) => (
          <div key={i} className="glass h-[5.5rem] rounded-[var(--r-tile)] motion-safe:animate-pulse" />
        ))}
      </div>
    )
  }

  const figures = formatKpis(kpis)
  return (
    <dl aria-label={kpiCopy.region} data-testid="kpi-bar" className="grid grid-cols-2 gap-3 lg:grid-cols-4">
      <Kpi testId="kpi-total" label={kpiCopy.total} value={figures.total} unit={CO2E_UNIT} />
      <Kpi
        testId="kpi-share"
        label={supplierConfirmedShare.label}
        value={figures.share}
        subtitle={supplierConfirmedShare.subtitle}
        info={supplierConfirmedShare.tooltip}
        meter={kpis.totalKg > 0 ? kpis.primaryShare : undefined}
      />
      <Kpi testId="kpi-spend" label={kpiCopy.spend} value={figures.spend} />
      <Kpi testId="kpi-nodes" label={kpiCopy.nodes} value={figures.nodes} />
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
    <div data-testid={testId} className="glass flex min-h-[5.5rem] flex-col gap-1 rounded-[var(--r-tile)] px-4 py-3">
      <dt className="flex items-center gap-1 text-xs font-medium text-ink-2">
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
        {unit !== undefined && <span className="text-[13px] font-medium text-mute">{unit}</span>}
      </dd>
      {subtitle !== undefined && <dd className="text-xs text-mute">{subtitle}</dd>}
      {fill !== null && (
        <dd aria-hidden className="meter mt-auto">
          <span className="meter-fill" style={{ transform: `scaleX(${fill})` }} />
        </dd>
      )}
    </div>
  )
}
