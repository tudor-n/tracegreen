import { describe, expect, it } from 'vitest'
import type { DraftInvoice } from '@/api/client'
import { addLine, flagged, initialForm, isPdf, linePath, removeLine, setField, setLineField } from './pdf-form'

const draft: DraftInvoice = {
  supplierName: 'Zincora Turnatorie SRL',
  supplierTaxId: 'RO99999968',
  supplierCountry: 'RO',
  invoiceNumber: 'ZT-2026-PDF1',
  issueDate: '2026-10-22',
  currency: 'EUR',
  totalNet: '12000.00',
  lines: [
    { description: 'Housings', quantity: '1200', unitCode: 'H87', amount: '9840.00', classificationCode: '790700', classificationScheme: 'HS' },
    { description: 'Covers', quantity: '1200', unitCode: 'BOX', amount: '2160.00', classificationCode: '', classificationScheme: 'UNSPSC' },
  ],
}

describe('pdf form', () => {
  it('tells a PDF by its type or its name', () => {
    expect(isPdf({ name: 'a.PDF', type: '' })).toBe(true)
    expect(isPdf({ name: 'scan', type: 'application/pdf' })).toBe(true)
    expect(isPdf({ name: 'invoice.xml', type: 'text/xml' })).toBe(false)
  })

  it('starts as the draft, leaving a unit or scheme outside the lists for the person to pick', () => {
    const form = initialForm(draft)
    expect(form.lines[0].unitCode).toBe('H87')
    expect(form.lines[1].unitCode).toBe('')
    expect(form.lines[1].classificationScheme).toBe('')
  })

  it('edits fields and lines without touching the draft', () => {
    let form = setField(initialForm(draft), 'totalNet', '12.000,00')
    form = setLineField(form, 0, 'quantity', '1.200')
    form = removeLine(addLine(form), 2)
    expect(form.totalNet).toBe('12.000,00')
    expect(form.lines[0].quantity).toBe('1.200')
    expect(form.lines).toHaveLength(2)
    expect(draft.totalNet).toBe('12000.00')
    expect(addLine(form).lines[2]).toEqual({
      description: '',
      quantity: '',
      unitCode: '',
      amount: '',
      classificationCode: '',
      classificationScheme: '',
    })
  })

  it('marks a field the PDF text does not show until the person changes it', () => {
    const found = { invoiceNumber: false, totalNet: true, [linePath(0, 'amount')]: false }
    expect(flagged(found, 'invoiceNumber', 'ZT-9', 'ZT-9')).toBe(true)
    expect(flagged(found, 'invoiceNumber', 'ZT-9', 'ZT-2026-PDF1')).toBe(false)
    expect(flagged(found, 'totalNet', '12000.00', '12000.00')).toBe(false)
    expect(flagged(found, 'lines.0.amount', '9840.00', '9840.00')).toBe(true)
    expect(flagged(found, 'lines.0.unitCode', 'H87', 'H87')).toBe(false)
  })
})
