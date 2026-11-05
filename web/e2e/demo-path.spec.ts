import { type BrowserContext, devices, expect, test } from '@playwright/test'

// The demo path (B6) as one scripted run against the running Compose stack: the dashboard on the laptop, and the
// supplier page on a phone-sized second page. Every check reads a data-testid or another data-* attribute, never copy,
// so a wording change does not break the smoke. The run starts with a reset, so it wipes whatever the stack held.

/** N1, the demo invoice's node; its id is fixed in seed/demo-state.json. */
const N1 = '11111111-1111-4111-8111-000000000001'

/**
 * Collects the browser's reports of Content Security Policy violations on every page of `context`. The demo image's
 * nginx sends the header (B7); under the Vite dev server there is none, so the list stays empty there.
 */
function watchCsp(context: BrowserContext, into: string[]): void {
  context.on('console', (message) => {
    if (message.type() === 'error' && /Content Security Policy/i.test(message.text())) into.push(message.text())
  })
}

test('demo path: import, verify on the phone, live flip, simulator, audit chain, CSP', async ({ page, browser }) => {
  const n1 = page.getByTestId(`node-${N1}`)
  const cspViolations: string[] = []
  watchCsp(page.context(), cspViolations)
  let link = ''

  await test.step('1. reset, then load the demo invoice: N1 is ESTIMATED', async () => {
    await page.goto('/')
    await page.getByTestId('toolbar-reset').click()
    await expect(n1).toHaveCount(0)
    await page.getByTestId('toolbar-demo').click()
    await expect(n1).toHaveAttribute('data-status', 'ESTIMATED')
  })

  await test.step('2. open the drawer and request a link: the printed URL', async () => {
    await n1.click()
    await expect(page.getByTestId('node-drawer')).toBeVisible()
    await page.getByTestId('drawer-request').click()
    link = (await page.getByTestId('qr-url').innerText()).trim()
    expect(link).toMatch(new RegExp(`/verify/${N1}\\?t=v1\\.`))
  })

  const phone = await browser.newContext({ ...devices['Pixel 7'] })
  watchCsp(phone, cspViolations)
  try {
    const supplier = await phone.newPage()
    const verifyPage = supplier.getByTestId('verify-page')

    await test.step('3. the phone opens the link and answers Air + Grid', async () => {
      await supplier.goto(link)
      await expect(verifyPage).toHaveAttribute('data-phase', 'ready')
      await supplier.getByTestId('verify-transport-air').click()
      await supplier.getByTestId('verify-energy-grid').click()
      await supplier.getByTestId('verify-submit').click()
    })

    await test.step('4. the dashboard node is VERIFIED within 2 s of the submit', async () => {
      await expect(n1).toHaveAttribute('data-status', 'VERIFIED', { timeout: 2_000 })
      await expect(verifyPage).toHaveAttribute('data-result', 'thanks')
    })

    await test.step('5. the same link opened again: already used', async () => {
      const again = await phone.newPage()
      await again.goto(link)
      await expect(again.getByTestId('verify-page')).toHaveAttribute('data-result', 'alreadyUsed')
    })
  } finally {
    await phone.close()
  }

  await test.step('6. simulator: Sea is win-win, N1-alt is premium or below-ETS; Apply leaves N1 as it is', async () => {
    const result = page.getByTestId('sim-result')
    const ghost = page.getByTestId(`ghost-${N1}`)
    await page.getByTestId('drawer-simulate').click()
    await page.getByTestId('sim-mode-sea').click()
    await expect(result).toHaveAttribute('data-badge', 'win-win')
    await expect(ghost).toHaveAttribute('data-badge', 'win-win')

    await page.getByTestId('sim-alt-N1-alt').click()
    await expect(page.getByTestId('sim-alt-N1-alt')).toHaveAttribute('aria-pressed', 'true')
    await expect(result).toHaveAttribute('data-badge', /^(premium|below-ets)$/)
    await expect(ghost).toHaveAttribute('data-badge', /^(premium|below-ets)$/)

    await page.getByTestId('sim-apply').click()
    await expect(page.getByTestId('sim-apply')).toHaveAttribute('data-saved', 'true')
    await expect(page.getByTestId(`ghost-${N1}-saved`)).toBeVisible()
    await expect(n1).toHaveAttribute('data-status', 'VERIFIED')
  })

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
