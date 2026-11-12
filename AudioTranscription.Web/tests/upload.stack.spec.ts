import { test, expect } from '@playwright/test';
import { adminEmail, adminPassword, login, stackConfigured } from './support/stack';

/** The pages around the upload against the real stack: the app loads, the upload page shows its dropzone, a wrong file type is refused. */
test.skip(!stackConfigured, 'E2E_EMAIL and E2E_PASSWORD must be set (the initial user of the stack)');

test.beforeEach(async ({ page }) => {
  await login(page, adminEmail!, adminPassword!);
  await expect(page).toHaveURL(/\/jobs$/);
});

test('the job list loads with its title', async ({ page }) => {
  await expect(page.locator('.navbar .btn-ghost').first()).toContainText('Transkription');
  await expect(page.locator('h1')).toContainText('Transkriptionen');
});

test('the upload page shows the dropzone', async ({ page }) => {
  await page.getByRole('link', { name: 'Upload' }).first().click();

  await expect(page).toHaveURL(/.*upload/);
  await expect(page.locator('h1')).toContainText('Audio hochladen');
  await expect(page.locator('.border-dashed')).toContainText('Audiodatei hierher ziehen');
});

test('a file of the wrong type is refused with a clear message', async ({ page }) => {
  await page.goto('/upload');

  await page.setInputFiles('input[type="file"]', { name: 'test.txt', mimeType: 'text/plain', buffer: Buffer.from('this is not an audio file') });

  await expect(page.getByText('Ungültiges Dateiformat')).toBeVisible();
});
