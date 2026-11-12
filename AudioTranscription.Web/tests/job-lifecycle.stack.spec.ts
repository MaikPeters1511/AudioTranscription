import { test, expect } from '@playwright/test';
import { adminEmail, adminPassword, login, stackConfigured, uploadBrokenAudio } from './support/stack';

/**
 * S09 against the real stack. The uploaded bytes are only a valid MP3 header, so the transcription itself fails.
 * That is intended: it gives a deterministic Failed job to retry and delete.
 */
test.skip(!stackConfigured, 'E2E_EMAIL and E2E_PASSWORD must be set (the initial user of the stack)');

test('failed job can be retried and deleted', async ({ page }) => {
  const fileName = `e2e-${Date.now()}.mp3`;
  await login(page, adminEmail!, adminPassword!);
  await expect(page).toHaveURL(/\/jobs$/);
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
