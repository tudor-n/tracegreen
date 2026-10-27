import { describe, expect, it } from 'vitest'
import { node } from '@/store/testFixtures'
import { DELTA_STEP, deltaDirection, modeOptions, scenarioTitle } from './scenario-display'

describe('scenarioTitle', () => {
  it('names a mode change by the mode, and a swap generically', () => {
    expect(scenarioTitle({ mode: 'sea', alternativeId: null })).toBe('Switch to Sea')
    expect(scenarioTitle({ mode: 'rail', alternativeId: null })).toBe('Switch to Rail')
    expect(scenarioTitle({ mode: null, alternativeId: 'N1-alt' })).toBe('Switch supplier')
  })
})

describe('modeOptions', () => {
  it('lists Air, Sea, Rail and Road, marking the legs the lane lacks and the mode it uses now', () => {
    const { lane } = node('n1', 22_562, {
      lane: { ...node('x', 0).lane, mode: 'air', modeDefault: 'sea', modes: ['rail', 'sea', 'air'] },
    })
    expect(modeOptions(lane)).toEqual([
      { mode: 'air', available: true, current: true },
      { mode: 'sea', available: true, current: false },
      { mode: 'rail', available: true, current: false },
      { mode: 'road', available: false, current: false },
    ])
  })

  it('leaves only the current mode on a lane with one leg', () => {
    const options = modeOptions(node('n4', 2_900).lane)
    expect(options.filter((o) => o.available && !o.current)).toEqual([])
  })
})

describe('deltaDirection', () => {
  it('reads lower, higher or the same as the rounded figure on screen does', () => {
    expect(deltaDirection(-15_074, DELTA_STEP.carbonKg)).toBe('lower')
    expect(deltaDirection(400, DELTA_STEP.carbonKg)).toBe('higher')
    expect(deltaDirection(-20, DELTA_STEP.carbonKg)).toBe('same')
    expect(deltaDirection(-50, DELTA_STEP.carbonKg)).toBe('same')
    expect(deltaDirection(50, DELTA_STEP.carbonKg)).toBe('higher')
    expect(deltaDirection(33, DELTA_STEP.leadDays)).toBe('higher')
    expect(deltaDirection(-0.4, DELTA_STEP.costEur)).toBe('same')
    expect(deltaDirection(Number.NaN, DELTA_STEP.costEur)).toBe('same')
  })
})
