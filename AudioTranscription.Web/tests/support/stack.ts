import { randomBytes } from 'node:crypto';
import { expect, Page } from '@playwright/test';

/**
 * Helpers for the `*.stack.spec.ts` flows (S31 stage 2): they run against a real, running stack (API, SQL Server and the
 * app), not against the faked API. The admin account is the initial user of the stack (Auth:InitialUser), given to the
 * tests as E2E_EMAIL and E2E_PASSWORD; accounts the tests create get throw-away passwords generated for the run.
 */
export const adminEmail = process.env['E2E_EMAIL'];
export const adminPassword = process.env['E2E_PASSWORD'];

export const stackConfigured = !!adminEmail && !!adminPassword;

/** A password that satisfies the rules (12+ characters with upper and lower case, digit and special character). */
export const newPassword = () => `E2e-${randomBytes(12).toString('hex')}-Aa1!`;

/** A unique address per run, so repeated runs against the same database do not collide. */
export const newEmail = (name: string) => `${name}-${Date.now().toString(36)}${randomBytes(2).toString('hex')}@example.com`;

export async function login(page: Page, email: string, password: string, target = '/jobs'): Promise<void> {
  await page.goto(target);
  await expect(page).toHaveURL(/\/login/);
  await page.getByLabel('E-Mail').fill(email);
  await page.getByLabel('Passwort').fill(password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  // The session exists once the app has left the login page; navigating away earlier would cancel the sign-in
  await expect(page).not.toHaveURL(/\/login/);
}

export async function logout(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Abmelden' }).click();
  await expect(page).toHaveURL(/\/login/);
}

/** Only a valid MP3 header: the job is created and then fails in the transcription, which gives a deterministic job to share. */
export async function uploadBrokenAudio(page: Page, fileName: string): Promise<void> {
  await page.goto('/upload');
  await page.setInputFiles('input[type="file"]', {
    name: fileName,
    mimeType: 'audio/mpeg',
    buffer: Buffer.concat([Buffer.from('ID3'), Buffer.alloc(2048)]),
  });
  await expect(page).toHaveURL(/\/jobs\/[0-9a-f-]{36}$/);
}
