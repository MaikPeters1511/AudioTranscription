import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangePasswordComponent } from './change-password.component';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from './auth.service';
import { translocoTesting } from '../i18n/transloco-testing';

describe('ChangePasswordComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ChangePasswordComponent, translocoTesting('en')],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render() {
    const fixture = TestBed.createComponent(ChangePasswordComponent);
    fixture.detectChanges();
    await fixture.whenStable(); // ngModel registers its controls asynchronously
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  function fill(el: HTMLElement, oldPassword: string, newPassword: string) {
    for (const [id, value] of [['old-password', oldPassword], ['new-password', newPassword]]) {
      const input = el.querySelector(`#${id}`) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
  }

  it('has labelled inputs with the right autocomplete hints', async () => {
    const { el } = await render();

    expect(el.querySelector('label[for=old-password]')).not.toBeNull();
    expect(el.querySelector('label[for=new-password]')).not.toBeNull();
    expect(el.querySelector('#old-password')?.getAttribute('autocomplete')).toBe('current-password');
    expect(el.querySelector('#new-password')?.getAttribute('autocomplete')).toBe('new-password');
  });

  it('sends old and new password, then ends the session and goes to the login', async () => {
    const { fixture, el } = await render();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const logout = vi.spyOn(TestBed.inject(AuthService), 'markLoggedOut');
    fill(el, 'old-secret', 'new-secret');
    (el.querySelector('button[type=submit]') as HTMLButtonElement).click();

    const req = http.expectOne('/api/auth/manage/info');
    expect(req.request.body).toEqual({ oldPassword: 'old-secret', newPassword: 'new-secret' });
    req.flush({});
    await fixture.whenStable();

    expect(logout).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { passwordChanged: '1' } });
    expect(fixture.componentInstance.oldPassword).toBe('');
    expect(fixture.componentInstance.newPassword).toBe('');
  });

  it('shows a clean error when the API rejects the change', async () => {
    const { fixture, el } = await render();
    fill(el, 'wrong', 'new-secret');
    (el.querySelector('button[type=submit]') as HTMLButtonElement).click();

    http.expectOne('/api/auth/manage/info').flush({ errors: { PasswordMismatch: ['Incorrect password.'] } }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('[role=alert]')?.textContent).toContain('could not be changed');
    expect(el.textContent).not.toContain('PasswordMismatch');
  });

  it('does not call the API while a field is empty', async () => {
    const { el } = await render();
    (el.querySelector('button[type=submit]') as HTMLButtonElement).click();

    http.expectNone('/api/auth/manage/info');
  });
});
