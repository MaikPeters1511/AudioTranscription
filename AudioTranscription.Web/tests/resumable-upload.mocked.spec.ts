import { randomBytes } from 'node:crypto';
import { test, expect, Page } from '@playwright/test';
import { MockApi } from './support/mock-api';

/**
 * S27: a large upload goes over the tus protocol, survives a lost connection in the middle and creates
 * exactly one job. Against the faked API (see support/mock-api.ts, which simulates the dropped request).
 * The credentials are throw-away values that only exist for this run.
 */
const email = 'e2e@example.com';
const password = randomBytes(16).toString('hex');

let api: MockApi;

test.beforeEach(async ({ page }) => {
  api = new MockApi(page, { email, password });
  await api.install();
});

async function signIn(page: Page, target: string) {
  await page.goto(target);
  await expect(page).toHaveURL(/\/login/);
  await page.getByLabel('E-Mail').fill(email);
  await page.getByLabel('Passwort').fill(password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
}

test.describe('resumable upload', () => {
  test('an upload interrupted in the middle continues and creates exactly one job', async ({ page }) => {
    // 12 MiB are sent in three chunks (5 + 5 + 2 MiB); the connection breaks while the second chunk is on its way
    api.dropTusPatchNumber = 2;
    await signIn(page, '/upload');
    await expect(page).toHaveURL(/\/upload$/);

    await page.setInputFiles('input[type="file"]', {
      name: 'grosse-aufnahme.mp3',
      mimeType: 'audio/mpeg',
      buffer: Buffer.concat([Buffer.from('ID3'), Buffer.alloc(12 * 1024 * 1024)]),
    });

    // The upload carries on by itself and ends in the new job
    await expect(page).toHaveURL(/\/jobs\/[^/]+$/, { timeout: 30_000 });
    expect(api.requests.filter((r) => r === 'POST /api/uploads')).toHaveLength(1);
    expect(api.requests.filter((r) => r === 'PATCH /api/uploads/tus1').length).toBeGreaterThanOrEqual(4); // 3 chunks + 1 repeated
    expect([...api.jobs.values()].filter((j) => j.fileName === 'grosse-aufnahme.mp3')).toHaveLength(1);
  });
});
