// Writes seed/invoices/P1-bistrita.pdf: a fictional one-page PDF invoice for the PDF import (F9), with a text layer in
// Helvetica. Its seller is R1's (Zincora Turnatorie SRL, a made-up name with a CUI not registered at ANAF, see
// docs/NEXT.md); the number, the date and the amounts are invented like every seed invoice's. No dependencies.
// Run: node scripts/make-sample-pdf.mjs
import { writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

export const LINES = [
  'FACTURA / INVOICE   ZT-2026-PDF1',
  'Seller: Zincora Turnatorie SRL   CUI RO99999968   Str. Fabricii 12, Bistrita, RO',
  'Buyer: Transilvania Electro-Assembly SRL   CUI RO99999992   Sibiu, RO',
  'Issue date: 22.10.2026   Currency: EUR',
  '',
  'No.  Description                              HS code   Qty        Net amount',
  '1    Zinc die-cast sensor housings, type ZH-220   790700    1.200 pcs  9.840,00',
  '2    Zamak covers for sensor housings            790700    1.200 pcs  2.160,00',
  '',
  'Total without VAT: 12.000,00 EUR',
  'VAT 21%: 2.520,00 EUR',
  'Total: 14.520,00 EUR',
]

const escape = (text) => text.replaceAll('\\', '\\\\').replaceAll('(', '\\(').replaceAll(')', '\\)')

export function makePdf(lines) {
  const shown = lines.map((line) => `(${escape(line)}) Tj T*`).join(' ')
  const content = Buffer.from(`BT /F1 9 Tf 40 800 Td 14 TL ${shown} ET`, 'latin1')
  const objects = [
    Buffer.from('<< /Type /Catalog /Pages 2 0 R >>'),
    Buffer.from('<< /Type /Pages /Kids [3 0 R] /Count 1 >>'),
    Buffer.from('<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>'),
    Buffer.from('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>'),
    Buffer.concat([Buffer.from(`<< /Length ${content.length} >>\nstream\n`), content, Buffer.from('\nendstream')]),
  ]
  const parts = [Buffer.from('%PDF-1.4\n')]
  let size = parts[0].length
  const offsets = []
  objects.forEach((body, i) => {
    offsets.push(size)
    const chunk = Buffer.concat([Buffer.from(`${i + 1} 0 obj\n`), body, Buffer.from('\nendobj\n')])
    parts.push(chunk)
    size += chunk.length
  })
  const xref = [`xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`, ...offsets.map((o) => `${String(o).padStart(10, '0')} 00000 n \n`)].join('')
  parts.push(Buffer.from(`${xref}trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${size}\n%%EOF\n`))
  return Buffer.concat(parts)
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const target = fileURLToPath(new URL('../seed/invoices/P1-bistrita.pdf', import.meta.url))
  writeFileSync(target, makePdf(LINES))
  console.log(`wrote ${target}`)
}
