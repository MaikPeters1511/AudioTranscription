import { randomBytes } from 'node:crypto';
import { test, expect, Page } from '@playwright/test';
import { MockApi } from './support/mock-api';

/**
 * S33: record while the browser is offline, keep the take on the device, upload it when the connection is back.
 * Against the faked API (see support/mock-api.ts); the microphone is faked in the page, since a CI browser has none.
 * The credentials are throw-away values that only exist for this run.
 */
const email = 'e2e@example.com';
const password = randomBytes(16).toString('hex');

let api: MockApi;

test.beforeEach(async ({ page }) => {
  // A tiny stand-in for the microphone and MediaRecorder: one section of audio-like bytes per interval
  await page.addInitScript(() => {
    class FakeMediaRecorder {
      static isTypeSupported = () => true;
      ondataavailable: ((event: { data: Blob }) => void) | null = null;
      onstop: (() => void) | null = null;
      onerror: (() => void) | null = null;
      private timer?: ReturnType<typeof setInterval>;
      constructor(
        _stream: unknown,
        private options?: { mimeType?: string },
      ) {}
      private emit() {
        this.ondataavailable?.({ data: new Blob([new Uint8Array(2048)], { type: this.options?.mimeType ?? 'audio/webm' }) });
      }
      start(timeslice = 1000) {
        this.timer = setInterval(() => this.emit(), Math.min(timeslice, 200));
      }
      stop() {
        clearInterval(this.timer);
        this.emit();
        this.onstop?.();
      }
    }
    (window as unknown as { MediaRecorder: unknown }).MediaRecorder = FakeMediaRecorder;
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { getUserMedia: async () => ({ getTracks: () => [{ stop() {} }] }) },
    });
  });

  api = new MockApi(page, { email, password });
  await api.install();
});

async function signInAndOpenUpload(page: Page) {
  await page.goto('/upload');
  await expect(page).toHaveURL(/\/login/);
  await page.getByLabel('E-Mail').fill(email);
  await page.getByLabel('Passwort').fill(password);
  await page.getByRole('button', { name: 'Anmelden' }).click();
  await expect(page).toHaveURL(/\/upload$/);
  await page.getByRole('tab', { name: 'Aufnehmen' }).click();
}

test.describe('offline recording', () => {
  test('a take recorded offline is saved on the device and uploaded once the connection is back', async ({ page, context }) => {
    await signInAndOpenUpload(page);

    await context.setOffline(true);
    await expect(page.getByRole('status').filter({ hasText: 'Du bist offline' })).toBeVisible();

    await page.getByRole('button', { name: 'Aufnahme starten' }).click();
    await page.waitForTimeout(500); // a few sections reach the local store
    await page.getByRole('button', { name: 'Aufnahme stoppen' }).click();

    // Offline there is no "transcribe" - only keeping the take for later
    await expect(page.getByRole('button', { name: 'Transkribieren' })).toHaveCount(0);
    await page.getByRole('button', { name: 'Für späteren Upload speichern' }).click();

    const waiting = page.getByRole('region', { name: 'Wartet auf Upload' });
    await expect(waiting).toBeVisible();
    await expect(waiting.getByRole('listitem')).toHaveCount(1);
    await expect(waiting).toContainText('Wartet auf Verbindung');
    expect(api.requests.filter((r) => r === 'POST /api/audio-jobs')).toHaveLength(0);

    // The connection returns: the take is uploaded without any click, and removed from the list
    await context.setOffline(false);
    await expect(page.getByRole('status').filter({ hasText: 'Du bist offline' })).toHaveCount(0);
    await expect.poll(() => api.requests.filter((r) => r === 'POST /api/audio-jobs').length).toBe(1);
    await expect(waiting).toHaveCount(0);
    expect(api.jobs.size).toBe(1);
  });

  test('a waiting recording can be deleted after a confirmation', async ({ page, context }) => {
    await signInAndOpenUpload(page);
    await context.setOffline(true);
    await page.getByRole('button', { name: 'Aufnahme starten' }).click();
    await page.waitForTimeout(300);
    await page.getByRole('button', { name: 'Aufnahme stoppen' }).click();
    await page.getByRole('button', { name: 'Für späteren Upload speichern' }).click();
    const waiting = page.getByRole('region', { name: 'Wartet auf Upload' });

    await waiting.getByRole('button', { name: /^Aufnahme vom .* löschen$/ }).click();
    await waiting.getByRole('button', { name: /endgültig löschen$/ }).click();

    await expect(waiting).toHaveCount(0);
  });

  test('signing out with an unsent recording asks first and keeps everything on "stay signed in"', async ({ page, context }) => {
    await signInAndOpenUpload(page);
    await context.setOffline(true);
    await page.getByRole('button', { name: 'Aufnahme starten' }).click();
    await page.waitForTimeout(300);
    await page.getByRole('button', { name: 'Aufnahme stoppen' }).click();
    await page.getByRole('button', { name: 'Für späteren Upload speichern' }).click();
    await expect(page.getByRole('region', { name: 'Wartet auf Upload' })).toBeVisible();

    await page.getByRole('button', { name: 'Abmelden' }).click();
    const dialog = page.getByRole('alertdialog', { name: 'Nicht hochgeladene Aufnahmen' });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Angemeldet bleiben' })).toBeFocused();

    await page.keyboard.press('Escape');
    await expect(dialog).toHaveCount(0);
    await expect(page.getByRole('region', { name: 'Wartet auf Upload' })).toBeVisible();

    await page.getByRole('button', { name: 'Abmelden' }).click();
    await page.getByRole('button', { name: 'Löschen und abmelden' }).click();
    await expect(page).toHaveURL(/\/login/);
  });
});
