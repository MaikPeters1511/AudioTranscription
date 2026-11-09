import { randomBytes } from 'node:crypto';
import { test, expect, Page } from '@playwright/test';
import { MockApi } from './support/mock-api';

/**
 * S31: the core user flows against a faked API (see support/mock-api.ts), so they run with
 * nothing but the Angular dev server. The credentials are throw-away values that only exist for this run.
 */
const email = 'e2e@example.com';
// Generated per run: no password-like literal in the repository (secret scanners flag those)
const password = randomBytes(16).toString('hex');

let api: MockApi;

test.beforeEach(async ({ page }) => {
  api = new MockApi(page, { email, password });
  await api.install();
});

async function signIn(page: Page, target = '/jobs') {
  await page.goto(target);
  await expect(page).toHaveURL(/\/login/);
  await page.getByLabel('E-Mail').fill(email);
  await page.getByLabel('Passwort').fill(password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
}

test.describe('login and logout', () => {
  test('a protected page sends anonymous users to the login and back afterwards', async ({ page }) => {
    await signIn(page, '/search');

    await expect(page).toHaveURL(/\/search$/);
    await expect(page.getByRole('heading', { name: 'Suche' })).toBeVisible();
  });

  test('wrong credentials show a clean message and keep the user on the login', async ({ page }) => {
    await page.goto('/jobs');
    await page.getByLabel('E-Mail').fill(email);
    await page.getByLabel('Passwort').fill('definitely-wrong');
    await page.getByRole('button', { name: 'Anmelden' }).click();

    await expect(page.getByRole('alert')).toContainText('Anmeldung fehlgeschlagen');
    await expect(page).toHaveURL(/\/login/);
  });

  test('logging out ends the session and protects the pages again', async ({ page }) => {
    await signIn(page);
    await expect(page).toHaveURL(/\/jobs$/);

    await page.getByRole('button', { name: 'Abmelden' }).click();
    await expect(page).toHaveURL(/\/login/);

    await page.goto('/jobs');
    await expect(page).toHaveURL(/\/login/);
  });
});

test.describe('upload to transcript', () => {
  test('an uploaded file leads to its transcript, which can be downloaded', async ({ page }) => {
    await signIn(page, '/upload');
    await expect(page).toHaveURL(/\/upload$/);

    await page.setInputFiles('input[type="file"]', {
      name: 'uploaded.mp3',
      mimeType: 'audio/mpeg',
      buffer: Buffer.concat([Buffer.from('ID3'), Buffer.alloc(2048)]),
    });

    await expect(page).toHaveURL(/\/jobs\/[0-9a-f-]{36}$/);
    await expect(page.getByText('Hallo Welt vom Upload')).toBeVisible();

    const download = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Download .txt' }).click();
    expect((await download).suggestedFilename()).toMatch(/\.txt$/);
  });
});

test.describe('search', () => {
  test('a hit is highlighted and opens the job at the matching time', async ({ page }) => {
    const job = api.addJob({
      fileName: 'meeting.mp3',
      rawTranscript: 'Wir sprechen über das Budget',
      segments: [
        { index: 0, startMs: 0, endMs: 4000, text: 'Willkommen zur Besprechung' },
        { index: 1, startMs: 12_000, endMs: 16_000, text: 'Wir sprechen über das Budget' },
      ],
    });
    await signIn(page, '/search');

    await page.getByLabel('Alle Transkripte durchsuchen').fill('Budget');

    const hit = page.getByRole('link', { name: /meeting\.mp3/ });
    await expect(hit).toBeVisible();
    await expect(hit.locator('mark')).toHaveText('Budget');

    await hit.click();
    await expect(page).toHaveURL(new RegExp(`/jobs/${job.id}\\?t=12000$`));
  });

  test('a word without matches says so', async ({ page }) => {
    api.addJob({ fileName: 'a.mp3', segments: [{ index: 0, startMs: 0, endMs: 1000, text: 'Hallo' }] });
    await signIn(page, '/search');

    await page.getByLabel('Alle Transkripte durchsuchen').fill('Nichtvorhanden');

    // The same sentence is also announced to screen readers (sr-only), only the visible one counts
    await expect(page.locator('p:not(.sr-only)').filter({ hasText: 'Keine Treffer' })).toBeVisible();
  });
});

test.describe('speakers and deleting', () => {
  test('a speaker can be renamed and the new name shows on every segment', async ({ page }) => {
    const job = api.addJob({
      fileName: 'interview.mp3',
      rawTranscript: 'Hallo. Guten Tag.',
      segments: [
        { index: 0, startMs: 0, endMs: 2000, text: 'Hallo.', speakerIndex: 0 },
        { index: 1, startMs: 2000, endMs: 4000, text: 'Guten Tag.', speakerIndex: 1 },
      ],
      speakers: [
        { index: 0, displayName: 'Sprecher 1' },
        { index: 1, displayName: 'Sprecher 2' },
      ],
    });
    await signIn(page, `/jobs/${job.id}`);

    await page.getByRole('button', { name: 'Sprecher 1 umbenennen' }).click();
    const input = page.getByTestId('speaker-rename-input');
    await input.fill('Anna');
    await input.press('Enter');

    await expect(page.getByTestId('speaker-toolbar')).toContainText('Anna');
    await expect(page.getByText('Anna').first()).toBeVisible();
    await expect(page.getByText('Sprecher 1')).toHaveCount(0);
    expect(job.speakers[0].displayName).toBe('Anna');
  });

  test('deleting a job needs a confirmation and removes it from the list', async ({ page }) => {
    const job = api.addJob({ fileName: 'weg.mp3', rawTranscript: 'zu löschen' });
    await signIn(page, `/jobs/${job.id}`);

    await page.getByRole('button', { name: 'Löschen', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Job löschen?' });
    await expect(dialog).toContainText('weg.mp3');

    // Keeping the job closes the dialog without calling the API
    await dialog.getByRole('button', { name: 'Behalten' }).click();
    await expect(dialog).toBeHidden();
    expect(api.requests).not.toContain(`DELETE /api/audio-jobs/${job.id}`);

    await page.getByRole('button', { name: 'Löschen', exact: true }).click();
    await dialog.getByRole('button', { name: 'Endgültig löschen' }).click();

    await expect(page).toHaveURL(/\/jobs$/);
    await expect(page.getByText('Job gelöscht')).toBeVisible();
    await expect(page.getByText('weg.mp3')).toHaveCount(0);
    expect(api.jobs.has(job.id)).toBe(false);
  });
});
