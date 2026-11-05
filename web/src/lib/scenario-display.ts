import type { AlternativeView, LaneView, ScenarioChange, TransportMode } from '@/api/client'
import { scenarioCopy, transportModeCopy } from '@/copy/dashboard'

// What a simulated change shows, decided from the change, the node's alternatives and the API's result alone. Nothing
// here computes carbon, cost or lead time: the deltas, the MAC and its badge all come from the API.

/**
 * The change in words: "Switch to Sea" for a mode change, "Switch to Cluj Metal Works SRL" for a swap to one of the
 * node's `alternatives`, and "Switch supplier" for a swap to one it no longer lists.
 */
export function scenarioTitle(change: ScenarioChange, alternatives: readonly AlternativeView[] = []): string {
  if (change.mode !== null) return scenarioCopy.modeChange(transportModeCopy[change.mode])
  const alternative = alternatives.find((a) => a.id === change.alternativeId)
  return alternative === undefined ? scenarioCopy.supplierSwapUnnamed : scenarioCopy.supplierSwap(alternative.label)
}

/** Whether two changes ask for the same thing: the simulator presses the button of the change it shows. */
export function sameChange(a: ScenarioChange | null, b: ScenarioChange | null): boolean {
  return a !== null && b !== null && a.mode === b.mode && a.alternativeId === b.alternativeId
}

/** The simulator's mode buttons, in this order. */
export const SIMULATOR_MODES: readonly TransportMode[] = ['air', 'sea', 'rail', 'road']

export interface ModeOption {
  mode: TransportMode
  /** The lane has a leg (distance, lead time and cost) for this mode. */
  available: boolean
  /** The mode the node ships by now: simulating it would change nothing. */
  current: boolean
}

/** Every mode, each marked by whether the node's lane has a leg for it and whether it is the node's mode now. */
export function modeOptions(lane: LaneView): ModeOption[] {
  return SIMULATOR_MODES.map((mode) => ({ mode, available: lane.modes.includes(mode), current: mode === lane.mode }))
}

export type DeltaDirection = 'lower' | 'higher' | 'same'

/**
 * Which way a delta from the API goes, as shown: it is rounded to the display step (0.1 t = 100 kg, €1, 1 day) the way
 * the formatters round, so the colour never contradicts a "0.0" on screen. Lower is better for all three.
 */
export function deltaDirection(delta: number, step: number): DeltaDirection {
  const steps = Math.round(delta / step)
  if (!Number.isFinite(steps) || steps === 0) return 'same'
  return steps < 0 ? 'lower' : 'higher'
}

/** Display steps of the three deltas, in the API's units. */
export const DELTA_STEP = { carbonKg: 100, costEur: 1, leadDays: 1 } as const
