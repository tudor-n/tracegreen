import { devices, expect, test } from '@playwright/test'

// The demo path (B6) as one scripted run against the running Compose stack: the dashboard on the laptop, and the
// supplier page on a phone-sized second page. Every check reads a data-testid or another data-* attribute, never copy,
// so a wording change does not break the smoke. The run starts with a reset, so it wipes whatever the stack held.

/** N1, the demo invoice's node; its id is fixed in seed/demo-state.json. */
const N1 = '11111111-1111-4111-8111-000000000001'

test('demo path: import, verify on the phone, live flip', async ({ page, browser }) => {
  const n1 = page.getByTestId(`node-${N1}`)
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
  } finally {
    await phone.close()
  }
})
