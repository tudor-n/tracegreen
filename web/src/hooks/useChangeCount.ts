import { useState } from 'react'

/**
 * How many times `value` has changed since mount (0 on the first render). Keyed on this count, an element remounts and
 * replays its CSS animation once per change and never on mount: the number flash and the verify sweep.
 */
export function useChangeCount<T>(value: T): number {
  const [seen, setSeen] = useState({ value, count: 0 })
  if (Object.is(seen.value, value)) return seen.count
  const next = { value, count: seen.count + 1 }
  setSeen(next)
  return next.count
}
