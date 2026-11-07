import { type ReactNode, useId, useState } from 'react'
import type { EnergySource, NodeView, TransportMode } from '@/api/client'
import { Button } from '@/components/ui/button'
import { energySourceCopy, figuresCopy, transportModeCopy } from '@/copy/dashboard'
import { ILLUSTRATIVE } from '@/copy/labels'
import { trustApi } from '@/lib/trust-api'
import { cn } from '@/lib/utils'
import { CO2E_UNIT, formatTonnes } from '@/store'
import { useTrustWrite } from './useTrustWrite'

// The three figures of a node (F7), side by side: the estimate from spend, the supplier's figure as counted (with the
// answered one under it when they differ, the gap being the reductions nothing backs yet), and the best possible state,
// which the buyer can choose. Every number is the API's. Test hooks: `figures`, `figures-estimate`, `figures-claimed`,
// `figures-counted`, `figures-target`, `target-change`, `target-mode`, `target-energy`, `target-alternative`,
// `target-save`, `target-reset`, `target-error`.

export function NodeFigures({ node }: { node: NodeView }) {
  const { figures } = node
  const [editing, setEditing] = useState(false)
  const supplier = figures.countedKg !== null && figures.claimedKg !== null
  const gap = supplier && figures.countedKg! > figures.claimedKg!
  const target = figures.target

  return (
    <div data-testid="figures" className="flex flex-col gap-2">
      <div className="grid grid-cols-3 gap-1.5">
        <Tile label={figuresCopy.estimateShort} testId="figures-estimate" kg={figures.estimateKg} />
        <Tile
          label={figuresCopy.supplierShort}
          testId="figures-counted"
          kg={supplier ? figures.countedKg : null}
          empty={node.status === 'PARTIAL' ? figuresCopy.partialAnswer : figuresCopy.noAnswer}
          tone={gap ? 'warning' : supplier ? 'verified' : undefined}
          note={
            gap ? (
              <span data-testid="figures-claimed" data-kg={figures.claimedKg}>
                {figuresCopy.answeredShort(formatTonnes(figures.claimedKg!))}
              </span>
            ) : undefined
          }
        />
        <Tile
          label={figuresCopy.targetShort}
          testId="figures-target"
          kg={target?.kg ?? null}
          empty={figuresCopy.noMass}
          note={target === null ? undefined : `${transportModeCopy[target.mode]} · ${figuresCopy.energyShort[target.energy]}`}
        />
      </div>
      <p className="flex flex-wrap items-start justify-between gap-x-2 gap-y-0.5 px-0.5 text-[11px] text-muted-foreground">
        <span className="flex flex-col">
          {gap && <span>{figuresCopy.countedNote}</span>}
          <span data-testid="figures-target-note">{figuresCopy.targetNote}</span>
        </span>
        {target !== null && !editing && (
          <button
            type="button"
            data-testid="target-change"
            onClick={() => setEditing(true)}
            className="font-medium text-verified-ink hover:text-verified"
          >
            {figuresCopy.changeTarget}
          </button>
        )}
      </p>
      {editing && <TargetForm node={node} onDone={() => setEditing(false)} />}
    </div>
  )
}

function Tile({
  label,
  testId,
  kg,
  empty,
  note,
  tone,
}: {
  label: string
  testId: string
  kg: number | null
  empty?: string
  note?: ReactNode
  tone?: 'warning' | 'verified'
}) {
  return (
    <div
      className={cn(
        'flex min-w-0 flex-col gap-1 rounded-[14px] px-3 py-2.5',
        tone === 'warning' ? 'bg-estimated-tint text-estimated-ink' : tone === 'verified' ? 'bg-verified-tint' : 'bg-grouped',
      )}
    >
      <span className="truncate text-[11px] font-medium text-ink-2">{label}</span>
      {kg === null ? (
        <span data-testid={testId} className="text-xs leading-snug text-muted-foreground">
          {empty}
        </span>
      ) : (
        <span data-testid={testId} data-kg={kg} className="font-display text-[19px] leading-none font-semibold tracking-[-0.02em]">
          {formatTonnes(kg)} <span className="text-[10px] font-medium tracking-normal text-muted-foreground">{CO2E_UNIT}</span>
        </span>
      )}
      {note !== undefined && <span className="truncate text-[11px] leading-snug">{note}</span>}
    </div>
  )
}

/** The target's choices: a mode the lane has, a power source, or one of the node's alternative suppliers. */
function TargetForm({ node, onDone }: { node: NodeView; onDone: () => void }) {
  const target = node.figures.target
  const [alternativeId, setAlternativeId] = useState<string>(target?.alternativeId ?? '')
  const alternative = node.alternatives.find((a) => a.id === alternativeId)
  // An alternative travels its own lane, whose default mode the target uses.
  const modes: TransportMode[] = alternative === undefined ? node.lane.modes : [alternative.mode]
  const [mode, setMode] = useState<TransportMode>(target?.mode ?? node.lane.mode)
  const [energy, setEnergy] = useState<EnergySource>(target?.energy ?? 'renewable')
  const { phase, busy, run } = useTrustWrite(node)
  const ids = { mode: useId(), energy: useId(), alternative: useId() }
  const shownMode = modes.includes(mode) ? mode : modes[0]
  const field = 'h-9 rounded-lg bg-card px-2 text-sm text-foreground shadow-[0_0_0_0.5px_var(--hairline)]'

  const save = (body: Parameters<typeof trustApi.setTarget>[1]) => void run(() => trustApi.setTarget(node.id, body))

  return (
    <div className="flex flex-col gap-3 rounded-[14px] bg-grouped p-3">
      <div className="grid grid-cols-2 gap-2">
        <label htmlFor={ids.mode} className="flex flex-col gap-1 text-xs text-ink-2">
          {figuresCopy.targetMode}
          <select id={ids.mode} data-testid="target-mode" value={shownMode} onChange={(e) => setMode(e.target.value as TransportMode)} className={field}>
            {modes.map((m) => (
              <option key={m} value={m}>
                {transportModeCopy[m]}
              </option>
            ))}
          </select>
        </label>
        <label htmlFor={ids.energy} className="flex flex-col gap-1 text-xs text-ink-2">
          {figuresCopy.targetEnergy}
          <select id={ids.energy} data-testid="target-energy" value={energy} onChange={(e) => setEnergy(e.target.value as EnergySource)} className={field}>
            {(['renewable', 'grid'] as const).map((e) => (
              <option key={e} value={e}>
                {energySourceCopy[e]}
              </option>
            ))}
          </select>
        </label>
      </div>
      {node.alternatives.length > 0 && (
        <label htmlFor={ids.alternative} className="flex flex-col gap-1 text-xs text-ink-2">
          {figuresCopy.targetAlternative}
          <select id={ids.alternative} data-testid="target-alternative" value={alternativeId} onChange={(e) => setAlternativeId(e.target.value)} className={field}>
            <option value="">{figuresCopy.targetSameSupplier}</option>
            {node.alternatives.map((a) => (
              <option key={a.id} value={a.id}>
                {a.label}
              </option>
            ))}
          </select>
        </label>
      )}
      <div className="flex flex-wrap items-center gap-2">
        <Button
          size="sm"
          data-testid="target-save"
          disabled={busy}
          onClick={() => save({ mode: shownMode, energy, alternativeId: alternativeId === '' ? null : alternativeId })}
        >
          {figuresCopy.targetSave}
        </Button>
        <Button size="sm" variant="outline" data-testid="target-reset" disabled={busy} onClick={() => save({ mode: null, energy: null, alternativeId: null })}>
          {figuresCopy.targetReset}
        </Button>
        <Button size="sm" variant="ghost" disabled={busy} onClick={onDone}>
          {figuresCopy.cancel}
        </Button>
        <span className="text-[11px] text-muted-foreground">{ILLUSTRATIVE}</span>
      </div>
      <p role="status" className="text-xs text-ink-2 empty:hidden">
        {busy ? figuresCopy.saving : phase.kind === 'saved' ? figuresCopy.saved : ''}
      </p>
      {phase.kind === 'failed' && (
        <p role="alert" data-testid="target-error" className="text-xs text-destructive">
          {phase.message}
        </p>
      )}
    </div>
  )
}
