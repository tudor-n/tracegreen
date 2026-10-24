import type { CarbonBasis, EnergySource, NodeStatus, ProblemCode, TransportMode, Uncertainty } from '@/api/client'
import type { Connection } from '@/hooks/graphSync'

// Dashboard text a juror can read. Components import it from here instead of writing strings inline.
//
// `supplierConfirmedShare` and `AI_MAPPED` stand in for web/src/copy/labels.ts (C3) until it lands; then import them
// from there and delete them here (docs/plan/README.md §3). The share tooltip below is a factual placeholder: it must
// become ruling 2's tooltip, word for word.

export const dashboardTitle = 'TraceGreen'

/** Ruling 2. `primaryShare` on the wire. */
export const supplierConfirmedShare = {
  label: 'Supplier-confirmed share',
  subtitle: 'by emissions, not by node count',
  tooltip:
    'Share of total tCO₂e from suppliers who confirmed both questions (transport and energy). Weighted by emissions, not by node count.',
} as const

/** CLAUDE.md: the label on every value an AI classification produced (cache or model). */
export const AI_MAPPED = 'AI-mapped'

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

/**
 * Ruling 3: which figure a node's number is. The provenance line adds the factor source (and, once supplier-confirmed,
 * the year). Stands in for web/src/copy/labels.ts (C3) until it lands.
 */
export const provenanceCopy = {
  activity: 'Supplier-confirmed',
  spend: 'Spend-based estimate',
} satisfies Record<CarbonBasis, string>

/** Ruling 4: the uncertainty is a word, never a ±%. Stands in for web/src/copy/labels.ts (C3) until it lands. */
export const uncertaintyCopy = {
  low: 'Low uncertainty',
  medium: 'Medium uncertainty',
  high: 'High uncertainty',
} satisfies Record<Uncertainty, string>

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

/** The node drawer. Lane cost and lead time always carry `illustrative` (CLAUDE.md). */
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
  illustrative: 'illustrative',
  requestData: 'Request data',
  simulate: 'Simulate',
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
}
