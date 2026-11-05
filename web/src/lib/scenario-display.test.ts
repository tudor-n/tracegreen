import { describe, expect, it } from 'vitest'
import { node } from '@/store/testFixtures'
import { DELTA_STEP, deltaDirection, modeOptions, sameChange, scenarioTitle } from './scenario-display'

const n1Alt = { id: 'N1-alt', label: 'Cluj Metal Works SRL', country: 'RO', origin: 'RO-CJ', mode: 'road', illustrative: true } as const

describe('scenarioTitle', () => {
  it('names a mode change by the mode', () => {
    expect(scenarioTitle({ mode: 'sea', alternativeId: null })).toBe('Switch to Sea')
    expect(scenarioTitle({ mode: 'rail', alternativeId: null }, [n1Alt])).toBe('Switch to Rail')
  })

  it("names a swap by the alternative's label, and generically when the node no longer lists it", () => {
    expect(scenarioTitle({ mode: null, alternativeId: 'N1-alt' }, [n1Alt])).toBe('Switch to Cluj Metal Works SRL')
    expect(scenarioTitle({ mode: null, alternativeId: 'N1-alt' })).toBe('Switch supplier')
    expect(scenarioTitle({ mode: null, alternativeId: 'N2-alt' }, [n1Alt])).toBe('Switch supplier')
  })
})

describe('sameChange', () => {
  it('matches the same mode or the same alternative, and nothing against no change', () => {
    expect(sameChange({ mode: 'sea', alternativeId: null }, { mode: 'sea', alternativeId: null })).toBe(true)
    expect(sameChange({ mode: null, alternativeId: 'N1-alt' }, { mode: null, alternativeId: 'N1-alt' })).toBe(true)
    expect(sameChange({ mode: 'sea', alternativeId: null }, { mode: 'air', alternativeId: null })).toBe(false)
    expect(sameChange({ mode: null, alternativeId: 'N1-alt' }, { mode: 'sea', alternativeId: null })).toBe(false)
    expect(sameChange(null, { mode: 'sea', alternativeId: null })).toBe(false)
    expect(sameChange(null, null)).toBe(false)
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
