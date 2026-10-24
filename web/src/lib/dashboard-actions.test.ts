import { describe, expect, it } from 'vitest'
import { failureMessage } from './dashboard-actions'

const problem = (status: number, code: string, detail: string) => ({
  type: 'about:blank',
  title: 'ignored: clients switch on code, never on titles',
  status,
  detail,
  code,
  nodeId: null,
})

describe('failureMessage', () => {
  it('puts the headline for the code before the API detail', () => {
    expect(failureMessage(409, problem(409, 'Duplicate', 'Invoice SDC-2026-1028 is already imported.'))).toBe(
      'Already on the graph. Invoice SDC-2026-1028 is already imported.',
    )
    expect(failureMessage(400, problem(400, 'DoctypeForbidden', 'Document type definitions are not accepted.'))).toBe(
      'Refused for safety. Document type definitions are not accepted.',
    )
    expect(failureMessage(413, problem(413, 'TooLarge', 'The invoice exceeds 4 MB.'))).toBe(
      'File too large. The invoice exceeds 4 MB.',
    )
    expect(failureMessage(422, problem(422, 'Schema', 'Malformed XML: Data at the root level is invalid.'))).toBe(
      'The invoice could not be read. Malformed XML: Data at the root level is invalid.',
    )
  })

  it('shows the headline alone when the detail is empty', () => {
    expect(failureMessage(422, problem(422, 'NoLane', '  '))).toBe('Supplier country not covered.')
  })

  it('falls back to the HTTP status for a body that is not an ApiProblem', () => {
    expect(failureMessage(502, '<html>Bad Gateway</html>')).toBe('The API answered HTTP 502. Try again.')
    expect(failureMessage(500, undefined)).toBe('The API answered HTTP 500. Try again.')
    expect(failureMessage(500, problem(500, 'SomethingNew', 'detail'))).toBe('The API answered HTTP 500. Try again.')
    expect(failureMessage(500, problem(500, 'toString', 'detail'))).toBe('The API answered HTTP 500. Try again.')
  })

  it("reads a proxy's bare 413 as too large", () => {
    expect(failureMessage(413, '<html>413 Request Entity Too Large</html>')).toBe('File too large.')
  })
})
