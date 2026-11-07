// Every string of the data chat (F13). The answers' sentences are the AI's or the templates below; every value in
// them is filled in from the workspace's data and shown as a chip that says what it is.

export const chatCopy = {
  open: 'AI assistant',
  title: 'AI assistant',
  subtitle: 'Answers from your supplier data',
  close: 'Close the AI assistant',
  newChat: 'New chat',
  welcome: 'Ask about your suppliers and emissions, or for help with TraceGreen.',
  intro: 'Every value in an answer is filled in from your data, never by the AI.',
  placeholder: 'Ask about your suppliers and emissions',
  inputLabel: 'Your question',
  send: 'Send',
  sending: 'Checking your data…',
  tryThese: 'Try one of these',
  sources: 'Sources',
  /** The toggle that shows or hides an answer's sources, with how many there are. */
  showSources: (count: number) => `Sources (${count})`,
  jumpToLatest: 'Jump to the latest message',
  retry: 'Try again',
  aiWrittenHelp:
    'An AI wrote this sentence. Every value in it was filled in from your data, and its claims were checked against the data before it was shown.',
  fromDataHelp: 'Answered from your data with a fixed sentence; no AI was involved.',
  generalHelp:
    'An AI wrote this from what TraceGreen does and general knowledge. It holds no value from your data; ask about a supplier or a figure to get one.',
  /** When no model gave an answer that passed the checks, even after being told why: what to do instead. */
  notAnswered: 'Here is what I can show you from your data right away:',
  notAnsweredSub: 'Or ask it another way, for example about one supplier.',
  unavailable: "The AI isn't available right now. These questions are answered from your data:",
  tooLong: (max: number) => `Ask in ${max} characters or fewer.`,
  /** The chip's accessible name and tooltip: what the value is and whose. */
  factLabel: (field: string, subject: string | null) => (subject === null ? field : `${field} · ${subject}`),
  openSupplier: (subject: string) => `Open ${subject}`,
  openWhatIf: (subject: string) => `Open What if for ${subject}`,
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
    leadDelta: 'Change in lead time (illustrative)',
    badge: 'Simulator verdict',
    whatIfs: 'What-ifs worked out',
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
    /** What Automatic asks, by name: one model, or several in turn. */
    autoHelp: (names: readonly string[]) =>
      names.length === 1 ? `Automatic asks ${names[0]}.` : `Automatic asks ${names.join(', then ')}, until one answers.`,
    aloneHelp: 'Only this model is asked.',
    none: 'No AI model is set up or found on this server (AI_CHAT_MODELS, AI_MODELS or Ollama). The suggested questions still work.',
    noneReady: 'No model has its key on this server yet. The suggested questions still work.',
    /** The picker's groups: the models Automatic asks, in its order, and the local server's others, asked only when picked. */
    autoGroup: 'Used by Automatic',
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
