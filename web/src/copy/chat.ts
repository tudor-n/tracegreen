// Every string of the data chat (F13). The answers' sentences are the AI's or the templates below; every value in
// them is filled in from the workspace's data and shown as a chip that says what it is.

export const chatCopy = {
  open: 'Ask',
  title: 'Ask about your data',
  close: 'Close the chat',
  intro: 'Answers come from the data in this workspace. Every value is filled in from your data, never by the AI.',
  placeholder: 'Ask about suppliers, emissions or the audit log',
  inputLabel: 'Your question',
  send: 'Ask',
  sending: 'Checking your data…',
  tryThese: 'Try one of these',
  sources: 'Sources',
  aiWrittenHelp:
    'An AI wrote this sentence. Every value in it was filled in from your data, and its claims were checked against the data before it was shown.',
  fromDataHelp: 'Answered from your data with a fixed sentence; no AI was involved.',
  notAnswered: "I couldn't answer that from the data.",
  unavailable: "The AI isn't available right now. These questions are answered from your data:",
  tooLong: (max: number) => `Ask in ${max} characters or fewer.`,
  /** The chip's accessible name and tooltip: what the value is and whose. */
  factLabel: (field: string, subject: string | null) => (subject === null ? field : `${field} · ${subject}`),
  openSupplier: (subject: string) => `Open ${subject}`,
  openAudit: 'Open the audit log',
  none: 'none',
  /** The questions answered from the data without the AI, and their fixed sentences around the values. */
  questions: {
    top: 'Which supplier emits the most?',
    total: 'What are the total emissions?',
    confirmed: 'How much is supplier-confirmed?',
    estimated: 'Which suppliers are still estimated?',
  },
  answers: {
    top: ['The largest emitter is ', ' with ', ', ', '.'],
    total: ['Total emissions are ', ' across ', ' deliveries.'],
    confirmed: ['The supplier-confirmed share is ', ' of total emissions, by emissions, not by node count.'],
    estimated: ['Still estimated: ', '.'],
    empty: 'There are no deliveries yet.',
  },
  /** What each fact is, by the API's `field`. */
  fields: {
    name: 'Supplier',
    carbon: 'Emissions',
    status: 'Status',
    basis: 'Source of the figure',
    category: 'Category',
    country: 'Country',
    origin: 'Ships from',
    mode: 'Transport mode',
    spend: 'Spend',
    cost: 'Cost (illustrative)',
    leadTime: 'Lead time (illustrative)',
    invoice: 'Invoice',
    issued: 'Invoice date',
    totalCarbon: 'Total emissions',
    totalSpend: 'Total spend',
    deliveries: 'Deliveries',
    suppliers: 'Suppliers',
    confirmed: 'Supplier-confirmed deliveries',
    partial: 'Partly confirmed deliveries',
    estimated: 'Estimated deliveries',
    confirmedShare: 'Supplier-confirmed share',
    listConfirmed: 'Supplier-confirmed suppliers',
    listPartial: 'Partly confirmed suppliers',
    listEstimated: 'Estimated suppliers',
    othersCount: 'Further deliveries',
    othersCarbon: 'Emissions of further deliveries',
    scenarios: 'Saved scenarios',
    change: 'Scenario change',
    carbonDelta: 'Change in emissions',
    costDelta: 'Change in cost (illustrative)',
    auditEntries: 'Audit log entries',
    action: 'Audit log entry',
    when: 'Date',
  } as Readonly<Partial<Record<string, string>>>,
  unknownField: 'Value',
  /** The model picker: which AI writes the sentence. Whichever it is, every value is still filled in from the data. */
  models: {
    label: 'Model',
    auto: 'Automatic',
    loading: 'Looking for models…',
    autoHelp: 'Automatic asks each ready model in turn until one answers.',
    aloneHelp: 'Only this model is asked; if it cannot answer, the question is not answered.',
    none: 'No AI model is set up or found on this server (AI_CHAT_MODELS, AI_MODELS or Ollama). The suggested questions still work.',
    noneReady: 'No model has its key on this server yet. The suggested questions still work.',
    /** Nothing ready for Automatic, but the local model server has models to pick. */
    pickLocal: 'Automatic has no model set up yet (AI_CHAT_MODELS or AI_MODELS). Pick a local model, or ask a suggested question.',
    /** The picker's groups: the models Automatic asks, in its order, and the local server's others, asked only when picked. */
    autoGroup: 'Set up for Automatic',
    localGroup: 'Local models (Ollama)',
    serviceDown: "The AI service isn't answering. The suggested questions still work.",
    /** A listed model that cannot answer yet, with the server's reason (e.g. its key is not set). */
    notReady: (name: string, reason: string | null) => `${name} (not set up${reason === null ? '' : `: ${reason}`})`,
    writtenBy: (name: string) => `by ${name}`,
    /** The provider part of a model id, as a reader knows it; an unknown one shows as it is. */
    providers: {
      anthropic: 'Anthropic',
      openai: 'OpenAI',
      google_genai: 'Google',
      ollama: 'Ollama (local)',
    } as Readonly<Partial<Record<string, string>>>,
  },
} as const

export type TemplateId = keyof typeof chatCopy.questions
