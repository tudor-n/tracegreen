import { fileURLToPath } from 'node:url'
import { type BrowserContext, devices, expect, test } from '@playwright/test'

// The supplier page (F12) against the running stack: the buyer imports a second invoice from N3's seller, sends the
// supplier page link, and the phone confirms both deliveries from one list while the dashboard flips each node live.
// Like the demo-path smoke, every check reads a data-testid or another data-* attribute, never copy, and the run starts
// with a reset, so it wipes whatever the stack held. Its calls count against the supplier page's own rate limit, not the
// phone page's, so it can run right after the demo-path smoke.

/** N3, Oradea Harness SRL's seed invoice; its id is fixed in seed/demo-state.json. */
const N3 = '11111111-1111-4111-8111-000000000003'

/** A second invoice from the same seller (its tax id), with an injected instruction in a line description. */
const SECOND_INVOICE = fileURLToPath(new URL('../../seed/invoices/adversarial.xml', import.meta.url))

function watchCsp(context: BrowserContext, into: string[]): void {
  context.on('console', (message) => {
    if (message.type() === 'error' && /Content Security Policy/i.test(message.text())) into.push(message.text())
  })
}

test('supplier page: one link lists every delivery from the supplier, each confirmed live', async ({ page, browser }) => {
  const n3 = page.getByTestId(`node-${N3}`)
  const nodes = page.locator('[data-testid^="node-"][data-status]')
  const cspViolations: string[] = []
  watchCsp(page.context(), cspViolations)
  let link = ''

  await test.step('1. reset, then import a second invoice from N3’s seller', async () => {
    await page.goto('/')
    await page.getByTestId('toolbar-reset').click()
    // The seed start state is N2-N5 (no N1, no second Oradea invoice); waiting for it proves the reset has landed.
    await expect(nodes).toHaveCount(4)
    await expect(n3).toHaveAttribute('data-status', 'ESTIMATED')
    await page.getByTestId('import-file').setInputFiles(SECOND_INVOICE)
    await expect(nodes).toHaveCount(5)
  })

  await test.step('2. N3’s drawer gives the supplier page link', async () => {
    await n3.click()
    await page.getByTestId('drawer-portal').click()
    link = (await page.getByTestId('portal-url').innerText()).trim()
    expect(link).toMatch(/\/supplier\?t=p1\./)
  })

  const phone = await browser.newContext({ ...devices['Pixel 7'] })
  watchCsp(phone, cspViolations)
  try {
    const supplier = await phone.newPage()
    const supplierPage = supplier.getByTestId('supplier-page')
    const deliveries = supplier.locator('[data-testid^="delivery-"][data-status]')

    let second = ''
    await test.step('3. the phone opens the link: both deliveries, both waiting', async () => {
      await supplier.goto(link)
      await expect(supplierPage).toHaveAttribute('data-phase', 'ready')
      await expect(deliveries).toHaveCount(2)
      const ids = await deliveries.evaluateAll((items) => items.map((i) => i.getAttribute('data-testid') ?? ''))
      second = ids.map((id) => id.replace(/^delivery-/, '')).find((id) => id !== N3) ?? ''
      expect(second).not.toBe('')
      for (const id of [N3, second]) {
        await expect(supplier.getByTestId(`delivery-${id}`)).toHaveAttribute('data-status', 'ESTIMATED')
      }
    })

    await test.step('4. N3 confirmed with Road + Mostly renewable: VERIFIED on the dashboard within 2 s', async () => {
      await supplier.getByTestId(`delivery-${N3}-open`).click()
      await expect(supplier.getByTestId(`delivery-${N3}-transport-road`)).toHaveAttribute('aria-pressed', 'true')
      await supplier.getByTestId(`delivery-${N3}-energy-renewable`).click()
      await supplier.getByTestId(`delivery-${N3}-confirm`).click()
      await expect(n3).toHaveAttribute('data-status', 'VERIFIED', { timeout: 2_000 })
      await expect(supplier.getByTestId(`delivery-${N3}`)).toHaveAttribute('data-status', 'VERIFIED')
    })

    await test.step('5. the second delivery comes pre-selected with the same power source', async () => {
      await supplier.getByTestId(`delivery-${second}-open`).click()
      await expect(supplier.getByTestId(`delivery-${second}-energy-renewable`)).toHaveAttribute('aria-pressed', 'true')
      await supplier.getByTestId(`delivery-${second}-confirm`).click()
      await expect(page.getByTestId(`node-${second}`)).toHaveAttribute('data-status', 'VERIFIED', { timeout: 2_000 })
    })

    await test.step('6. the same link opened again: both confirmed', async () => {
      const again = await phone.newPage()
      await again.goto(link)
      await expect(again.getByTestId('supplier-page')).toHaveAttribute('data-phase', 'ready')
      for (const id of [N3, second]) {
        await expect(again.getByTestId(`delivery-${id}`)).toHaveAttribute('data-status', 'VERIFIED')
      }
    })
  } finally {
    await phone.close()
  }

  await test.step('7. the audit panel shows the chain as valid', async () => {
    await page.getByTestId('toolbar-audit').click()
    await expect(page.getByTestId('audit-panel')).toBeVisible()
    await page.getByTestId('audit-verify').click()
    await expect(page.getByTestId('audit-chain')).toHaveAttribute('data-valid', 'true')
  })

  await test.step('8. no Content Security Policy violation on the dashboard or the phone', () => {
    expect(cspViolations).toEqual([])
  })
})
