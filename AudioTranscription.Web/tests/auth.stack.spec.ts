import { test, expect } from '@playwright/test';
import { adminEmail, adminPassword, login, logout, stackConfigured } from './support/stack';

/** S31-T2 against the real stack: sign in and out, a wrong password, and a protected page without a session. */
test.skip(!stackConfigured, 'E2E_EMAIL and E2E_PASSWORD must be set (the initial user of the stack)');

test.describe('login and logout', () => {
  test('a protected page leads to the login and back after signing in', async ({ page }) => {
    await login(page, adminEmail!, adminPassword!, '/search');

    await expect(page).toHaveURL(/\/search$/);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  });

  test('a wrong password shows a clean message and does not sign in', async ({ page }) => {
    await page.goto('/jobs');
    await page.getByLabel('E-Mail').fill(adminEmail!);
    await page.getByLabel('Passwort').fill('falsch-falsch-1!Aa');
    await page.getByRole('button', { name: 'Anmelden' }).click();

    const alert = page.getByRole('alert');
    await expect(alert).toContainText('Anmeldung fehlgeschlagen');
    await expect(alert).not.toContainText(/exception|stack|sql|lock/i);
    await expect(page).toHaveURL(/\/login/);
  });

  test('signing out ends the session', async ({ page }) => {
    await login(page, adminEmail!, adminPassword!);
    await expect(page).toHaveURL(/\/jobs$/);

    await logout(page);
    await page.goto('/jobs');

    await expect(page).toHaveURL(/\/login/);
  });
});
