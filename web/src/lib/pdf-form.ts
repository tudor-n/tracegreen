import type { DraftInvoice, DraftLine } from '@/api/client'

// The PDF review form's rules (F9), pure: the form is the draft as the person edits it. The server validates and
// compares it with the draft it holds; nothing here decides what is imported.

export const UNIT_CODES = ['KGM', 'TNE', 'GRM', 'H87', 'C62', 'MTR', 'LTR', 'SET'] as const
export const SCHEMES = ['', 'CPV', 'HS'] as const

export type InvoiceField = Exclude<keyof DraftInvoice, 'lines'>
export type LineField = keyof DraftLine

export const INVOICE_FIELDS: readonly InvoiceField[] = [
  'supplierName',
  'supplierTaxId',
  'supplierCountry',
  'invoiceNumber',
  'issueDate',
  'currency',
  'totalNet',
]

export const SUPPLIER_FIELDS: readonly InvoiceField[] = ['supplierName', 'supplierTaxId', 'supplierCountry']
export const INVOICE_DETAILS: readonly InvoiceField[] = ['invoiceNumber', 'issueDate', 'currency', 'totalNet']

export function isPdf(file: Pick<File, 'name' | 'type'>): boolean {
  return file.type === 'application/pdf' || file.name.toLowerCase().endsWith('.pdf')
}

/** The form starts as the draft, with any unit or scheme outside the lists left empty for the person to pick. */
export function initialForm(draft: DraftInvoice): DraftInvoice {
  return {
    ...draft,
    lines: draft.lines.map((line) => ({
      ...line,
      unitCode: (UNIT_CODES as readonly string[]).includes(line.unitCode) ? line.unitCode : '',
      classificationScheme: (SCHEMES as readonly string[]).includes(line.classificationScheme) ? line.classificationScheme : '',
    })),
  }
}

export function setField(form: DraftInvoice, field: InvoiceField, value: string): DraftInvoice {
  return { ...form, [field]: value }
}

export function setLineField(form: DraftInvoice, index: number, field: LineField, value: string): DraftInvoice {
  return { ...form, lines: form.lines.map((line, i) => (i === index ? { ...line, [field]: value } : line)) }
}

export function removeLine(form: DraftInvoice, index: number): DraftInvoice {
  return { ...form, lines: form.lines.filter((_, i) => i !== index) }
}

export function addLine(form: DraftInvoice): DraftInvoice {
  const empty: DraftLine = { description: '', quantity: '', unitCode: '', amount: '', classificationCode: '', classificationScheme: '' }
  return { ...form, lines: [...form.lines, empty] }
}

/** The path the API marks a field by: "invoiceNumber", "lines.0.amount". */
export function linePath(index: number, field: LineField): string {
  return `lines.${index}.${field}`
}

/**
 * Whether to mark a field "not in the PDF": the API looked and did not find the value the AI copied, and the person has
 * not changed it since. A field the API did not check (a unit, a scheme, an empty one) is never marked.
 */
export function flagged(found: Readonly<Record<string, boolean>>, path: string, draftValue: string, value: string): boolean {
  return found[path] === false && draftValue.trim() === value.trim()
}
