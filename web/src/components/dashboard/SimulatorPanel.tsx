import { useEffect, useRef, useState, type ReactNode } from 'react'
import type { NodeView, ScenarioChange, ScenarioResult } from '@/api/client'
import { Tag } from '@/components/dashboard/DrawerParts'
import { FactorInfo } from '@/components/graph/FactorInfo'
import { ScenarioBadge } from '@/components/graph/ScenarioBadge'
import { Button } from '@/components/ui/button'
import { scenarioCopy, simulatorCopy, transportModeCopy } from '@/copy/dashboard'
import { ILLUSTRATIVE } from '@/copy/labels'
import { simulateChange } from '@/lib/dashboard-actions'
import { DELTA_STEP, type DeltaDirection, deltaDirection, modeOptions, scenarioTitle } from '@/lib/scenario-display'
import { cn } from '@/lib/utils'
import {
  baselineOf,
  CO2E_UNIT,
  formatDays,
  formatDaysDelta,
  formatEuros,
  formatEurosDelta,
  formatTonnesCo2e,
  formatTonnesDelta,
  type ScenarioPreview,
  useGraphStore,
} from '@/store'

// The simulator (B5), at the top of the node drawer while "Simulate" is pressed. Picking a mode asks the API for the
// change; the answer becomes the store's scenario preview, which this panel and the ghost node on the canvas both show.
// The base node is never touched. Every figure, delta, the MAC and its badge come from the API as they are; cost, lead
// time and the MAC rest on illustrative lane figures and carry the label. A later pick wins over an earlier answer.

export function SimulatorPanel({ node }: { node: NodeView }) {
  const preview = useGraphStore((s) => (s.scenarioPreview?.nodeId === node.id ? s.scenarioPreview : null))
  const setPreview = useGraphStore((s) => s.setScenarioPreview)
  const [pending, setPending] = useState<ScenarioChange | null>(null)
  const [error, setError] = useState<string | null>(null)
  const latest = useRef(0)
  const panel = useRef<HTMLElement>(null)
  const options = modeOptions(node.lane)

  // The panel opens at the top of the drawer; bring it into view when the drawer is scrolled down. An answer that
  // arrives after the panel closed must not put a ghost back on the canvas, so closing invalidates it.
  useEffect(() => {
    panel.current?.scrollIntoView({ block: 'nearest' })
    const pickCount = latest
    return () => {
      pickCount.current += 1
    }
  }, [])

  const onPick = async (change: ScenarioChange) => {
    const ticket = ++latest.current
    const baseline = baselineOf(node)
    setPending(change)
    setError(null)
    const answer = await simulateChange(node.id, change)
    if (ticket !== latest.current) return
    setPending(null)
    if (answer.ok) {
      setPreview({ nodeId: node.id, change, result: answer.data, baseline })
    } else {
      setPreview(null)
      setError(answer.message)
    }
  }

  const picked = pending?.mode ?? preview?.change.mode ?? null

  let body: ReactNode
  if (pending !== null) body = <Message>{simulatorCopy.simulating}</Message>
  else if (error !== null)
    body = (
      <p role="alert" data-testid="sim-error" className="text-xs text-destructive">
        {error}
      </p>
    )
  else if (preview !== null) body = <ScenarioView preview={preview} />
  else if (options.some((o) => o.available && !o.current)) body = <Message>{simulatorCopy.pick}</Message>
  else body = <Message>{simulatorCopy.onlyMode}</Message>

  return (
    <section ref={panel} data-testid="simulator" className="flex flex-col gap-3 rounded-lg border p-3">
      <h3 className="text-xs font-semibold tracking-wide text-muted-foreground uppercase">{simulatorCopy.title}</h3>
      <div className="flex flex-col gap-1.5">
        <div role="group" aria-label={simulatorCopy.modes} className="grid grid-cols-4 gap-1.5">
          {options.map(({ mode, available, current }) => (
            <Button
              key={mode}
              size="sm"
              variant={picked === mode ? 'default' : 'outline'}
              aria-pressed={picked === mode}
              disabled={!available || current}
              data-testid={`sim-mode-${mode}`}
              data-available={available}
              className="h-auto flex-col gap-0 py-1"
              onClick={() => void onPick({ mode, alternativeId: null })}
            >
              {transportModeCopy[mode]}
              {current && <span className="text-[10px] font-normal">{simulatorCopy.current}</span>}
            </Button>
          ))}
        </div>
        {options.some((o) => !o.available) && <p className="text-xs text-muted-foreground">{simulatorCopy.noLeg}</p>}
      </div>
      <div aria-live="polite">{body}</div>
    </section>
  )
}

/** The API's result for the picked change. A result without a mass has no figures to show (`massUnknown`). */
function ScenarioView({ preview }: { preview: ScenarioPreview }) {
  const { change, result } = preview
  return (
    <div data-testid="sim-result" data-badge={result.massUnknown ? undefined : result.badge} className="flex flex-col gap-2">
      <div className="flex items-center justify-between gap-2">
        <p className="text-sm font-medium">{scenarioTitle(change)}</p>
        {!result.massUnknown && <ScenarioBadge badge={result.badge} />}
      </div>
      {result.massUnknown ? (
        <p data-testid="sim-mass-unknown" className="text-xs text-muted-foreground">
          {scenarioCopy.massUnknown}
        </p>
      ) : (
        <Figures result={result} />
      )}
    </div>
  )
}

function Figures({ result }: { result: ScenarioResult }) {
  const { delta, mac } = result
  return (
    <dl className="flex flex-col divide-y text-sm">
      <DeltaRow
        label={simulatorCopy.emissions}
        value={
          <span className="inline-flex items-center gap-1.5">
            {formatTonnesCo2e(result.carbonKg)}
            <FactorInfo factors={result.inputs} />
          </span>
        }
        delta={`${formatTonnesDelta(delta.carbonKg)} ${CO2E_UNIT}`}
        direction={deltaDirection(delta.carbonKg, DELTA_STEP.carbonKg)}
        testId="sim-delta-carbon"
      />
      <DeltaRow
        label={simulatorCopy.cost}
        tag={ILLUSTRATIVE}
        value={formatEuros(result.costEur)}
        delta={formatEurosDelta(delta.costEur)}
        direction={deltaDirection(delta.costEur, DELTA_STEP.costEur)}
        testId="sim-delta-cost"
      />
      <DeltaRow
        label={simulatorCopy.leadTime}
        tag={ILLUSTRATIVE}
        value={formatDays(result.leadDays)}
        delta={formatDaysDelta(delta.leadDays)}
        direction={deltaDirection(delta.leadDays, DELTA_STEP.leadDays)}
        testId="sim-delta-lead"
      />
      {mac.macEurPerT !== null && (
        <div className="flex items-baseline justify-between gap-3 py-1.5">
          <dt className="flex items-center gap-1.5 text-muted-foreground">
            {simulatorCopy.mac}
            <Tag>{ILLUSTRATIVE}</Tag>
          </dt>
          <dd data-testid="sim-mac" className="text-right tabular-nums">
            {simulatorCopy.perTonne(formatEuros(mac.macEurPerT), CO2E_UNIT)}
          </dd>
        </div>
      )}
    </dl>
  )
}

// The status hues, a shade darker so small text keeps a 4.5:1 contrast on the card.
const directionTone: Record<DeltaDirection, string> = {
  lower: 'text-green-700',
  higher: 'text-amber-700',
  same: 'text-muted-foreground',
}

/** Label, the scenario's value, and below it the change against the node now, green when lower and amber when higher. */
function DeltaRow({
  label,
  tag,
  value,
  delta,
  direction,
  testId,
}: {
  label: string
  tag?: string
  value: ReactNode
  delta: string
  direction: DeltaDirection
  testId: string
}) {
  return (
    <div className="flex items-baseline justify-between gap-3 py-1.5">
      <dt className="flex items-center gap-1.5 text-muted-foreground">
        {label}
        {tag !== undefined && <Tag>{tag}</Tag>}
      </dt>
      <dd className="flex flex-col items-end tabular-nums">
        {value}
        <span data-testid={testId} data-direction={direction} className={cn('text-xs font-medium', directionTone[direction])}>
          {scenarioCopy.versusNow(delta)}
        </span>
      </dd>
    </div>
  )
}

function Message({ children }: { children: ReactNode }) {
  return <p className="text-xs text-muted-foreground">{children}</p>
}
