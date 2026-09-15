import { test, expect } from '@playwright/test';

test.describe('Audio Transcription App', () => {

  test('should load the home page and have correct title', async ({ page }) => {
    // Navigate to the app (assuming it runs on localhost:4200 or similar during test)
    await page.goto('/');

    // Check navbar title
    await expect(page.locator('.navbar .btn-ghost').first()).toContainText('Transkription');

    // Check main heading on Jobs page (default route)
    await expect(page.locator('h1')).toContainText('Transkriptionen');
  });

  test('should navigate to upload page and show dropzone', async ({ page }) => {
    await page.goto('/');

    // Click "Neue Datei" / "Upload"
    await page.getByRole('link', { name: 'Upload' }).first().click();

    // Verify upload page is loaded
    await expect(page).toHaveURL(/.*upload/);
    await expect(page.locator('h1')).toContainText('Audio hochladen');

    // Verify dropzone exists
    const dropzone = page.locator('.border-dashed');
    await expect(dropzone).toBeVisible();
    await expect(dropzone).toContainText('Audiodatei auswählen');
  });

  test('should show error when trying to upload invalid file type', async ({ page }) => {
    await page.goto('/upload');

    // Since we cannot easily trigger the file chooser with invalid files if accept is strict,
    // we can test the UI state or simulate file selection.
    
    // Create a dummy text file
    const fileContent = 'this is not an audio file';
    const buffer = Buffer.from(fileContent);

    // Set file to input
    await page.setInputFiles('input[type="file"]', {
      name: 'test.txt',
      mimeType: 'text/plain',
      buffer
    });

    // Verify error message appears
    await expect(page.locator('.text-error')).toBeVisible();
    await expect(page.locator('.text-error')).toContainText('ungültig');
  });

});
