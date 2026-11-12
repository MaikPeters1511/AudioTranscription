import { test, expect, Browser, Page } from '@playwright/test';
import { adminEmail, adminPassword, login, newEmail, newPassword, stackConfigured, uploadBrokenAudio } from './support/stack';

/**
 * S31-F1 (#212) against the real stack: an admin creates two users, one of them shares a job with the other, the second
 * user sees it (read only) and loses it live when the share is revoked. Three browser contexts act at the same time.
 */
test.skip(!stackConfigured, 'E2E_EMAIL and E2E_PASSWORD must be set (the initial user of the stack)');

async function newSession(browser: Browser): Promise<Page> {
  return (await browser.newContext({ locale: 'de-DE' })).newPage();
}

async function createUser(admin: Page, email: string, password: string): Promise<void> {
  await admin.goto('/admin/users');
  await admin.getByRole('button', { name: 'Benutzer anlegen' }).click();
  const dialog = admin.getByRole('dialog');
  await dialog.getByLabel('E-Mail').fill(email);
  await dialog.getByLabel('Initiales Passwort').fill(password);
  await dialog.getByRole('button', { name: 'Anlegen' }).click();
  await expect(admin.getByRole('row').filter({ hasText: email })).toHaveCount(1);
}

test('admin creates users, a job is shared with one of them and taken away again', async ({ browser }) => {
  const annaEmail = newEmail('anna');
  const benEmail = newEmail('ben');
  const annaPassword = newPassword();
  const benPassword = newPassword();
  const fileName = `mehrbenutzer-${Date.now()}.mp3`;

  // 1. The admin creates both accounts in the user administration
  const admin = await newSession(browser);
  await login(admin, adminEmail!, adminPassword!, '/admin/users');
  await createUser(admin, annaEmail, annaPassword);
  await createUser(admin, benEmail, benPassword);

  // 2. Anna uploads a file and shares the job with Ben
  const anna = await newSession(browser);
  await login(anna, annaEmail, annaPassword);
  await expect(anna).toHaveURL(/\/jobs$/);
  await uploadBrokenAudio(anna, fileName);
  const jobUrl = anna.url();
  await anna.getByRole('button', { name: 'Teilen' }).click();
  const shareDialog = anna.getByRole('dialog', { name: 'Job teilen' });
  await shareDialog.getByLabel('E-Mail-Adresse des Benutzers').fill(benEmail);
  await shareDialog.getByRole('button', { name: 'Freigeben' }).click();
  await expect(shareDialog.getByText(benEmail)).toBeVisible();

  // 3. Ben sees the job in his list, marked as shared, and cannot delete or share it
  const ben = await newSession(browser);
  await login(ben, benEmail, benPassword);
  await expect(ben).toHaveURL(/\/jobs$/);
  await expect(ben.getByText(fileName).first()).toBeVisible();
  await ben.goto(jobUrl);
  await expect(ben.getByTestId('shared-by')).toContainText(annaEmail);
  await expect(ben.getByRole('button', { name: 'Löschen', exact: true })).toHaveCount(0);
  await expect(ben.getByRole('button', { name: 'Teilen' })).toHaveCount(0);

  // 4. Ben looks at his list; Anna takes the share away; the job disappears without a reload
  await ben.goto('/jobs');
  await expect(ben.getByText(fileName).first()).toBeVisible();
  await shareDialog.getByRole('button', { name: `Freigabe für ${benEmail} entziehen` }).click();
  await expect(shareDialog.getByText('Noch mit niemandem geteilt.')).toBeVisible();
  await expect(ben.getByText(fileName)).toHaveCount(0, { timeout: 15_000 });
  await ben.goto(jobUrl);
  await expect(ben.getByText(fileName)).toHaveCount(0);

  // 5. Anna still has the job, and removes it
  await anna.keyboard.press('Escape');
  await anna.goto(jobUrl);
  await expect(anna.getByText(fileName).first()).toBeVisible();
  await anna.getByRole('button', { name: 'Löschen', exact: true }).click();
  await anna.getByRole('dialog', { name: 'Job löschen?' }).getByRole('button', { name: 'Endgültig löschen' }).click();
  await expect(anna).toHaveURL(/\/jobs$/);

  // 6. An ordinary user has no access to the user administration
  await ben.goto('/admin/users');
  await expect(ben.getByRole('heading', { name: 'Benutzerverwaltung' })).toHaveCount(0);
});
