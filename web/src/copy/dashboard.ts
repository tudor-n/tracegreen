import type {
  ClaimDecision,
  ClaimKind,
  ClaimReasonCode,
  EnergyCertType,
  EnergySource,
  EvidenceLevel,
  NodeStatus,
  PlausibilityCode,
  ProblemCode,
  ShippingDocType,
  TransportMode,
  TrustBand,
  TrustSignalCode,
} from '@/api/client'
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
  /** Under an alternative the AI picked (F11): its reason, in the AI's words. */
  why: (reason: string) => `Why: ${reason}`,
  suggestedHelp:
    'An AI picked this supplier from the list as a fit for this part. The figures come from the simulator, not from the AI.',
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
  fileInput: 'Invoice: UBL XML, or a PDF to check before importing',
  /** After a PDF upload: the review form opens. */
  pdfReady: 'Check the fields read from the PDF.',
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
  InvalidQuestion: 'That question could not be asked.',
  NotPdf: 'Not a PDF.',
  PdfNoText: 'This PDF has no text to read.',
  PdfUnreadable: 'This PDF could not be read.',
  ExtractionUnavailable: 'No AI model could read the PDF right now.',
  DraftExpired: 'This check has run out of time.',
  InvalidInvoiceFields: 'A field needs fixing.',
  InvalidRecord: 'That record cannot be right.',
  NoClaim: 'The supplier gave no such answer.',
  ClaimChanged: 'The answer changed since this page loaded.',
  InvalidEvidence: 'The proof given cannot be right.',
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

/**
 * The trust model (F7) on the dashboard: the three figures of a node, how far its supplier answers can be believed, the
 * buyer's own records and the list of answers to check. Keyed by the generated codes, so a new level, reason or signal
 * fails the typecheck until it has words here. A reduction a supplier claims counts once something backs it; the copy
 * says "backed" and "not backed yet", never that a value is proven true.
 */
export const figuresCopy = {
  estimateShort: 'Estimate',
  supplierShort: 'From supplier',
  targetShort: 'Best possible',
  noAnswer: 'No answer yet',
  partialAnswer: 'Partial answer: the estimate counts',
  noMass: 'No mass known',
  answeredShort: (tonnes: string) => `answered ${tonnes} t`,
  energyShort: { grid: 'grid', renewable: 'renewable', unknown: 'unknown' } satisfies Record<EnergySource, string>,
  targetNote: 'Best possible is a what-if target: illustrative, not in the total.',
  countedNote: 'Counted: reductions nothing backs yet are added back.',
  changeTarget: 'Change target',
  targetMode: 'Mode',
  targetEnergy: 'Power',
  targetAlternative: 'Supplier',
  targetSameSupplier: 'This supplier',
  targetSave: 'Save target',
  targetReset: 'Back to default',
  cancel: 'Cancel',
  saving: 'Saving…',
  saved: 'Target saved.',
} as const

export const trustCopy = {
  titleShort: 'Trust in the answers',
  scoreShort: (score: number) => `${score}/100`,
  whyScore: 'Why this score',
  band: { strong: 'Well backed', moderate: 'Partly backed', review: 'Needs a check' } satisfies Record<TrustBand, string>,
  atRisk: (tonnes: string) => `${tonnes} of claimed reductions not backed yet`,
  nothingAtRisk: 'Every claimed reduction is backed.',
  partial: 'Partial answer: the spend estimate counts until both questions are answered.',
  claims: 'Answers',
  claimKind: { mode: 'Transport', energy: 'Power', mass: 'Mass' } satisfies Record<ClaimKind, string>,
  level: {
    contradicted: 'Contradicted',
    unproven: 'Not backed yet',
    documented: 'Backed by a document',
    corroborated: 'Matches your records',
    reviewed: 'Accepted by a person',
    notNeeded: 'Nothing to back',
  } satisfies Record<EvidenceLevel, string>,
  reason: {
    NO_EVIDENCE: 'No document or record backs this answer yet.',
    DOCUMENT_GIVEN: 'The supplier named a shipping document whose number passes its check.',
    DOCUMENT_INVALID: 'The document number fails its check.',
    DOCUMENT_MODE_MISMATCH: 'The document belongs to another transport mode.',
    CUSTOMS_AGREES: 'Your customs declaration gives the same mode at the border.',
    CUSTOMS_DISAGREES: 'Your customs declaration gives another mode at the border.',
    TRANSIT_FITS: 'The time from dispatch to receipt fits this mode and not the default.',
    TRANSIT_TOO_FAST: 'The goods arrived too soon after dispatch for this mode.',
    CERTIFICATE_GIVEN: 'The supplier named the certificate behind the renewable power.',
    CERTIFICATE_INVALID: 'The certificate id is not in a usable form.',
    NO_CERTIFICATE: 'Renewable power without a certificate counts as grid power.',
    MASS_BELOW_INVOICE: 'The shipping mass is below the goods mass on the invoice.',
    MASS_FAR_ABOVE_INVOICE: 'The shipping mass is more than twice the invoice mass.',
    RAISES_FIGURE: 'This answer does not lower the figure, so it is taken as given.',
    ACCEPTED_BY_BUYER: 'A person accepted this answer.',
    REJECTED_BY_BUYER: 'A person rejected this answer.',
  } satisfies Record<ClaimReasonCode, string>,
  signals: 'How the answer arrived',
  signal: {
    SAME_NETWORK_AS_LINK_MAKER: 'Answered from the network the link was made on',
    FAST_ANSWER: 'Answered within two minutes of the link',
    SELLER_TAX_ID_INVALID: "The seller's tax id fails its check",
    MODE_CHANGED_ON_LANE: 'Another mode than this supplier gave last time on this lane',
    PRIOR_REJECTION: 'An earlier answer from this supplier was rejected',
    SIMULATED_ANSWER: 'Answered by the demo tool, not a supplier',
    RECEIPT_BEFORE_DISPATCH: 'The receipt date is before the dispatch date',
  } satisfies Record<TrustSignalCode, string>,
  points: (points: number) => `−${points}`,
  accept: 'Accept',
  reject: 'Reject',
  acceptHelp: 'You checked a document for this answer: count it as given.',
  rejectHelp: 'The answer is wrong: count the default instead.',
  decided: { accepted: 'accepted', rejected: 'rejected' } satisfies Record<ClaimDecision, string>,
  massValue: (tonnes: string) => `${tonnes} t`,
  saving: 'Saving…',
  saved: 'Saved.',
  /** 409: the supplier answered again after this page loaded. */
  changed: 'The supplier changed this answer in the meantime. It now shows as it is; check it again.',
  invalid: 'That cannot be saved. A date in the future, or an MRN that is not 18 characters, is refused.',
} as const

/** What backs the answers: the supplier's document and certificate, and the buyer's own records of the delivery. */
export const evidenceCopy = {
  title: 'Evidence',
  shippingDoc: 'Shipping document',
  dispatchDate: 'Dispatched',
  certificate: 'Certificate',
  none: 'None given',
  docType: {
    containerNumber: 'Container',
    airWaybill: 'Air waybill',
    billOfLading: 'Bill of lading',
    cmr: 'CMR',
    eTransportUit: 'e-Transport UIT',
    railConsignment: 'Rail consignment note',
  } satisfies Record<ShippingDocType, string>,
  certType: {
    guaranteeOfOrigin: 'Guarantee of origin',
    iRec: 'I-REC',
    chinaGec: 'China GEC',
    taiwanTRec: 'Taiwan T-REC',
    ppa: 'Power purchase agreement',
    onSite: 'Own generation',
  } satisfies Record<EnergyCertType, string>,
  records: 'Your records',
  receivedOn: 'Goods received on',
  borderMode: 'Mode at the EU border (customs)',
  borderNone: 'Not recorded',
  mrn: 'Customs MRN',
  save: 'Save records',
  recordsPrompt: 'Add your goods receipt or customs record to back or contradict the transport answer.',
  recordedLine: (received: string | null, border: string | null) =>
    ['Your records:', received === null ? null : `received ${received}`, border === null ? null : `${border} at the border`]
      .filter(Boolean)
      .join(' '),
  addRecords: 'Add your records',
  editRecords: 'Edit records',
  receivedOnShort: 'Received on',
  borderModeShort: 'Customs: mode at border',
  fillDemo: 'Fill demo values',
  demoTag: 'demo values',
  close: 'Close',
} as const

/** The plausibility warnings on the dashboard (D2: shown, never blocking), by code. */
export const warningCopy = {
  AIR_HEAVY: 'Heavy for air freight',
  SEA_TOO_FAST: 'Too fast for sea freight',
  MASS_BELOW_INVOICE: 'Mass below the invoice',
} satisfies Record<PlausibilityCode, string>

/** The Overview's counter of answers to check and the filtered supplier list (F7). */
export const answersCopy = {
  count: (n: number) => (n === 1 ? '1 supplier answer to check' : `${n} supplier answers to check`),
  countBody: (tonnes: string) =>
    `${tonnes} of the reductions they claim is not backed yet, so the totals leave it out. Largest first.`,
  countBodyNoRisk: 'Their answers carry a contradiction or a weak score. Largest first.',
  filtered: 'Showing the answers to check, the most at stake first.',
  showAll: 'Show all suppliers',
  noneToCheck: 'No supplier answers left to check.',
  atRiskKpi: (tonnes: string) => `${tonnes} of claimed reductions not backed yet`,
} as const
