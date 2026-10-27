import { describe, expect, it } from 'vitest'
import { scenarioTitle } from './scenario-display'

describe('scenarioTitle', () => {
  it('names a mode change by the mode, and a swap generically', () => {
    expect(scenarioTitle({ mode: 'sea', alternativeId: null })).toBe('Switch to Sea')
    expect(scenarioTitle({ mode: 'rail', alternativeId: null })).toBe('Switch to Rail')
    expect(scenarioTitle({ mode: null, alternativeId: 'N1-alt' })).toBe('Switch supplier')
  })
})
