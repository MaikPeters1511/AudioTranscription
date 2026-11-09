import { defineConfig, devices } from '@playwright/test';

/**
 * Read environment variables from file.
 * https://github.com/motdotla/dotenv
 */
// import dotenv from 'dotenv';
// import path from 'path';
// dotenv.config({ path: path.resolve(__dirname, '.env') });

/**
 * See https://playwright.dev/docs/test-configuration.
 */
const port = process.env['E2E_PORT'] ?? '4200';

export default defineConfig({
  testDir: './tests',
  /* Run tests in files in parallel */
  fullyParallel: true,
  /* Fail the build on CI if you accidentally left test.only in the source code. */
  forbidOnly: !!process.env.CI,
  /* Retry on CI only */
  retries: process.env.CI ? 2 : 0,
  /* Opt out of parallel tests on CI. */
  workers: process.env.CI ? 1 : undefined,
  /* Reporter to use. See https://playwright.dev/docs/test-reporters */
  reporter: process.env['CI'] ? [['html', { open: 'never' }], ['list']] : 'html',
  /* Shared settings for all the projects below. See https://playwright.dev/docs/api/class-testoptions. */
  use: {
    /* Base URL to use in actions like `await page.goto('')`. */
    /* Running app (e.g. `npm start` with the API behind the dev proxy) */
    baseURL: process.env['E2E_BASE_URL'] ?? `http://localhost:${port}`,

    /* Optional: use a preinstalled browser instead of `npx playwright install` */
    launchOptions: process.env['E2E_CHROMIUM_PATH']
      ? { executablePath: process.env['E2E_CHROMIUM_PATH'] }
      : {},

    /* Collect trace when retrying the failed test. See https://playwright.dev/docs/trace-viewer */
    trace: 'on-first-retry',

    /* The UI language follows the browser language (ADR 0002); the E2E tests assert German texts. */
    locale: 'de-DE',
  },

  /*
   * Two kinds of specs (S31):
   *  - *.mocked.spec.ts: UI flows against a faked API, need only the dev server (webServer below)
   *  - everything else: runs against a real, running app + API (E2E_EMAIL / E2E_PASSWORD, see the specs)
   */
  projects: [
    {
      name: 'mocked',
      testMatch: /.*\.mocked\.spec\.ts/,
      use: { ...devices['Desktop Chrome'] },
    },

    {
      name: 'chromium',
      testIgnore: /.*\.mocked\.spec\.ts/,
      use: { ...devices['Desktop Chrome'] },
    },

    {
      name: 'firefox',
      testIgnore: /.*\.mocked\.spec\.ts/,
      use: { ...devices['Desktop Firefox'] },
    },

    {
      name: 'webkit',
      testIgnore: /.*\.mocked\.spec\.ts/,
      use: { ...devices['Desktop Safari'] },
    },

    /* Test against mobile viewports. */
    // {
    //   name: 'Mobile Chrome',
    //   use: { ...devices['Pixel 5'] },
    // },
    // {
    //   name: 'Mobile Safari',
    //   use: { ...devices['iPhone 12'] },
    // },

    /* Test against branded browsers. */
    // {
    //   name: 'Microsoft Edge',
    //   use: { ...devices['Desktop Edge'], channel: 'msedge' },
    // },
    // {
    //   name: 'Google Chrome',
    //   use: { ...devices['Desktop Chrome'], channel: 'chrome' },
    // },
  ],

  /* Serve the app for the mocked specs unless an app is already running (E2E_BASE_URL or E2E_PORT, default 4200) */
  webServer: process.env['E2E_BASE_URL']
    ? undefined
    : {
        command: `npm start -- --port ${port}`,
        url: `http://localhost:${port}`,
        reuseExistingServer: !process.env['CI'],
        timeout: 120_000,
      },
});
