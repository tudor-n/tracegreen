import { InfoIcon } from 'lucide-react'
import { useMemo } from 'react'
import { Link, useNavigate } from 'react-router'
import type { Kpis, NodeView } from '@/api/client'
import { AnswerChecks } from '@/components/dashboard/AnswerChecks'
import { CategoryChecks } from '@/components/dashboard/CategoryChecks'
import { PageHeader, PageMessage, Surface } from '@/components/shell/Page'
import { Button } from '@/components/ui/button'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { answersCopy, canvasCopy, kpiCopy, overviewCopy } from '@/copy/dashboard'
import { supplierConfirmedShare } from '@/copy/labels'
import { categoriesToCheck } from '@/lib/review-display'
import { answersToCheck } from '@/lib/trust-display'
import { byCarbonDesc, CO2E_UNIT, formatKpis, formatShareSplit, formatTonnesCo2e, selectLoaded, useGraphStore } from '@/store'
import { useShell } from '@/hooks/useShell'

// "Where do I stand?": the total, the supplier-confirmed share and how the total splits, and the one next step. Every
// figure is the API's KPI as it is; the next step is the largest figure no supplier has confirmed yet.

export default function Overview() {
  const { connection } = useShell()
  const loaded = useGraphStore(selectLoaded)
  const kpis = useGraphStore((s) => s.kpis)
  const buyer = useGraphStore((s) => s.buyer)
  const nodes = useGraphStore((s) => s.nodes)
  const next = useMemo(() => nextToConfirm(Object.values(nodes)), [nodes])
  const checks = useMemo(() => categoriesToCheck(Object.values(nodes)).length, [nodes])
  const answers = useMemo(() => answersToCheck(Object.values(nodes)), [nodes])

  let body
  if (!loaded || kpis === null) {
    body =
      connection === 'offline' ? (
        <PageMessage tone="error">{kpiCopy.unavailable}</PageMessage>
      ) : (
        <PageMessage busy>{canvasCopy.loading}</PageMessage>
      )
  } else if (Object.keys(nodes).length === 0) {
    body = <PageMessage>{canvasCopy.empty}</PageMessage>
  } else {
    body = (
      <>
        <CategoryChecks count={checks} />
        <AnswerChecks nodes={answers} kgAtRisk={kpis.kgAtRisk} />
        <Cards kpis={kpis} next={next} />
      </>
    )
  }

  return (
    <main className="min-w-0 flex-1 px-5 py-8 md:px-12 md:py-11">
      <div className="flex flex-col gap-7">
        <PageHeader title={overviewCopy.title} subtitle={buyer === null ? undefined : `${buyer.name} · ${buyer.city}, ${buyer.country}`} />
        {body}
      </div>
    </main>
  )
}

/** The supplier with the largest figure that is not VERIFIED yet, or null when every supplier has confirmed. */
function nextToConfirm(nodes: readonly NodeView[]): NodeView | null {
  return nodes.filter((n) => n.status !== 'VERIFIED').sort(byCarbonDesc)[0] ?? null
}

function Cards({ kpis, next }: { kpis: Kpis; next: NodeView | null }) {
  const figures = formatKpis(kpis)
  const hasTotal = kpis.totalKg > 0
  const { confirmed, estimated } = formatShareSplit(kpis)
  const select = useGraphStore((s) => s.select)
  const setPanel = useGraphStore((s) => s.setPanel)
  const navigate = useNavigate()

  return (
    <div data-testid="kpi-bar" className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-4">
        <Surface aria-label={kpiCopy.total} data-testid="kpi-total" className="flex flex-[1_1_300px] flex-col gap-3">
          <h2 className="text-[15px] font-semibold">{kpiCopy.total}</h2>
          <BigFigure value={figures.total} unit={CO2E_UNIT} />
          <p data-testid="kpi-nodes" className="text-[13px] text-muted-foreground">
            <span data-testid="kpi-spend">{overviewCopy.totalCaption(kpis.nodeCount, figures.nodes, figures.spend)}</span>
          </p>
          {kpis.kgAtRisk > 0 && (
            // F7: the totals count every unbacked reduction's fallback; this is what backing them could take off.
            <p data-testid="kpi-at-risk" className="text-[13px] font-medium text-estimated-ink">
              {answersCopy.atRiskKpi(formatTonnesCo2e(kpis.kgAtRisk))}
            </p>
          )}
        </Surface>
        <Surface aria-label={supplierConfirmedShare.label} data-testid="kpi-share" className="flex flex-[1_1_300px] flex-col gap-3">
          <h2 className="flex items-center gap-1.5 text-[15px] font-semibold">
            {supplierConfirmedShare.label}
            <Tooltip>
              <TooltipTrigger
                aria-label={kpiCopy.shareInfo}
                className="inline-flex size-[22px] items-center justify-center rounded-full text-muted-foreground hover:text-foreground"
              >
                <InfoIcon className="size-[15px]" aria-hidden />
              </TooltipTrigger>
              <TooltipContent className="max-w-xs text-left">{supplierConfirmedShare.tooltip}</TooltipContent>
            </Tooltip>
          </h2>
          <BigFigure value={figures.share} />
          <p className="text-[13px] text-muted-foreground">{supplierConfirmedShare.subtitle}</p>
        </Surface>
      </div>

      <Surface aria-label={overviewCopy.split} className="flex flex-col gap-4">
        <div className="flex items-baseline justify-between gap-4">
          <h2 className="text-[15px] font-semibold">{overviewCopy.split}</h2>
          <Link to="/suppliers" className="text-[13px] font-medium text-verified-ink hover:text-verified">
            {overviewCopy.allSuppliers}
          </Link>
        </div>
        <span role="img" aria-label={overviewCopy.splitLabel(confirmed, estimated)} className="flex h-3.5 gap-[3px]">
          {hasTotal && kpis.estimatedShare > 0 && (
            <span className="rounded-full bg-estimated" style={{ flexGrow: kpis.estimatedShare }} />
          )}
          {hasTotal && kpis.primaryShare > 0 && (
            <span className="rounded-full bg-verified" style={{ flexGrow: kpis.primaryShare }} />
          )}
          {!hasTotal && <span className="flex-1 rounded-full bg-fill" />}
        </span>
        <dl className="flex flex-wrap gap-x-10 gap-y-3">
          <Legend tone="bg-estimated" label={overviewCopy.estimated} value={estimated} />
          <Legend tone="bg-verified" label={overviewCopy.confirmed} value={confirmed} />
        </dl>
      </Surface>

      <Surface aria-label={next === null ? overviewCopy.allConfirmed : overviewCopy.nextTitle(next.label)} className="flex flex-wrap items-center gap-x-6 gap-y-4">
        {next === null ? (
          <p className="text-[15px] font-medium">{overviewCopy.allConfirmed}</p>
        ) : (
          <>
            <div className="flex flex-[1_1_360px] flex-col gap-1.5">
              <h2 className="text-[17px] font-semibold tracking-[-0.015em]">{overviewCopy.nextTitle(next.label)}</h2>
              <p className="text-sm text-ink-2">{overviewCopy.nextBody}</p>
            </div>
            <Button
              size="lg"
              data-testid="overview-next"
              onClick={() => {
                // The step promises the request card, so the panel opens on Summary even if What if was left open.
                select(next.id)
                setPanel('simulator', false)
                void navigate('/suppliers')
              }}
            >
              {overviewCopy.nextAction}
            </Button>
          </>
        )}
      </Surface>
    </div>
  )
}

function BigFigure({ value, unit }: { value: string; unit?: string }) {
  return (
    <p className="flex items-baseline gap-1.5 font-display leading-none">
      <span className="text-5xl font-semibold tracking-[-0.03em]">{value}</span>
      {unit !== undefined && <span className="text-[15px] font-medium text-muted-foreground">{unit}</span>}
    </p>
  )
}

function Legend({ tone, label, value }: { tone: string; label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <dt className="flex items-center gap-2 text-[13px] text-ink-2">
        <span aria-hidden className={`size-2 rounded-full ${tone}`} />
        {label}
      </dt>
      <dd className="text-xl font-semibold tracking-[-0.02em]">{value}</dd>
    </div>
  )
}
