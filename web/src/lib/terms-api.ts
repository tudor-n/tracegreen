import type { Client } from 'openapi-fetch'
import { api, type SupplierTerms } from '@/api/client'
import type { paths } from '@/api/schema'

// The printable terms page's one request. The terms are public text behind no token, so any failure reads as "could
// not be loaded" with a retry; a request that hangs counts as failed.

export const TERMS_TIMEOUT_MS = 10_000

export type TermsReply = { ok: true; data: SupplierTerms } | { ok: false }

export interface TermsApi {
  supplier(): Promise<TermsReply>
}

export function createTermsApi(client: Client<paths>, timeoutMs = TERMS_TIMEOUT_MS): TermsApi {
  return {
    supplier: async () => {
      try {
        const sent = await client.GET('/api/terms/supplier', { signal: AbortSignal.timeout(timeoutMs) })
        return sent.response.ok && sent.data !== undefined ? { ok: true, data: sent.data } : { ok: false }
      } catch {
        return { ok: false }
      }
    },
  }
}

export const termsApi = createTermsApi(api)
