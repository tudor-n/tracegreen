import { useEffect, useState } from 'react'
import { fetchDemoTools } from '@/lib/dashboard-actions'

/**
 * Whether the API runs with demo tools (`/api/health` `demoTools`), asked on mount and again whenever `recheck` changes
 * (the dashboard passes whether the API is reachable, since it may have restarted with other settings). False until the
 * answer arrives and whenever the API cannot be asked, so a demo-only control never shows by mistake.
 */
export function useDemoTools(recheck: unknown = true): boolean {
  const [enabled, setEnabled] = useState(false)
  useEffect(() => {
    if (recheck === false) return
    const aborts = new AbortController()
    void fetchDemoTools(aborts.signal).then((on) => {
      if (!aborts.signal.aborted) setEnabled(on)
    })
    return () => aborts.abort()
  }, [recheck])
  return enabled
}
