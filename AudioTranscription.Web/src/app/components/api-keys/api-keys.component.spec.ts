import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ApiKeysComponent } from './api-keys.component';
import { ApiKeyService } from '../../services/api-key.service';
import { ToastService } from '../../services/toast.service';
import { translocoTesting } from '../../i18n/transloco-testing';
import { ApiKey, CreatedApiKey } from '../../models/automation.model';

const apiKey = (over: Partial<ApiKey> = {}): ApiKey => ({
  id: 'k1',
  name: 'CI pipeline',
  prefix: 'ABCDEFGH',
  scopes: ['jobs:read'],
  createdAt: '2026-11-10T12:00:00Z',
  expiresAt: null,
  lastUsedAt: null,
  ...over,
});

describe('ApiKeysComponent (S28)', () => {
  const keys = signal<ApiKey[]>([]);
  const service = { keys, load: vi.fn(), create: vi.fn(), delete: vi.fn() };
  const toast = { error: vi.fn(), success: vi.fn() };
  const writeText = vi.fn();

  async function render(initial: ApiKey[] = [apiKey()]) {
    keys.set(initial);
    service.load.mockReset().mockResolvedValue(undefined);
    service.create.mockReset().mockResolvedValue({ ...apiKey({ id: 'k2', name: 'Script' }), key: 'at_ZZZZZZZZ_the-secret' } satisfies CreatedApiKey);
    service.delete.mockReset().mockResolvedValue(undefined);
    toast.error.mockReset();
    toast.success.mockReset();
    writeText.mockReset().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    TestBed.configureTestingModule({
      imports: [ApiKeysComponent, translocoTesting('en')],
      providers: [
        { provide: ApiKeyService, useValue: service },
        { provide: ToastService, useValue: toast },
      ],
    });
    const fixture = TestBed.createComponent(ApiKeysComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  async function settle(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }) {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function type(fixture: Parameters<typeof settle>[0], el: HTMLElement, selector: string, value: string) {
    const input = el.querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle(fixture);
  }

  it('lists the keys with prefix and scopes, never a secret', async () => {
    const { el } = await render([apiKey({ scopes: ['jobs:read', 'jobs:write'], lastUsedAt: '2026-11-11T08:00:00Z' })]);

    expect(el.textContent).toContain('CI pipeline');
    expect(el.textContent).toContain('at_ABCDEFGH_');
    expect(el.textContent).toContain('jobs:read');
    expect(el.textContent).toContain('jobs:write');
    expect(el.querySelector('[data-testid="api-key-secret"]')).toBeNull();
  });

  it('says so when a key was never used and has no expiry', async () => {
    const { el } = await render();

    expect(el.textContent).toContain('Never used');
    expect(el.textContent).toContain('No expiry');
  });

  it('shows the empty state', async () => {
    const { el } = await render([]);

    expect(el.textContent).toContain('No API keys yet.');
  });

  it('disables creating until a name and a scope are given', async () => {
    const { fixture, el } = await render();
    const create = el.querySelector('[data-testid="api-key-create"]') as HTMLButtonElement;
    expect(create.disabled).toBe(true);

    await type(fixture, el, '[data-testid="api-key-name"]', 'Script');
    expect(create.disabled).toBe(false);

    const read = el.querySelector('[data-testid="scope-jobs:read"]') as HTMLInputElement;
    read.click();
    await settle(fixture);
    expect(create.disabled).toBe(true);
  });

  it('creates a key with the chosen scopes and expiry and shows the key once', async () => {
    const before = Date.now();
    const { fixture, el } = await render();
    await type(fixture, el, '[data-testid="api-key-name"]', 'Script');
    (el.querySelector('[data-testid="scope-jobs:write"]') as HTMLInputElement).click();
    await settle(fixture);

    (el.querySelector('[data-testid="api-key-create"]') as HTMLButtonElement).click();
    await settle(fixture);

    expect(service.create).toHaveBeenCalledTimes(1);
    const [name, scopes, expiresAt] = service.create.mock.calls[0];
    expect(name).toBe('Script');
    expect(scopes).toEqual(['jobs:read', 'jobs:write']);
    const days = (new Date(expiresAt).getTime() - before) / 86_400_000;
    expect(days).toBeGreaterThan(89.9);
    expect(days).toBeLessThan(90.1);

    const shown = el.querySelector('[data-testid="api-key-secret"]') as HTMLInputElement;
    expect(shown.value).toBe('at_ZZZZZZZZ_the-secret');
    expect(shown.readOnly).toBe(true);
    expect(el.querySelector('[data-testid="api-key-once"]')?.textContent).toContain('only time');
    expect(shown.getAttribute('aria-label')).toBe('New API key');
  });

  it('creates a key without expiry when "never" is chosen', async () => {
    const { fixture, el } = await render();
    await type(fixture, el, '[data-testid="api-key-name"]', 'Script');
    const expiry = el.querySelector('[data-testid="api-key-expiry"]') as HTMLSelectElement;
    expiry.value = '';
    expiry.dispatchEvent(new Event('change'));
    await settle(fixture);

    (el.querySelector('[data-testid="api-key-create"]') as HTMLButtonElement).click();
    await settle(fixture);

    expect(service.create.mock.calls[0][2]).toBeNull();
  });

  it('copies the key and confirms it', async () => {
    const { fixture, el } = await render();
    await type(fixture, el, '[data-testid="api-key-name"]', 'Script');
    (el.querySelector('[data-testid="api-key-create"]') as HTMLButtonElement).click();
    await settle(fixture);

    (el.querySelector('[data-testid="api-key-copy"]') as HTMLButtonElement).click();
    await settle(fixture);

    expect(writeText).toHaveBeenCalledWith('at_ZZZZZZZZ_the-secret');
    expect(toast.success).toHaveBeenCalled();
  });

  it('forgets the key when it is dismissed', async () => {
    const { fixture, el } = await render();
    await type(fixture, el, '[data-testid="api-key-name"]', 'Script');
    (el.querySelector('[data-testid="api-key-create"]') as HTMLButtonElement).click();
    await settle(fixture);

    (el.querySelector('[data-testid="api-key-dismiss"]') as HTMLButtonElement).click();
    await settle(fixture);

    expect(el.querySelector('[data-testid="api-key-secret"]')).toBeNull();
    expect(el.textContent).not.toContain('the-secret');
  });

  it('shows a clean message when the limit is reached and never the server text', async () => {
    const { fixture, el } = await render();
    service.create.mockRejectedValue(new HttpErrorResponse({ status: 409, error: 'internal detail 10.0.0.1' }));
    await type(fixture, el, '[data-testid="api-key-name"]', 'Script');

    (el.querySelector('[data-testid="api-key-create"]') as HTMLButtonElement).click();
    await settle(fixture);

    const error = el.querySelector('[data-testid="api-key-error"]');
    expect(error?.textContent).toContain('limit');
    expect(error?.getAttribute('role')).toBe('alert');
    expect(el.textContent).not.toContain('internal detail');
  });

  it('asks before revoking and then deletes', async () => {
    const { fixture, el } = await render();

    (el.querySelector('[data-action="delete-api-key"]') as HTMLButtonElement).click();
    await settle(fixture);
    expect(service.delete).not.toHaveBeenCalled();
    expect(el.textContent).toContain('Revoke this key?');

    (el.querySelector('[data-action="confirm-delete-api-key"]') as HTMLButtonElement).click();
    await settle(fixture);

    expect(service.delete).toHaveBeenCalledWith('k1');
  });

  it('labels the revoke button with the key name for screen readers', async () => {
    const { el } = await render();

    const button = el.querySelector('[data-action="delete-api-key"]') as HTMLButtonElement;
    expect(button.getAttribute('aria-label')).toBe('Revoke API key CI pipeline');
  });

  it('reports a failed load', async () => {
    keys.set([]);
    service.load.mockReset().mockRejectedValue(new Error('x'));
    TestBed.configureTestingModule({
      imports: [ApiKeysComponent, translocoTesting('en')],
      providers: [{ provide: ApiKeyService, useValue: service }, { provide: ToastService, useValue: toast }],
    });
    const fixture = TestBed.createComponent(ApiKeysComponent);
    await settle(fixture);

    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')?.textContent).toContain('could not be loaded');
  });
});
