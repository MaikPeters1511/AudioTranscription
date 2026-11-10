import { randomBytes } from 'node:crypto';
import { test, expect, Page } from '@playwright/test';
import { MockApi } from './support/mock-api';

/**
 * S25: generating chapters and jumping to one from the player, against the faked API
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

test.describe('chapters', () => {
  test('chapters are generated, marked on the timeline and can be jumped to', async ({ page }) => {
    const job = api.addJob({
      fileName: 'meeting.mp3',
      rawTranscript: 'Hallo zusammen. Zum Budget. Und zum Ausblick. Ende.',
      segments: [
        { index: 0, startMs: 0, endMs: 5000, text: 'Hallo zusammen.' },
        { index: 1, startMs: 5000, endMs: 10_000, text: 'Zum Budget.' },
        { index: 2, startMs: 10_000, endMs: 15_000, text: 'Und zum Ausblick.' },
        { index: 3, startMs: 15_000, endMs: 20_000, text: 'Ende.' },
      ],
    });
    await signIn(page, `/jobs/${job.id}`);

    await page.getByTestId('chapter-generate').click();

    await expect(page.getByTestId('chapter-marker')).toHaveCount(2);
    await expect(page.getByTestId('chapter-row')).toHaveCount(2);
    await expect(page.getByTestId('chapter-row').nth(1)).toContainText('Thema 2');

    // A chapter is reachable by keyboard: focus its jump button and press Enter
    await page.getByTestId('chapter-jump').nth(1).focus();
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('chapter-jump').nth(1)).toHaveAttribute('aria-current', 'true');
  });
});
