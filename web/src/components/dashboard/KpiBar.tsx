import { InfoIcon } from 'lucide-react'
import type { Kpis } from '@/api/client'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { kpiCopy, supplierConfirmedShare } from '@/copy/dashboard'
import type { Connection } from '@/hooks/useGraphEvents'
import { CO2E_UNIT, formatKpis } from '@/store'

// The four KPIs exactly as the API last sent them, in the snapshot or on a node event. Nothing is computed here.

export function KpiBar({ kpis, connection }: { kpis: Kpis | null; connection: Connection }) {
  if (kpis === null) {
    return connection === 'offline' ? (
      <p role="alert" data-testid="kpi-bar" className="py-3 text-sm text-destructive">
        {kpiCopy.unavailable}
      </p>
    ) : (
      <div aria-hidden data-testid="kpi-bar" className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {[0, 1, 2, 3].map((i) => (
          <div key={i} className="h-[4.5rem] rounded-lg bg-muted motion-safe:animate-pulse" />
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
}

function Kpi({ testId, label, value, unit, subtitle, info }: KpiProps) {
  return (
    <div data-testid={testId} className="flex min-h-[4.5rem] flex-col rounded-lg border bg-card px-3 py-2">
      <dt className="flex items-center gap-1 text-xs font-medium text-muted-foreground">
        {label}
        {info !== undefined && (
          <Tooltip>
            <TooltipTrigger
              aria-label={kpiCopy.shareInfo}
              className="inline-flex rounded-full outline-none hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring"
            >
              <InfoIcon className="size-3.5" aria-hidden />
            </TooltipTrigger>
            <TooltipContent className="max-w-xs text-left">{info}</TooltipContent>
          </Tooltip>
        )}
      </dt>
      <dd className="flex items-baseline gap-1">
        <span className="text-2xl font-semibold tabular-nums">{value}</span>
        {unit !== undefined && <span className="text-sm text-muted-foreground">{unit}</span>}
      </dd>
      {subtitle !== undefined && <dd className="text-xs text-muted-foreground">{subtitle}</dd>}
    </div>
  )
}
