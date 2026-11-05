import { defineConfig, devices } from '@playwright/test'

// The Playwright smoke (B6). It runs against a stack that is already up (`docker compose up` from the repo root) and
// starts no server itself. One worker, because every run resets the shared demo workspace first.
// `E2E_BASE_URL` points it at another origin, such as the demo image.

export default defineConfig({
  testDir: './e2e',
  workers: 1,
  fullyParallel: false,
  retries: 0,
  forbidOnly: Boolean(process.env.CI),
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:3000',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
})
