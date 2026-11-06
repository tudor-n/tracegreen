import type { Client } from 'openapi-fetch'
import { api, type SupplierTerms } from '@/api/client'
import type { paths } from '@/api/schema'
import { reply, type Result } from './api-reply'
import { VERIFY_TIMEOUT_MS } from './verify-api'

// The printable terms page's one request. The terms are public text behind no token, so any failure, a hang included,
// reads as "could not be loaded" with a retry.

export type TermsReply = Result<SupplierTerms, 'unavailable'>

export interface TermsApi {
  supplier(): Promise<TermsReply>
}

export function createTermsApi(client: Client<paths>, timeoutMs = VERIFY_TIMEOUT_MS): TermsApi {
  return {
    supplier: () =>
      reply(
        () => client.GET('/api/terms/supplier', { signal: AbortSignal.timeout(timeoutMs) }),
        () => 'unavailable' as const,
      ),
  }
}

export const termsApi = createTermsApi(api)
