import type { VerifyContext, VerifyOutcome, VerifyRequest } from '@/api/client'
import type { Failure, Reply, VerifyApi } from './verify-state'

// Stands in for the API until the page is wired to it (C5, step 2; delete this file then). The link's token picks the
// scenario, so each state of the page opens at its own URL, for example
// /verify/11111111-1111-4111-8111-000000000001?t=fixture-used&lang=en. The contexts copy the seed (N1 and N3).

const N1: VerifyContext = {
  nodeId: '11111111-1111-4111-8111-000000000001',
  supplierLabel: 'Shenzhen Diecast Co.',
  buyerName: 'Transilvania Electro-Assembly SRL',
  invoiceNumber: 'SDC-2026-1028',
  issueDate: '2026-10-28',
  itemDescription: 'Zinc die-cast sensor housings, type ZH-220',
  origin: 'Shenzhen, CN',
  dest: 'Sibiu, RO',
  modes: ['rail', 'sea', 'air'],
  defaultMode: 'air',
  energySources: ['grid', 'renewable', 'unknown'],
  defaultEnergy: 'grid',
  massT: 2.3,
  expiresAt: '2026-11-05T12:00:00Z',
}

const N3: VerifyContext = {
  ...N1,
  nodeId: '11111111-1111-4111-8111-000000000003',
  supplierLabel: 'Oradea Harness SRL',
  invoiceNumber: 'OH-2026-1005',
  issueDate: '2026-10-05',
  itemDescription: 'Wire harness set for sensor module, 12-way',
  origin: 'Oradea, RO',
  modes: ['road'],
  defaultMode: 'road',
  massT: 1.4,
}

/** Tokens whose context request fails, and how. Any token not listed here or below is refused as not valid. */
const CONTEXT_FAILURES: Record<string, Failure> = {
  'fixture-used': 'alreadyUsed',
  'fixture-expired': 'expired',
  'fixture-changed': 'invoiceChanged',
  'fixture-busy': 'rateLimited',
}

const attempts = new Map<string, number>()
const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms))

/** Counts calls per token and request, so a scenario can fail the first time and succeed on retry. */
function attempt(key: string): number {
  const n = (attempts.get(key) ?? 0) + 1
  attempts.set(key, n)
  return n
}

export const fixtureApi: VerifyApi = {
  async context(token): Promise<Reply<VerifyContext>> {
    await wait(token === 'fixture-slow' ? 5000 : 400)
    if (token in CONTEXT_FAILURES) return { ok: false, failure: CONTEXT_FAILURES[token] }
    // Always: in development React runs the first request twice and keeps the second reply (StrictMode).
    if (token === 'fixture-offline') return { ok: false, failure: 'offline' }
    if (token === 'fixture-road') return { ok: true, data: N3 }
    if (['fixture', 'fixture-slow', 'fixture-lost'].includes(token)) return { ok: true, data: N1 }
    return { ok: false, failure: 'invalid' }
  },

  async submit(request: VerifyRequest): Promise<Reply<VerifyOutcome>> {
    await wait(700)
    if (request.t === 'fixture-lost' && attempt(`submit:${request.t}`) === 1) return { ok: false, failure: 'offline' }
    const context = request.t === 'fixture-road' ? N3 : N1
    const both = request.transportMode != null && request.energySource != null
    const heavyAir = request.transportMode === 'air' && (context.massT ?? 0) > 2
    return {
      ok: true,
      data: {
        nodeId: context.nodeId,
        status: both ? 'VERIFIED' : 'PARTIAL',
        warnings: heavyAir ? [{ code: 'AIR_HEAVY', detail: 'Air freight above 2 t' }] : [],
      },
    }
  },
}
