import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { UserAdminComponent } from './user-admin.component';
import { AuthService } from '../../auth/auth.service';
import { ToastService } from '../../services/toast.service';
import { translocoTesting } from '../../i18n/transloco-testing';

const alice = { id: '1', email: 'alice@example.com', roles: ['Admin'], isActive: true };
const bob = { id: '2', email: 'bob@example.com', roles: [] as string[], isActive: true };
const carol = { id: '3', email: 'carol@example.com', roles: [] as string[], isActive: false };

describe('UserAdminComponent', () => {
  let http: HttpTestingController;
  const toast = { success: vi.fn(), error: vi.fn() };

  beforeEach(() => {
    toast.success.mockReset();
    toast.error.mockReset();
    TestBed.configureTestingModule({
      imports: [UserAdminComponent, translocoTesting('en')],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toast },
        { provide: AuthService, useValue: { user: () => ({ email: alice.email, roles: ['Admin'] }) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(users = [alice, bob, carol]) {
    const fixture = TestBed.createComponent(UserAdminComponent);
    fixture.detectChanges();
    http.expectOne((r) => r.url === '/api/admin/users').flush({ items: users, total: users.length });
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  async function settle(fixture: Awaited<ReturnType<typeof render>>['fixture']) {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('renders an accessible table with one row per user', async () => {
    const { el } = await render();

    expect(el.querySelector('table caption')).not.toBeNull();
    expect(el.querySelectorAll('tbody tr').length).toBe(3);
    expect(el.querySelectorAll('thead th[scope=col]').length).toBeGreaterThan(0);
    expect(el.textContent).toContain('3 users');
  });

  it('labels every admin toggle with the user it belongs to', async () => {
    const { el } = await render();

    const toggle = el.querySelector('input[type=checkbox][aria-label="Admin role for bob@example.com"]');
    expect(toggle).not.toBeNull();
  });

  it('does not offer to deactivate the signed-in user', async () => {
    const { el } = await render();

    expect(el.querySelector('[aria-label="Deactivate alice@example.com"]')).toBeNull();
    expect(el.querySelector('[aria-label="Deactivate bob@example.com"]')).not.toBeNull();
    expect(el.querySelector('[aria-label="Activate carol@example.com"]')).not.toBeNull();
  });

  it('shows a retry option when loading fails', async () => {
    const fixture = TestBed.createComponent(UserAdminComponent);
    fixture.detectChanges();
    http.expectOne((r) => r.url === '/api/admin/users').flush(null, { status: 500, statusText: 'Server Error' });
    await settle(fixture);

    expect(fixture.nativeElement.querySelector('[role=alert]')?.textContent).toContain('could not be loaded');
    expect(fixture.nativeElement.textContent).not.toContain('Server Error');
  });

  it('creates a user, closes the dialog and reloads the list', async () => {
    const { fixture, el } = await render();
    (el.querySelector('button.btn-primary') as HTMLButtonElement).click();
    fixture.detectChanges();
    await settle(fixture);

    const dialog = el.querySelector('[role=dialog]')!;
    expect(dialog.getAttribute('aria-modal')).toBe('true');
    const email = dialog.querySelector('#new-user-email') as HTMLInputElement;
    const password = dialog.querySelector('#new-user-password') as HTMLInputElement;
    email.value = 'dave@example.com';
    email.dispatchEvent(new Event('input'));
    password.value = 'Secret-1!';
    password.dispatchEvent(new Event('input'));
    (dialog.querySelector('input[name=makeAdmin]') as HTMLInputElement).click();
    fixture.detectChanges();
    (dialog.querySelector('button[type=submit]') as HTMLButtonElement).click();

    const create = http.expectOne('/api/admin/users');
    expect(create.request.body).toEqual({ email: 'dave@example.com', password: 'Secret-1!', roles: ['Admin'] });
    create.flush({ id: '4', email: 'dave@example.com', roles: ['Admin'], isActive: true }, { status: 201, statusText: 'Created' });
    await settle(fixture);
    http.expectOne((r) => r.url === '/api/admin/users').flush({ items: [alice], total: 1 });
    await settle(fixture);

    expect(el.querySelector('[role=dialog]')).toBeNull();
    expect(toast.success).toHaveBeenCalled();
  });

  it('shows a clean message for a duplicate e-mail address', async () => {
    const { fixture, el } = await render();
    (el.querySelector('button.btn-primary') as HTMLButtonElement).click();
    fixture.detectChanges();
    await settle(fixture);
    const dialog = el.querySelector('[role=dialog]')!;
    const email = dialog.querySelector('#new-user-email') as HTMLInputElement;
    email.value = 'bob@example.com';
    email.dispatchEvent(new Event('input'));
    (dialog.querySelector('button[type=submit]') as HTMLButtonElement).click();

    http.expectOne('/api/admin/users').flush('internal details', { status: 409, statusText: 'Conflict' });
    await settle(fixture);

    expect(dialog.querySelector('[role=alert]')?.textContent).toContain('already exists');
    expect(el.textContent).not.toContain('internal details');
  });

  it('closes the create dialog with Escape', async () => {
    const { fixture, el } = await render();
    (el.querySelector('button.btn-primary') as HTMLButtonElement).click();
    fixture.detectChanges();
    await settle(fixture);

    el.querySelector('[role=dialog]')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();

    expect(el.querySelector('[role=dialog]')).toBeNull();
  });

  it('asks for confirmation before deactivating', async () => {
    const { fixture, el } = await render();
    (el.querySelector('[aria-label="Deactivate bob@example.com"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    const dialog = el.querySelector('[role=alertdialog]')!;
    expect(dialog.textContent).toContain('bob@example.com');
    http.expectNone('/api/admin/users/2/deactivate');

    (dialog.querySelector('button.btn-error') as HTMLButtonElement).click();
    http.expectOne('/api/admin/users/2/deactivate').flush(null, { status: 204, statusText: 'No Content' });
    await settle(fixture);
    http.expectOne((r) => r.url === '/api/admin/users').flush({ items: [alice, { ...bob, isActive: false }], total: 2 });
    await settle(fixture);

    expect(toast.success).toHaveBeenCalled();
    expect(el.querySelector('[role=alertdialog]')).toBeNull();
  });

  it('toggles the admin role and reports the last-admin conflict', async () => {
    const { fixture, el } = await render();
    const toggle = el.querySelector('input[aria-label="Admin role for alice@example.com"]') as HTMLInputElement;
    toggle.checked = false;
    toggle.dispatchEvent(new Event('change'));

    const req = http.expectOne('/api/admin/users/1/roles');
    expect(req.request.body).toEqual({ roles: [] });
    req.flush('The last active admin cannot be demoted.', { status: 409, statusText: 'Conflict' });
    await settle(fixture);
    http.expectOne((r) => r.url === '/api/admin/users').flush({ items: [alice, bob, carol], total: 3 });
    await settle(fixture);

    expect(toast.error).toHaveBeenCalledWith('The last active administrator cannot be demoted.');
    const reloaded = el.querySelector('input[aria-label="Admin role for alice@example.com"]') as HTMLInputElement;
    expect(reloaded.checked).toBe(true);
  });

  it('reactivates a deactivated user', async () => {
    const { fixture, el } = await render();
    (el.querySelector('[aria-label="Activate carol@example.com"]') as HTMLButtonElement).click();

    http.expectOne('/api/admin/users/3/activate').flush(null, { status: 204, statusText: 'No Content' });
    await settle(fixture);
    http.expectOne((r) => r.url === '/api/admin/users').flush({ items: [alice, bob, { ...carol, isActive: true }], total: 3 });
    await settle(fixture);

    expect(toast.success).toHaveBeenCalled();
  });
});
