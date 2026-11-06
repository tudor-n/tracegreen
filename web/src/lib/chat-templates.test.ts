import { describe, expect, it } from 'vitest'
import { kpis, node } from '@/store/testFixtures'
import { templateAnswer } from './chat-templates'

const nodes = [
  node('a', 2000, { label: 'Sibiu Pack SRL', status: 'ESTIMATED' }),
  node('b', 4900, { label: 'Oradea Harness SRL', status: 'ESTIMATED' }),
  node('c', 4700, { label: 'Taiwan PCB Ltd.', status: 'VERIFIED' }),
]
const totals = { ...kpis(11600, 3), primaryShare: 0.405 }

/** The sentence with each fact as its raw value in brackets. */
const read = (answer: ReturnType<typeof templateAnswer>) => {
  const byId = new Map(answer.facts.map((f) => [f.id, f.value]))
  return answer.segments.map((s) => s.text ?? `[${byId.get(s.factId ?? '')}]`).join('')
}

describe('templateAnswer', () => {
  it('names the largest emitter by the app emissions order, with its figure and status as facts', () => {
    const answer = templateAnswer('top', nodes, totals)
    expect(read(answer)).toBe('The largest emitter is [Oradea Harness SRL] with [4900], [ESTIMATED].')
    expect(answer.facts.map((f) => f.nodeId)).toEqual(['b', 'b', 'b'])
    expect(answer.provider).toBe('template')
  })

  it('gives the totals and the share from the API KPIs', () => {
    expect(read(templateAnswer('total', nodes, totals))).toBe('Total emissions are [11600] across [3] deliveries.')
    expect(read(templateAnswer('confirmed', nodes, totals))).toBe(
      'The supplier-confirmed share is [0.405] of total emissions, by emissions, not by node count.',
    )
  })

  it('lists the estimated suppliers largest first, or none', () => {
    expect(read(templateAnswer('estimated', nodes, totals))).toBe('Still estimated: [Oradea Harness SRL, Sibiu Pack SRL].')
    expect(read(templateAnswer('estimated', [nodes[2]], totals))).toBe('Still estimated: [none].')
  })

  it('says so when there is no data yet', () => {
    expect(read(templateAnswer('top', [], null))).toBe('There are no deliveries yet.')
  })
})
