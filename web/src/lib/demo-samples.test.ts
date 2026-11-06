import { describe, expect, it } from 'vitest'
import { certIdProblem } from './evidence'
import { addDays, DEMO_CERT_IDS, DEMO_CONTAINER, demoMrn, demoRecords, demoSamplesValid } from './demo-samples'

describe('demo samples', () => {
  it('passes the same checks as a typed number', () => {
    expect(demoSamplesValid()).toBe(true)
    expect(DEMO_CONTAINER).toMatch(/^TGNU100100\d$/)
    for (const id of Object.values(DEMO_CERT_IDS)) expect(certIdProblem(id)).toBeNull()
  })

  it('gives an MRN in the 18-character shape the API checks', () => {
    expect(demoMrn(2026)).toBe('26RODEMO0000000001')
    expect(demoMrn(2026)).toMatch(/^[0-9]{2}[A-Z]{2}[A-Z0-9]{14}$/)
  })

  it('puts the receipt one lead time after dispatch, with the claimed mode at the border', () => {
    expect(demoRecords('sea', 40, '2026-09-01', '2026-10-05', '2026-11-07')).toEqual({
      receivedOn: '2026-10-11',
      customsBorderMode: 'sea',
      customsMrn: '26RODEMO0000000001',
    })
    expect(demoRecords('air', 7, null, '2026-10-05', '2026-11-07').receivedOn).toBe('2026-10-12')
    // Never in the future: the API refuses that.
    expect(demoRecords('sea', 40, null, '2026-10-28', '2026-11-07').receivedOn).toBe('2026-11-07')
    expect(addDays('2026-12-30', 3)).toBe('2027-01-02')
  })
})
