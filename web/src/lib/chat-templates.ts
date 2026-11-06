import type { ChatAnswer, ChatFactView, Kpis, NodeView } from '@/api/client'
import { chatCopy, type TemplateId } from '@/copy/chat'
import { byCarbonDesc } from '@/store'

// The questions the chat answers with a fixed sentence from the store's own data, without the AI (F13): instantly, and
// when no AI is available. Every value is a fact chip like an AI answer's, with the same raw values the API sends.
// Nothing here computes carbon: totals and shares are the API's KPIs, the order is the app's one emissions order.

const fact = (id: string, field: string, kind: ChatFactView['kind'], value: string, node?: NodeView): ChatFactView => ({
  id,
  field,
  kind,
  value,
  subject: node?.label ?? null,
  nodeId: node?.id ?? null,
  auditId: null,
})

/** Text and facts in turn: `parts[0] fact[0] parts[1] fact[1] … parts[n]`. */
function sentence(parts: readonly string[], facts: ChatFactView[]): ChatAnswer {
  const segments: ChatAnswer['segments'] = []
  parts.forEach((text, i) => {
    if (text !== '') segments.push({ text, factId: null })
    if (i < facts.length) segments.push({ text: null, factId: facts[i].id })
  })
  return { status: 'answered', segments, facts, provider: 'template' }
}

export function templateAnswer(id: TemplateId, nodes: readonly NodeView[], kpis: Kpis | null): ChatAnswer {
  const ranked = [...nodes].sort(byCarbonDesc)
  if (ranked.length === 0 || kpis === null) {
    return { status: 'answered', segments: [{ text: chatCopy.answers.empty, factId: null }], facts: [], provider: 'template' }
  }
  switch (id) {
    case 'top': {
      const top = ranked[0]
      return sentence(chatCopy.answers.top, [
        fact('S1.name', 'name', 'text', top.label, top),
        fact('S1.carbon', 'carbon', 'carbonKg', String(top.carbonKg), top),
        fact('S1.status', 'status', 'status', top.status, top),
      ])
    }
    case 'total':
      return sentence(chatCopy.answers.total, [
        fact('total.carbon', 'totalCarbon', 'carbonKg', String(kpis.totalKg)),
        fact('count.deliveries', 'deliveries', 'count', String(kpis.nodeCount)),
      ])
    case 'confirmed':
      return sentence(chatCopy.answers.confirmed, [fact('share.confirmed', 'confirmedShare', 'share', String(kpis.primaryShare))])
    case 'estimated': {
      const labels = [...new Set(ranked.filter((n) => n.status === 'ESTIMATED').map((n) => n.label))]
      return sentence(chatCopy.answers.estimated, [
        fact('list.estimated', 'listEstimated', 'text', labels.length === 0 ? 'none' : labels.join(', ')),
      ])
    }
  }
}
