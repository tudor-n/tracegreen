import type { EnergyCertType, ShippingDocType, TransportMode } from '@/api/client'
import { docIdProblem, isoCheckDigit } from './evidence'

// Fictional proof for the demo (F7): one sample per document and certificate type, each in a shape the API accepts and
// with a correct check digit where the document has one, so the presenter never types a code on stage. "TG" and "DEMO"
// mark them as TraceGreen's own; none names a real company. The pages offer them only while the API runs with demo tools
// (`/api/health` `demoTools`), so a real supplier never gets a button that fills in proof.

const CONTAINER_BODY = 'TGNU100100'

/** A container number with its ISO 6346 check digit. */
export const DEMO_CONTAINER = `${CONTAINER_BODY}${isoCheckDigit(CONTAINER_BODY)}`

export const DEMO_DOC_IDS: Record<ShippingDocType, string> = {
  containerNumber: DEMO_CONTAINER,
  // Serial 1234567, whose remainder mod 7 is 5; the airline prefix 000 names no carrier.
  airWaybill: '00012345675',
  billOfLading: 'TGBL-260001',
  cmr: 'CMR-2026-001234',
  eTransportUit: 'UIT-DEMO-2026-0001',
  railConsignment: 'CIM-2026-0042',
}

export const DEMO_CERT_IDS: Record<EnergyCertType, string> = {
  guaranteeOfOrigin: 'GO-RO-2026-DEMO01',
  iRec: 'IREC-2026-DEMO01',
  chinaGec: 'GEC-2026-DEMO01',
  taiwanTRec: 'TREC-2026-DEMO01',
  ppa: 'PPA-2026-DEMO01',
  onSite: 'ONSITE-2026-DEMO01',
}

/** An MRN in the 18-character shape the API checks: year, country, then 14 letters and digits. */
export function demoMrn(year: number, country = 'RO'): string {
  return `${String(year % 100).padStart(2, '0')}${country}DEMO0000000001`
}

/** The sample number for a document type; every one passes `docIdProblem`, which the tests pin. */
export function demoDocId(type: ShippingDocType): string {
  return DEMO_DOC_IDS[type]
}

/** The ISO date `days` after `isoDate` (both `YYYY-MM-DD`, in UTC). */
export function addDays(isoDate: string, days: number): string {
  const date = new Date(`${isoDate}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + days)
  return date.toISOString().slice(0, 10)
}

/**
 * The buyer's demo records for a delivery: received the claimed mode's lead time after dispatch (or after the invoice
 * date, when the supplier gave no dispatch date), never after `today` (the API refuses a receipt in the future), with
 * that mode at the border and a demo MRN, so the transport answer matches them. Choosing another border mode afterwards
 * shows a contradiction.
 */
export function demoRecords(
  mode: TransportMode,
  leadDays: number,
  dispatchDate: string | null,
  issueDate: string,
  today: string,
): { receivedOn: string; customsBorderMode: TransportMode; customsMrn: string } {
  const start = dispatchDate ?? issueDate
  const received = addDays(start, leadDays)
  return {
    receivedOn: received > today ? today : received,
    customsBorderMode: mode,
    customsMrn: demoMrn(Number(start.slice(0, 4))),
  }
}

/** For the tests: whether every sample passes the page's own check. */
export function demoSamplesValid(): boolean {
  return (Object.keys(DEMO_DOC_IDS) as ShippingDocType[]).every((type) => docIdProblem(type, DEMO_DOC_IDS[type]) === null)
}
