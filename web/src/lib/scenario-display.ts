import type { ScenarioChange } from '@/api/client'
import { scenarioCopy, transportModeCopy } from '@/copy/dashboard'

// What a simulated change shows, decided from the change and the API's result alone. Nothing here computes carbon,
// cost or lead time: the deltas, the MAC and its badge all come from the API.

/** The change in words: "Switch to Sea" for a mode change, "Switch supplier" for a swap. */
export function scenarioTitle(change: ScenarioChange): string {
  return change.mode !== null ? scenarioCopy.modeChange(transportModeCopy[change.mode]) : scenarioCopy.supplierSwap
}
