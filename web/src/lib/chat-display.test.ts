import { describe, expect, it } from 'vitest'
import type { ChatFactView } from '@/api/client'
import { factLabel, factLink, factValue } from './chat-display'

const fact = (overrides: Partial<ChatFactView>): ChatFactView => ({
  id: 'S1.carbon',
  field: 'carbon',
  kind: 'carbonKg',
  value: '4884.4555',
  subject: 'Oradea Harness SRL',
  nodeId: '11111111-1111-4111-8111-000000000003',
  auditId: null,
  ...overrides,
})

describe('factValue', () => {
  it('formats every kind the way the rest of the app does', () => {
    expect(factValue(fact({}))).toBe('4.88 tCO₂e')
    expect(factValue(fact({ kind: 'eur', field: 'spend', value: '22000.4' }))).toBe('€22,000')
    expect(factValue(fact({ kind: 'days', field: 'leadTime', value: '1' }))).toBe('1 day')
    expect(factValue(fact({ kind: 'share', field: 'confirmedShare', value: '0.3256' }))).toBe('33%')
    expect(factValue(fact({ kind: 'count', field: 'deliveries', value: '4' }))).toBe('4')
    expect(factValue(fact({ kind: 'date', field: 'issued', value: '2026-10-05' }))).toBe('5 Oct 2026')
    expect(factValue(fact({ kind: 'status', field: 'status', value: 'VERIFIED' }))).toBe('Supplier-confirmed')
    expect(factValue(fact({ kind: 'mode', field: 'mode', value: 'sea' }))).toBe('Sea')
    expect(factValue(fact({ kind: 'text', field: 'name', value: 'Oradea Harness SRL' }))).toBe('Oradea Harness SRL')
  })

  it('signs a scenario change', () => {
    expect(factValue(fact({ field: 'carbonDelta', value: '-15074' }))).toBe('-15.07 t')
    expect(factValue(fact({ kind: 'eur', field: 'costDelta', value: '8350' }))).toBe('+€8,350')
  })

  it('names an audit action and an empty list in words', () => {
    expect(factValue(fact({ kind: 'text', field: 'action', value: 'invoice.imported' }))).toBe('Invoice imported')
    expect(factValue(fact({ kind: 'text', field: 'listEstimated', value: 'none' }))).toBe('none')
  })
})

describe('factLabel', () => {
  it('says what the value is and whose', () => {
    expect(factLabel(fact({}))).toBe('Emissions · Oradea Harness SRL')
    expect(factLabel(fact({ field: 'totalCarbon', subject: null, nodeId: null }))).toBe('Total emissions')
  })

  it("does not repeat a supplier's name after its name", () => {
    expect(factLabel(fact({ field: 'name', kind: 'text' }))).toBe('Supplier')
  })

  it('names a field it does not know plainly', () => {
    expect(factLabel(fact({ field: 'somethingNew', subject: null }))).toBe('Value')
  })
})

describe('factLink', () => {
  it('links a supplier fact to its supplier, an audit fact to the log, and a total to nothing', () => {
    expect(factLink(fact({}))).toEqual({
      kind: 'node',
      nodeId: '11111111-1111-4111-8111-000000000003',
      label: 'Open Oradea Harness SRL',
    })
    expect(factLink(fact({ nodeId: null, subject: null, auditId: 7 }))?.kind).toBe('audit')
    expect(factLink(fact({ nodeId: null, subject: null }))).toBeNull()
  })
})
