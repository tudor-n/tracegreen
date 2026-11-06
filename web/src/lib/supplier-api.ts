import type { Client } from 'openapi-fetch'
import { api } from '@/api/client'
import type { paths } from '@/api/schema'
import { reply } from './api-reply'
import { type SupplierApi, supplierFailureFor } from './supplier-state'
import { VERIFY_TIMEOUT_MS } from './verify-api'

// The supplier page's two requests through the generated client, read the supplier page's way. The token goes in the
// request body, never in an API URL. A request that hangs past the phone page's timeout counts as no connection.

export function createSupplierApi(client: Client<paths>, timeoutMs = VERIFY_TIMEOUT_MS): SupplierApi {
  return {
    view: (token) =>
      reply(
        () => client.POST('/api/portal/view', { body: { t: token }, signal: AbortSignal.timeout(timeoutMs) }),
        supplierFailureFor,
      ),
    answer: (body) =>
      reply(
        () => client.POST('/api/portal/answer', { body, signal: AbortSignal.timeout(timeoutMs) }),
        supplierFailureFor,
      ),
  }
}

export const supplierApi = createSupplierApi(api)
