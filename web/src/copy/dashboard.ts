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
  /** The AI's one-line reason for the category, under the invoice line it read; shown only when the AI chose it. */
  whyCategory: 'Why this category',
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
  /** Once a link is on screen: links are single-use or expire, so the buyer can always ask for a fresh one. */
  requestAgain: 'New verification link',
  supplierPage: 'Supplier page link',
  supplierPageAgain: 'New supplier page link',
  supplierPageRequesting: 'Making the link…',
  simulate: 'Simulate',
} as const

/**
 * A simulated change (B5): the ghost node beside its supplier and the simulator panel in the drawer. The MAC badge
 * names come from ./labels.ts; cost, lead time and the MAC rest on illustrative lane figures and say so.
 */
export const scenarioCopy = {
  whatIf: 'What if',
  modeChange: (mode: string) => `Switch to ${mode}`,
  supplierSwap: (supplier: string) => `Switch to ${supplier}`,
  /** A swap to an alternative the node no longer lists. */
  supplierSwapUnnamed: 'Switch supplier',
  versusNow: (delta: string) => `${delta} vs now`,
  massUnknownShort: 'Mass unknown',
  saved: 'Saved',
  massUnknown: 'The invoice gives no mass for this item, so this change cannot be estimated.',
} as const

export const simulatorCopy = {
  title: 'Simulate a change',
  modes: 'Transport mode',
  current: 'now',
  noLeg: 'Greyed-out modes have no leg on this lane.',
  suppliers: 'Alternative supplier',
  /** An alternative's country and the mode its lane ships by, which the swap uses. */
  alternativeVia: (country: string, mode: string) => `${country} · ${mode}`,
  noAlternatives: 'No other supplier of this category in the alternatives list.',
  pick: 'Pick a mode or a supplier to see the change in emissions, cost and lead time.',
  nothingToTry: 'This lane has a leg for one mode only and no other supplier makes this item, so there is nothing to try.',
  simulating: 'Simulating…',
  emissions: 'Emissions',
  cost: 'Cost',
  leadTime: 'Lead time',
  mac: 'Abatement cost',
  perTonne: (eur: string, unit: string) => `${eur} per ${unit}`,
  /** "Apply" saves the change as a scenario next to the supplier (POST /api/scenarios); the supplier never changes. */
  apply: 'Apply',
  applying: 'Saving…',
  applied: 'Saved',
  applyNote: 'Saves this change as a scenario. The supplier itself stays as it is.',
  appliedNote: 'Saved as a scenario. The supplier itself stays as it is.',
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
 * The supplier page link (F12) in the same card: one link per supplier that lists every delivery from them on this
 * graph, reusable until it expires. The expiry, hotspot and loopback lines are the verification link's.
 */
export const portalLinkCopy = {
  ...linkCopy,
  title: 'Supplier page link',
  qr: 'QR code of the supplier page link',
  scan: "Scan it with the supplier's phone. It lists every delivery from this supplier and works until it expires.",
} as const

/**
 * The audit panel around the words of ./labels.ts (its title and the chain results). It is a tamper-evident audit log
 * and is never called anything else (CLAUDE.md, Copy rules). Actions and actors the API adds later show as their raw
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
  notChecked: 'Not checked yet. Verify the chain to recompute every hash.',
  verifying: 'Verifying…',
  checked: (entries: number) => `${entries} ${entries === 1 ? 'entry' : 'entries'} checked`,
  actions: {
    'seed.loaded': 'Demo data loaded',
    'invoice.imported': 'Invoice imported',
    'link.requested': 'Verification link requested',
    'portal.requested': 'Supplier page link requested',
    'verification.submitted': 'Supplier answered',
  } as Readonly<Partial<Record<string, string>>>,
  actors: {
    seed: 'Seed data',
    buyer: 'Buyer',
    demo: 'Demo',
    supplier: 'Supplier',
    portal: 'Supplier page',
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
  AlternativeNotFound: 'Alternative supplier not found.',
  InvalidChange: 'Change not accepted.',
  ModeNotAvailable: 'This lane has no such mode.',
  MassUnknown: 'Mass unknown for this item.',
  AlreadyConfirmed: 'Already confirmed by the supplier.',
  DeliveryChanged: 'Another answer arrived first.',
  TermsNotAccepted: 'Supplier terms not accepted.',
  CategoryNotAllowed: 'Category not in the list.',
  CategoryChanged: 'Category changed since this page loaded.',
}

/** The v2 shell: the sidebar's sections. The audit section keeps its one name, auditPanelCopy.open. */
export const shellCopy = {
  sections: 'Sections',
  overview: 'Overview',
  suppliers: 'Suppliers',
} as const

/** The Overview: where the buyer stands, in three cards. Every figure is the API's KPI as it is. */
export const overviewCopy = {
  title: 'Overview',
  /** The API counts one node per invoice line, so the caption counts deliveries, not suppliers. */
  totalCaption: (count: number, deliveries: string, spend: string) =>
    `From ${deliveries} ${count === 1 ? 'delivery' : 'deliveries'} · ${spend} spend`,
  split: 'How the total splits',
  estimated: 'Estimated',
  confirmed: 'Supplier-confirmed',
  splitLabel: (confirmed: string, estimated: string) => `${confirmed} supplier-confirmed, ${estimated} estimated`,
  nextTitle: (supplier: string) => `Next: ask ${supplier} to confirm`,
  nextBody: 'It has the largest figure that no supplier has confirmed yet.',
  nextAction: 'Open supplier',
  allConfirmed: 'Every supplier has confirmed its figure.',
  allSuppliers: 'All suppliers',
} as const

/** The Suppliers screen: the tree of supply lines into the buyer, and the side panel for the selected supplier. */
export const suppliersCopy = {
  title: 'Suppliers',
  subtitle: 'Sorted by emissions, largest at the top. Line width shows tonnes.',
  selectHint: 'Select a supplier to see its details.',
  close: 'Close',
  tabs: { summary: 'Summary', details: 'Details', factors: 'Factors', whatIf: 'What if' },
  tabsLabel: 'Supplier details',
  askTitle: 'Ask the supplier to confirm',
  askBody: 'They answer two questions on their phone. No account, no login.',
  factorsIntro: (figure: string) => `The rows behind ${figure}`,
  /** For a confirmed node: the rows behind the spend-based figure it replaced. */
  factorsBefore: (figure: string) => `Before confirmation: the rows behind ${figure}`,
  statusKey: 'Status key',
} as const

/**
 * The classification review (F6): the Overview's counter, the filtered supplier list, and the side panel's control.
 * The flag itself and the buyer's provenance note are fixed labels in ./labels.ts. English only, like the rest of the
 * dashboard; the wording is Dev C's to settle.
 */
export const reviewCopy = {
  count: (n: number) => (n === 1 ? '1 category to check' : `${n} categories to check`),
  countBody: 'The AI chose these categories. Confirm or change each one; the figures follow the category.',
  filtered: 'Showing the categories to check.',
  showAll: 'Show all suppliers',
  noneToCheck: 'No categories left to check.',
  title: 'Category',
  lineLabel: 'Invoice line',
  confirm: 'Correct',
  change: 'Change category',
  choices: 'Categories to choose from',
  current: 'current',
  saving: 'Saving…',
  saved: 'Category saved.',
  /** 409: another person or page set the category first; the panel now shows it as it is. */
  changed: 'Someone changed this category in the meantime. It now shows as it is; check it again.',
  /** 422: a category the API no longer accepts. */
  notAllowed: 'That category is not in the list. Pick one of the categories shown.',
  categoriesLoading: 'Loading the categories…',
  categoriesFailed: "Can't load the categories.",
  retry: 'Try again',
} as const
