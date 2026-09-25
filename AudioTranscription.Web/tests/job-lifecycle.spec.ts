import { test, expect, Page } from '@playwright/test';

/**
 * S09 end-to-end against a running app + API (not mocked).
 * Requires a user: E2E_EMAIL / E2E_PASSWORD (e.g. the initial user from Auth:InitialUser).
 *
 * The uploaded bytes are only a valid MP3 header, so the transcription itself fails.
 * That is intended: it gives a deterministic Failed job to retry and delete.
 */
const email = process.env['E2E_EMAIL'];
const password = process.env['E2E_PASSWORD'];

test.skip(!email || !password, 'E2E_EMAIL and E2E_PASSWORD must be set');

async function login(page: Page) {
  await page.goto('/jobs');
  await expect(page).toHaveURL(/\/login/);
  await page.getByLabel('E-Mail').fill(email!);
  await page.getByLabel('Passwort').fill(password!);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).toHaveURL(/\/jobs$/);
}

async function uploadBrokenAudio(page: Page, fileName: string) {
  await page.goto('/upload');
  await page.setInputFiles('input[type="file"]', {
    name: fileName,
    mimeType: 'audio/mpeg',
    buffer: Buffer.concat([Buffer.from('ID3'), Buffer.alloc(2048)]),
  });
  await expect(page).toHaveURL(/\/jobs\/[0-9a-f-]{36}$/);
}

test('failed job can be retried and deleted', async ({ page }) => {
  const fileName = `e2e-${Date.now()}.mp3`;
  await login(page);
  await uploadBrokenAudio(page, fileName);

  const status = page.locator('.badge-lg');
  await expect(status).toHaveText('Fehlgeschlagen', { timeout: 30_000 });

  // Retry: the job is queued again (and fails again for the same reason)
  await page.getByRole('button', { name: 'Neu starten', exact: true }).click();
  await expect(page.getByText('Job neu gestartet')).toBeVisible();
  await expect(status).toHaveText('Fehlgeschlagen', { timeout: 30_000 });

  // Delete: the confirmation dialog can be dismissed with Escape ...
  await page.getByRole('button', { name: 'Löschen', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Job löschen?' });
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText(fileName);
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(page).toHaveURL(/\/jobs\/[0-9a-f-]{36}$/);

  // ... and confirming deletes the job and returns to the list
  await page.getByRole('button', { name: 'Löschen', exact: true }).click();
  await dialog.getByRole('button', { name: 'Endgültig löschen' }).click();
  await expect(page).toHaveURL(/\/jobs$/);
  await expect(page.getByText('Job gelöscht')).toBeVisible();
  await expect(page.getByText(fileName)).toHaveCount(0);
});
