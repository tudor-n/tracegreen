import { type BrowserContext, devices, expect, test } from '@playwright/test'

// The demo path (B6) as one scripted run against the running Compose stack: the dashboard on the laptop, and the
// supplier page on a phone-sized second page. Every check reads a data-testid or another data-* attribute, never copy,
// so a wording change does not break the smoke. The run starts with a reset, so it wipes whatever the stack held.

/** N1, the demo invoice's node; its id is fixed in seed/demo-state.json. */
const N1 = '11111111-1111-4111-8111-000000000001'
/** N4, Sibiu Pack SRL: a seed node whose category comes from the AI cache, so it starts with a category to check. */
const N4 = '11111111-1111-4111-8111-000000000004'

/**
 * Collects the browser's reports of Content Security Policy violations on every page of `context`. The demo image's
 * nginx sends the header (B7); under the Vite dev server there is none, so the list stays empty there.
 */
function watchCsp(context: BrowserContext, into: string[]): void {
  context.on('console', (message) => {
    if (message.type() === 'error' && /Content Security Policy/i.test(message.text())) into.push(message.text())
  })
}

test('demo path: import, verify on the phone, live flip, terms, simulator, category review, audit chain, CSP', async ({ page, browser }) => {
  const n1 = page.getByTestId(`node-${N1}`)
  const cspViolations: string[] = []
  watchCsp(page.context(), cspViolations)
  let link = ''

  await test.step('1. reset, then load the demo invoice: N1 is ESTIMATED', async () => {
    // The tree of supplier rows lives on the Suppliers screen; the sidebar with the demo tools is on every screen.
    await page.goto('/suppliers')
    await page.getByTestId('toolbar-reset').click()
    await expect(n1).toHaveCount(0)
    await page.getByTestId('toolbar-demo').click()
    await expect(n1).toHaveAttribute('data-status', 'ESTIMATED')
  })

  await test.step('2. open the side panel and request a link: the printed URL', async () => {
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

    await test.step('3. the phone opens the link, answers Air + Grid, ticks the supplier terms and submits', async () => {
      await supplier.goto(link)
      await expect(verifyPage).toHaveAttribute('data-phase', 'ready')
      await supplier.getByTestId('verify-transport-air').click()
      await supplier.getByTestId('verify-energy-grid').click()
      // The terms start unticked, their statement links to the full text without the token, and nothing can be sent
      // until the supplier ticks them.
      await expect(supplier.getByTestId('verify-terms')).not.toBeChecked()
      await expect(supplier.getByTestId('verify-terms-link')).toHaveAttribute('href', /^\/terms\/supplier\?lang=(ro|en)$/)
      await expect(supplier.getByTestId('verify-submit')).toBeDisabled()
      await supplier.getByTestId('verify-terms').check()
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

    await test.step('6. the printable supplier terms open at their own address, without a token', async () => {
      const terms = await phone.newPage()
      await terms.goto('/terms/supplier?lang=en')
      await expect(terms.getByTestId('terms-page')).toHaveAttribute('data-phase', 'ready')
      await expect(terms.getByTestId('terms-page')).toHaveAttribute('data-version', /^\d{4}-\d{2}-\d{2}$/)
    })
  } finally {
    await phone.close()
  }

  await test.step('7. simulator: Sea is win-win, N1-alt is premium or below-ETS; Apply leaves N1 as it is', async () => {
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

  await test.step('8. a category to check (F6): flagged, changed in two clicks, the flag clears, the figure and the KPI bar follow', async () => {
    // N4's category came from the AI cache, so it waits for a person; N1's came from its invoice code and does not.
    const n4 = page.getByTestId(`node-${N4}`)
    const checks = page.getByTestId('overview-checks')
    await page.getByTestId('nav-overview').click()
    await expect(checks).toHaveAttribute('data-count', '3')
    const total = (await page.getByTestId('kpi-total').innerText()).trim()

    // The counter opens the supplier list filtered to the categories to check, with the side panel closed.
    await checks.click()
    await expect(page.getByTestId('suppliers-filter')).toBeVisible()
    await expect(n1).toHaveCount(0)
    await expect(page.getByTestId(`node-${N4}-check`)).toBeVisible()
    await n4.click()
    await expect(page.getByTestId('drawer-check')).toBeVisible()
    await expect(page.getByTestId('review-control')).toHaveAttribute('data-review', 'pending')
    const figure = (await page.getByTestId('drawer-figure').innerText()).trim()

    await page.getByTestId('review-change').click()
    await page.getByTestId('review-option-pcb').click()
    // The store moves only with node.updated: once it lands, the flag is gone and the buyer is named as the source.
    await expect(page.getByTestId('review-control')).toHaveAttribute('data-review', 'corrected')
    await expect(page.getByTestId('drawer-check')).toHaveCount(0)
    await expect(page.getByTestId('drawer-category-source')).toBeVisible()
    await expect(page.getByTestId('drawer-figure')).not.toHaveText(figure)
    await expect(page.getByTestId(`node-${N4}-check`)).toHaveCount(0)

    await page.getByTestId('nav-overview').click()
    await expect(checks).toHaveAttribute('data-count', '2')
    await expect(page.getByTestId('kpi-total')).not.toHaveText(total)
  })

  await test.step('9. the audit log screen shows the chain as valid', async () => {
    await page.getByTestId('toolbar-audit').click()
    await expect(page.getByTestId('audit-panel')).toBeVisible()
    await page.getByTestId('audit-verify').click()
    await expect(page.getByTestId('audit-chain')).toHaveAttribute('data-valid', 'true')
  })

  await test.step('10. no Content Security Policy violation on the dashboard or the phone', () => {
    expect(cspViolations).toEqual([])
  })
})
