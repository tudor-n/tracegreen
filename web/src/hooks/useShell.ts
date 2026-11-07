import { useOutletContext } from 'react-router'
import type { Connection } from './graphSync'

/** What the app shell hands each screen: the state of the live updates, for the loading and offline messages. */
export interface ShellContext {
  connection: Connection
}

export function useShell(): ShellContext {
  return useOutletContext<ShellContext>()
}
