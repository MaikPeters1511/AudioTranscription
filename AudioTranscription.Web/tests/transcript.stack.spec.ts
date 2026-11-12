import { readFileSync } from 'node:fs';
import { test, expect } from '@playwright/test';
import { adminEmail, adminPassword, login, stackConfigured } from './support/stack';

/**
 * S31-T3/T4 against the real stack with a real Whisper model: a spoken sentence is uploaded, transcribed, copied and
 * downloaded, found by the search, and the hit opens the player at its time. The audio is generated outside the test
 * (espeak-ng in the CI), its path comes in E2E_SPEECH_WAV, and E2E_SPEECH_PHRASE is a phrase the model reliably hears.
 */
const speechFile = process.env['E2E_SPEECH_WAV'];
const phrase = process.env['E2E_SPEECH_PHRASE'] ?? 'fox';

test.skip(!stackConfigured, 'E2E_EMAIL and E2E_PASSWORD must be set (the initial user of the stack)');
test.skip(!speechFile, 'E2E_SPEECH_WAV must point to a short WAV file with the spoken sentence');

test.use({ permissions: ['clipboard-read', 'clipboard-write'] });

test('upload a spoken sentence, read, copy and download the transcript, find it by search', async ({ page }) => {
  test.setTimeout(5 * 60_000); // the first job may download the model

  await login(page, adminEmail!, adminPassword!, '/upload');
  // The sentence is English; naming the language keeps the model from guessing another one
  await page.getByLabel('Sprache der Aufnahme').selectOption('en');
  const fileName = `sprache-${Date.now()}.wav`;
  await page.setInputFiles('input[type="file"]', { name: fileName, mimeType: 'audio/wav', buffer: readFileSync(speechFile!) });
  await expect(page).toHaveURL(/\/jobs\/[0-9a-f-]{36}$/);
  const jobUrl = page.url();

  // The job runs and finishes; the transcript shows what was said
  await expect(page.locator('.badge-lg')).toHaveText('Abgeschlossen', { timeout: 4 * 60_000 });
  await expect(page.getByText(new RegExp(phrase, 'i')).first()).toBeVisible();

  // Copy
  await page.getByRole('button', { name: 'Kopieren', exact: true }).click();
  await expect(page.getByText('Transkript in die Zwischenablage kopiert')).toBeVisible();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toMatch(new RegExp(phrase, 'i'));

  // Download as .txt
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download .txt' }).click();
  const file = await download;
  expect(file.suggestedFilename()).toMatch(/\.txt$/);
  expect(readFileSync((await file.path())!, 'utf-8')).toMatch(new RegExp(phrase, 'i'));

  // Search finds the word, highlighted, and the hit opens the job at its time
  const word = phrase.split(' ').at(-1)!;
  // Earlier runs may have left transcripts with the same word: pick the hit of this run's file
  const hit = page.getByRole('link').filter({ hasText: fileName }).filter({ has: page.locator('mark') }).first();
  // The full-text index fills a moment after the job is saved (change tracking), so the search is repeated until it finds it
  await expect(async () => {
    await page.goto('/search');
    await page.getByLabel('Alle Transkripte durchsuchen').fill(word);
    await expect(hit).toBeVisible({ timeout: 4_000 });
  }).toPass({ timeout: 60_000 });
  await expect(hit.locator('mark').first()).toHaveText(new RegExp(word, 'i'));
  // The same word is also found in the whole transcript (no time); the hit of a segment links to its time
  const timedHit = page.locator('a[href*="t="]').filter({ hasText: fileName }).first();
  await expect(timedHit).toBeVisible();
  await timedHit.click();
  await expect(page).toHaveURL(new RegExp(`${jobUrl.split('/').at(-1)}\\?t=\\d+$`));
  await expect(page.locator('audio')).toBeVisible();

  // Clean up
  await page.goto(jobUrl);
  await page.getByRole('button', { name: 'Löschen', exact: true }).click();
  await page.getByRole('dialog', { name: 'Job löschen?' }).getByRole('button', { name: 'Endgültig löschen' }).click();
  await expect(page).toHaveURL(/\/jobs$/);
});
