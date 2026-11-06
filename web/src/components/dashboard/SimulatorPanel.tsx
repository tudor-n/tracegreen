import { useEffect, useRef, useState, type ReactNode } from 'react'
import type { AlternativeView, NodeView, ScenarioChange, ScenarioResult, SuggestionView } from '@/api/client'
import { ExplainedTag, Tag } from '@/components/shell/Page'
import { FactorInfo } from '@/components/graph/FactorInfo'
import { ScenarioBadge } from '@/components/graph/ScenarioBadge'
import { Button } from '@/components/ui/button'
import { scenarioCopy, simulatorCopy, transportModeCopy } from '@/copy/dashboard'
import { AI_SUGGESTED, ILLUSTRATIVE } from '@/copy/labels'
import { fetchSuggestions, saveScenario, simulateChange } from '@/lib/dashboard-actions'
import {
  aiSuggestion,
  DELTA_STEP,
  type DeltaDirection,
  deltaDirection,
  modeOptions,
  sameChange,
  scenarioTitle,
} from '@/lib/scenario-display'
import { cn } from '@/lib/utils'
import {
  baselineOf,
  CO2E_UNIT,
  formatDays,
  formatDaysDelta,
  formatEuros,
  formatEurosDelta,
  formatTonnesCo2e,
  formatTonnesChange,
  type ScenarioPreview,
  useGraphStore,
} from '@/store'

// The simulator (B5), the side panel's What if tab. Picking a mode, or one of the node's
// alternative suppliers, asks the API for the change; the answer becomes the store's scenario preview, which this panel and the ghost row in the supplier tree both show.
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
  const suggestions = useSuggestions(node)

  // Bring the panel into view when the side panel is scrolled down. An answer that arrives after the panel closed
  // must not put a ghost back on the tree, so closing invalidates it.
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

  // The change on screen: the one being asked for, else the one the preview shows.
  const shown = pending ?? preview?.change ?? null
  const alternatives = node.alternatives
  const canPick = options.some((o) => o.available && !o.current) || alternatives.length > 0

  let body: ReactNode
  if (pending !== null) body = <Message>{simulatorCopy.simulating}</Message>
  else if (error !== null)
    body = (
      <p role="alert" data-testid="sim-error" className="text-xs text-destructive">
        {error}
      </p>
    )
  else if (preview !== null) body = <ScenarioView preview={preview} alternatives={alternatives} />
  else if (canPick) body = <Message>{simulatorCopy.pick}</Message>
  else body = <Message>{simulatorCopy.nothingToTry}</Message>

  return (
    <section ref={panel} data-testid="simulator" className="flex flex-col gap-4">
      <h3 className="text-[15px] font-semibold">{simulatorCopy.title}</h3>
      <div className="flex flex-col gap-1.5">
        <GroupLabel>{simulatorCopy.modes}</GroupLabel>
        <div role="group" aria-label={simulatorCopy.modes} className="grid grid-cols-4 gap-1 rounded-full bg-[#e9e9ed] p-0.5">
          {options.map(({ mode, available, current }) => {
            const change: ScenarioChange = { mode, alternativeId: null }
            const pressed = sameChange(shown, change)
            return (
              <Button
                key={mode}
                size="sm"
                variant={pressed ? 'default' : 'outline'}
                aria-pressed={pressed}
                disabled={!available || current}
                data-testid={`sim-mode-${mode}`}
                data-available={available}
                className={cn(
                  'h-auto min-h-8 flex-col gap-0 py-1',
                  !pressed && 'bg-transparent hover:bg-card/70',
                )}
                onClick={() => void onPick(change)}
              >
                {transportModeCopy[mode]}
                {current && <span className="text-[10px] font-normal">{simulatorCopy.current}</span>}
              </Button>
            )
          })}
        </div>
        {options.some((o) => !o.available) && <p className="text-xs text-muted-foreground">{simulatorCopy.noLeg}</p>}
      </div>
      <div className="flex flex-col gap-1.5">
        <GroupLabel>{simulatorCopy.suppliers}</GroupLabel>
        {alternatives.length === 0 ? (
          <p data-testid="sim-no-alternatives" className="text-xs text-muted-foreground">
            {simulatorCopy.noAlternatives}
          </p>
        ) : (
          <div role="group" aria-label={simulatorCopy.suppliers} className="flex flex-col gap-1.5">
            {alternatives.map((alternative) => {
              const change: ScenarioChange = { mode: null, alternativeId: alternative.id }
              const pressed = sameChange(shown, change)
              return (
                <div key={alternative.id} className="flex flex-col gap-1">
                  <Button
                    size="sm"
                    variant={pressed ? 'default' : 'outline'}
                    aria-pressed={pressed}
                    data-testid={`sim-alt-${alternative.id}`}
                    className="h-auto min-h-11 justify-between gap-2 rounded-2xl px-4 py-2"
                    onClick={() => void onPick(change)}
                  >
                    <span className="truncate">{alternative.label}</span>
                    <span className="flex shrink-0 items-center gap-1.5 text-[10px] font-normal">
                      {simulatorCopy.alternativeVia(alternative.country, transportModeCopy[alternative.mode])}
                      {alternative.illustrative && <Tag>{ILLUSTRATIVE}</Tag>}
                    </span>
                  </Button>
                  <Suggested pick={aiSuggestion(suggestions, alternative.id)} alternativeId={alternative.id} />
                </div>
              )
            })}
          </div>
        )}
      </div>
      <div aria-live="polite" className="rounded-2xl bg-grouped p-4">{body}</div>
    </section>
  )
}

/**
 * The AI's picks among the node's alternatives (F11), asked once when the What if tab opens and dropped when it closes
 * or the node changes. Until they arrive, or when the AI has none, the list looks exactly as without them.
 */
function useSuggestions(node: NodeView): readonly SuggestionView[] {
  const [answer, setAnswer] = useState<{ nodeId: string; suggestions: readonly SuggestionView[] } | null>(null)
  const hasAlternatives = node.alternatives.length > 0
  useEffect(() => {
    if (!hasAlternatives) return
    const controller = new AbortController()
    void fetchSuggestions(node.id, controller.signal).then((result) => {
      if (!controller.signal.aborted && result.ok) setAnswer({ nodeId: node.id, suggestions: result.data.suggestions })
    })
    return () => controller.abort()
  }, [node.id, hasAlternatives])
  return answer?.nodeId === node.id ? answer.suggestions : []
}

/** Under an alternative the AI picked: the label once, with what it means, and the AI's reason when one is kept. */
function Suggested({ pick, alternativeId }: { pick: { reason: string | null } | null; alternativeId: string }) {
  if (pick === null) return null
  return (
    <p data-testid={`sim-alt-${alternativeId}-suggested`} className="flex flex-wrap items-baseline gap-1.5 px-4 text-xs text-muted-foreground">
      <ExplainedTag help={simulatorCopy.suggestedHelp}>{AI_SUGGESTED}</ExplainedTag>
      {pick.reason !== null && <span className="break-words">{simulatorCopy.why(pick.reason)}</span>}
    </p>
  )
}

/**
 * The API's result for the picked change, titled from the node's alternatives for a swap. A result without a mass has
 * no figures to show (`massUnknown`).
 */
function ScenarioView({ preview, alternatives }: { preview: ScenarioPreview; alternatives: readonly AlternativeView[] }) {
  const { change, result } = preview
  return (
    <div data-testid="sim-result" data-badge={result.massUnknown ? undefined : result.badge} className="flex flex-col gap-2">
      <div className="flex items-center justify-between gap-2">
        <p className="text-sm font-medium">{scenarioTitle(change, alternatives)}</p>
        {!result.massUnknown && <ScenarioBadge badge={result.badge} />}
      </div>
      {result.massUnknown ? (
        <p data-testid="sim-mass-unknown" className="text-xs text-muted-foreground">
          {scenarioCopy.massUnknown}
        </p>
      ) : (
        <>
          <Figures result={result} />
          <ApplyScenario key={`${change.mode}|${change.alternativeId}`} preview={preview} />
        </>
      )}
    </div>
  )
}

/**
 * "Apply" saves the shown change as a scenario (POST /api/scenarios); the node is never changed. The store marks the
 * preview saved from the API's answer, and ignores an answer that lands after another pick or after the panel closed.
 */
function ApplyScenario({ preview }: { preview: ScenarioPreview }) {
  const markSaved = useGraphStore((s) => s.markScenarioSaved)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const saved = preview.savedId !== undefined

  const onApply = async () => {
    setSaving(true)
    setError(null)
    const answer = await saveScenario(preview.nodeId, preview.change)
    setSaving(false)
    if (answer.ok) markSaved(answer.data)
    else setError(answer.message)
  }

  let label: string = simulatorCopy.apply
  if (saved) label = simulatorCopy.applied
  else if (saving) label = simulatorCopy.applying

  return (
    <div className="flex flex-col gap-1.5 border-t pt-2">
      <div className="flex items-center justify-between gap-3">
        <p data-testid="sim-apply-note" className="text-xs text-muted-foreground">
          {saved ? simulatorCopy.appliedNote : simulatorCopy.applyNote}
        </p>
        <Button
          size="sm"
          variant={saved ? 'outline' : 'default'}
          disabled={saving || saved}
          data-testid="sim-apply"
          data-saved={saved}
          onClick={() => void onApply()}
        >
          {label}
        </Button>
      </div>
      {error !== null && (
        <p role="alert" data-testid="sim-apply-error" className="text-xs text-destructive">
          {error}
        </p>
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
        // From the figures as shown: the node's figure (result minus the API's delta) and the result.
        delta={`${formatTonnesChange(result.carbonKg, result.carbonKg - delta.carbonKg)} ${CO2E_UNIT}`}
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
          <dd data-testid="sim-mac" className="text-right whitespace-nowrap tabular-nums">
            {simulatorCopy.perTonne(formatEuros(mac.macEurPerT), CO2E_UNIT)}
          </dd>
        </div>
      )}
    </dl>
  )
}

// The status hues, a shade darker so small text keeps a 4.5:1 contrast on the card.
const directionTone: Record<DeltaDirection, string> = {
  lower: 'text-verified-ink',
  higher: 'text-estimated-ink',
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

function GroupLabel({ children }: { children: ReactNode }) {
  return <p className="text-xs font-medium text-muted-foreground">{children}</p>
}
