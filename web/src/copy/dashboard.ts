import type { EnergySource, NodeStatus, ProblemCode, TransportMode } from '@/api/client'
import type { Connection } from '@/hooks/graphSync'

// Dashboard text a juror can read. Components import it from here instead of writing strings inline. The labels that
// rulings 2–4 and CLAUDE.md fix word for word (the supplier-confirmed share, the AI labels) live in ./labels.ts.

export const dashboardTitle = 'TraceGreen'

export const kpiCopy = {
  region: 'Key figures',
  total: 'Total emissions',
  spend: 'Spend',
  nodes: 'Suppliers',
  shareInfo: 'About the supplier-confirmed share',
  unavailable: 'Figures unavailable until the API answers.',
} as const

export const nodeCopy = {
  buyer: 'Buyer',
  factorInfo: 'Factor sources',
  factorsHeading: 'Factors behind this number',
  noFactors: 'No factor rows for this figure.',
  placeholder: 'Placeholder factor',
  hotspot: 'largest emitter',
  status: {
    ESTIMATED: 'Estimated',
    PARTIAL: 'Partly confirmed',
    VERIFIED: 'Supplier-confirmed',
  } satisfies Record<NodeStatus, string>,
} as const

export const canvasCopy = {
  label: 'Supplier graph',
  loading: 'Loading the supplier graph…',
  offline: "Can't reach the API. Trying again every 5 seconds.",
  empty: 'No suppliers yet. Import a UBL invoice or load the demo invoice.',
} as const

export const transportModeCopy = {
  road: 'Road',
  rail: 'Rail',
  sea: 'Sea',
  air: 'Air',
} satisfies Record<TransportMode, string>

export const energySourceCopy = {
  grid: 'Grid electricity',
  renewable: 'Renewable electricity',
  unknown: 'Not known',
} satisfies Record<EnergySource, string>

/** The node drawer. Lane cost and lead time always carry ILLUSTRATIVE from ./labels.ts (CLAUDE.md). */
export const drawerCopy = {
  emissions: 'Emissions',
  spendBefore: 'Spend-based estimate before confirmation',
  answers: 'Supplier answers',
  transport: 'Transport',
  energy: 'Energy',
  skipped: 'Skipped',
  classification: 'Classification',
  invoiceLine: 'Invoice line',
  invoice: 'Invoice',
  invoiceNumber: 'Number',
  issued: 'Issued',
  spend: 'Spend',
  lane: 'Lane',
  route: 'Route',
  mode: 'Mode',
  distance: 'Distance',
  cost: 'Cost',
  leadTime: 'Lead time',
  requestData: 'Request data',
  requesting: 'Requesting…',
  simulate: 'Simulate',
} as const

/**
 * The QR card in the node drawer. Ruling 6: a link opens only on a phone on the laptop's hotspot, so the card always
 * says so; `loopback` means the API built the link from a localhost address, which no phone can reach.
 */
export const linkCopy = {
  title: 'Verification link',
  qr: 'QR code of the verification link',
  scan: "Scan it with the supplier's phone. The link works once.",
  expiresIn: (left: string) => `expires in ${left}`,
  expired: 'Expired. Request a new link.',
  hotspot: "Only a phone on this laptop's hotspot can open this link.",
  loopback:
    "This link points to this computer (localhost), so no phone can open it. Set PUBLIC_BASE_URL to the laptop's hotspot address and request a new link.",
} as const

/**
 * The audit panel around the words of ./labels.ts (its title and the chain results). It is a tamper-evident audit log
 * and is never called anything else (PRIORITY-CHANGES.md §4). Actions and actors the API adds later show as their raw
 * names until they get a label here.
 */
export const auditPanelCopy = {
  open: 'Audit log',
  description:
    'Each entry is hashed together with the hash of the entry before it, so changing any entry breaks the chain from there on.',
  loading: 'Loading the audit log…',
  offline: "Can't reach the API. Trying again every 5 seconds.",
  empty: 'No entries yet.',
  hash: 'hash',
  verify: 'Verify chain',
  verifying: 'Verifying…',
  checked: (entries: number) => `${entries} ${entries === 1 ? 'entry' : 'entries'} checked`,
  actions: {
    'seed.loaded': 'Demo data loaded',
    'invoice.imported': 'Invoice imported',
    'link.requested': 'Verification link requested',
    'verification.submitted': 'Supplier answered',
  } as Readonly<Partial<Record<string, string>>>,
  actors: {
    seed: 'Seed data',
    buyer: 'Buyer',
    demo: 'Demo',
    supplier: 'Supplier',
  } as Readonly<Partial<Record<string, string>>>,
} as const

/** The live-updates badge: one label per state of useGraphEvents, and what it means as the tooltip. */
export const connectionCopy = {
  state: {
    connecting: 'Connecting…',
    live: 'Live',
    polling: 'Polling',
    offline: 'Offline',
  } satisfies Record<Connection, string>,
  detail: {
    connecting: 'Loading the supplier graph.',
    live: 'Changes appear as they happen.',
    polling: 'Live updates are interrupted. Checking for changes every 5 seconds.',
    offline: "Can't reach the API. Trying again every 5 seconds.",
  } satisfies Record<Connection, string>,
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
