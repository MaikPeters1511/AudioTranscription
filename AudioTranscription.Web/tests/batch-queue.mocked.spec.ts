import { randomBytes } from 'node:crypto';
import { test, expect, Page } from '@playwright/test';
import { MockApi } from './support/mock-api';

/**
 * S34: uploading several files at once and the queue overview, against the faked API
 * (see support/mock-api.ts). The credentials are throw-away values that only exist for this run.
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

test.describe('batch upload', () => {
  test('ten files are uploaded in one go', async ({ page }) => {
    await signIn(page, '/upload');
    await expect(page).toHaveURL(/\/upload$/);

    await page.setInputFiles(
      'input[type="file"]',
      Array.from({ length: 10 }, (_, i) => ({
        name: `aufnahme-${i + 1}.mp3`,
        mimeType: 'audio/mpeg',
        buffer: Buffer.concat([Buffer.from('ID3'), Buffer.alloc(1024)]),
      })),
    );

    await expect(page.getByTestId('batch-item')).toHaveCount(10);
    await expect(page.getByTestId('batch-status')).toHaveText(Array(10).fill('Hochgeladen'));
    await expect(page.getByRole('status').filter({ hasText: '10 von 10 hochgeladen' })).toBeVisible();
    expect(api.requests.filter((r) => r === 'POST /api/audio-jobs')).toHaveLength(10);

    // Every file got its own job; the user is not thrown into a single one
    await expect(page).toHaveURL(/\/upload$/);
    await page.getByRole('link', { name: 'Warteschlange anzeigen' }).click();
    await expect(page).toHaveURL(/\/queue$/);
  });

  test('a file with an unsupported type fails alone and the others are uploaded', async ({ page }) => {
    await signIn(page, '/upload');

    await page.setInputFiles('input[type="file"]', [
      { name: 'ok-1.mp3', mimeType: 'audio/mpeg', buffer: Buffer.from('ID3') },
      { name: 'notizen.txt', mimeType: 'text/plain', buffer: Buffer.from('x') },
      { name: 'ok-2.mp3', mimeType: 'audio/mpeg', buffer: Buffer.from('ID3') },
    ]);

    await expect(page.getByTestId('batch-status')).toHaveText(['Hochgeladen', 'Fehlgeschlagen', 'Hochgeladen']);
    expect(api.requests.filter((r) => r === 'POST /api/audio-jobs')).toHaveLength(2);
  });
});

test.describe('queue', () => {
  test('a job moved up in priority comes first and a cancelled job disappears', async ({ page }) => {
    api.queueJob('erste.mp3');
    api.queueJob('zweite.mp3');
    api.queueJob('dritte.mp3');
    await signIn(page, '/queue');

    const rows = page.locator('tbody tr');
    await expect(rows).toHaveCount(3);
    await expect(rows.nth(0)).toContainText('erste.mp3');

    await page.getByLabel('Priorität von dritte.mp3').selectOption({ label: 'Hoch' });
    await expect(rows.nth(0)).toContainText('dritte.mp3');
    await expect(rows.nth(1)).toContainText('erste.mp3');

    await page.getByRole('button', { name: 'erste.mp3 abbrechen' }).click();
    await expect(rows).toHaveCount(2);
    await expect(page.getByText('erste.mp3')).toHaveCount(0);
  });

  test('an empty queue says so', async ({ page }) => {
    await signIn(page, '/queue');

    await expect(page.getByText('Derzeit ist nichts in der Warteschlange.')).toBeVisible();
  });
});
