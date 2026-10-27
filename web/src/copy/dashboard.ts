import type { NodeStatus, ProblemCode } from '@/api/client'

// Dashboard text a juror can read. Components import it from here instead of writing strings inline. The labels that
// rulings 2–4 and CLAUDE.md fix word for word (the supplier-confirmed share, the AI labels) live in ./labels.ts.

export const dashboardTitle = 'TraceGreen'

export const topBarCopy = {
  title: (period: string | null) => (period === null ? 'Supply chain' : `Supply chain · ${period}`),
  subtitle: (buyer: string, suppliers: number, invoices: number) =>
    `${buyer} · ${suppliers} ${suppliers === 1 ? 'supplier' : 'suppliers'} from ${invoices} e-Factura ${invoices === 1 ? 'invoice' : 'invoices'}`,
} as const

export const sidebarCopy = {
  region: 'Sidebar',
  buyerLine: (city: string) => `Buyer · ${city}`,
  suppliers: 'Suppliers',
  noSuppliers: 'No suppliers yet.',
} as const

export const kpiCopy = {
  region: 'Key figures',
  total: 'Total footprint',
  totalSub: (verified: number, suppliers: number) => `${verified} of ${suppliers} suppliers verified`,
  spend: 'Spend',
  spendSub: (invoices: number) => `across ${invoices} ${invoices === 1 ? 'invoice' : 'invoices'}`,
  status: 'Status',
  hotspot: 'Hotspot',
  shareInfo: 'About the supplier-confirmed share',
  unavailable: 'Figures unavailable until the API answers.',
} as const

export const nodeCopy = {
  buyer: 'Buyer',
  factorInfo: 'Factor sources',
  factorsHeading: 'Factors behind this number',
  noFactors: 'No factor rows for this figure.',
  placeholder: 'Placeholder factor',
  tonnes: (t: string) => `${t} t`,
  hotspot: 'largest emitter',
  status: {
    ESTIMATED: 'Estimated',
    PARTIAL: 'Partly confirmed',
    VERIFIED: 'Supplier-confirmed',
  } satisfies Record<NodeStatus, string>,
} as const

export const canvasCopy = {
  label: 'Supplier graph',
  legend: 'Line width follows tCO₂e (log scale)',
  loading: 'Loading the supplier graph…',
  offline: "Can't reach the API. Trying again every 5 seconds.",
  empty: 'No suppliers yet. Import a UBL invoice or load the demo invoice.',
} as const

export const toolbarCopy = {
  import: 'Import invoice',
  importing: 'Importing…',
  fileInput: 'UBL invoice (XML file)',
  demo: 'Load demo invoice',
  demoLoading: 'Loading…',
  reset: 'Reset demo',
  resetting: 'Resetting…',
  // A label can end in a period already ("Shenzhen Diecast Co.").
  imported: (label: string) => `Imported ${label}${label.endsWith('.') ? '' : '.'}`,
  resetDone: 'Demo reset to its start state.',
  resetUnavailable: 'Demo tools are off on this server.',
  unreachable: "Can't reach the API. Check that it is running.",
  httpError: (status: number) => `The API answered HTTP ${status}. Try again.`,
} as const

/**
 * Headline per error code; the API's `detail` follows it. Keyed by every generated code, so a new code in the
 * contract fails the typecheck until it has a headline.
 */
export const problemHeadlines: Record<ProblemCode, string> = {
  TooLarge: 'File too large.',
  DoctypeForbidden: 'Refused for safety.',
  NotUblInvoice: 'Not a UBL 2.1 invoice.',
  Schema: 'The invoice could not be read.',
  Duplicate: 'Already on the graph.',
  MissingFile: 'No invoice received.',
  UnsupportedMediaType: 'Upload an XML file.',
  UnsupportedCurrency: 'Currency not supported.',
  NoLane: 'Supplier country not covered.',
  NodeNotFound: 'Supplier not on the graph.',
  InvalidToken: 'Link not valid.',
  LinkExpired: 'Link expired.',
  InvoiceChanged: 'The invoice has changed.',
  AlreadyUsed: 'Link already used.',
  InvalidAnswer: 'Answer not accepted.',
  RateLimited: 'Too many attempts. Wait a minute.',
}
