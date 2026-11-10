import { randomBytes } from 'node:crypto';
import { test, expect, Page } from '@playwright/test';
import { MockApi } from './support/mock-api';

/**
 * S32: correcting a segment in the player and restoring an earlier version, against the faked API
 * (see support/mock-api.ts). That search and exports use the corrected text is covered by the backend
 * integration tests. The credentials are throw-away values that only exist for this run.
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

test.describe('transcript editor', () => {
  test('a segment is corrected with the keyboard and the old version can be restored', async ({ page }) => {
    const job = api.addJob({
      fileName: 'meeting.mp3',
      rawTranscript: 'Hallo zusammen. Los geht es.',
      segments: [
        { index: 0, startMs: 0, endMs: 1500, text: 'Hallo zusammen.' },
        { index: 1, startMs: 1500, endMs: 4000, text: 'Los geht es.' },
      ],
    });
    await signIn(page, `/jobs/${job.id}`);

    // Enter on a segment opens the editor, Ctrl+Enter saves
    const first = page.getByTestId('segment-text').first();
    await first.focus();
    await page.keyboard.press('Enter');
    const editor = page.getByTestId('segment-editor');
    await expect(editor).toHaveValue('Hallo zusammen.');
    await editor.fill('Hallo Welt.');
    await page.keyboard.press('Control+Enter');

    await expect(editor).toBeHidden();
    await expect(page.getByTestId('segment-text').first()).toContainText('Hallo Welt.');

    // The history shows both versions; restoring the original creates a new version
    await page.getByTestId('segment-history-toggle').first().click();
    await expect(page.getByTestId('history-entry')).toHaveCount(2);
    await expect(page.getByTestId('history-entry').first().locator('ins')).toContainText('Welt.');
    await page.getByTestId('history-restore').click();

    await expect(page.getByTestId('segment-text').first()).toContainText('Hallo zusammen.');
    await expect(page.getByTestId('history-entry')).toHaveCount(3);
  });
});
