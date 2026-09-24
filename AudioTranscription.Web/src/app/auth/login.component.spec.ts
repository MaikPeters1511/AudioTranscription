import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { LoginComponent } from './login.component';
import { AuthService } from './auth.service';
import { translocoTesting } from '../i18n/transloco-testing';

describe('LoginComponent', () => {
  const login = vi.fn<(email: string, password: string) => Promise<boolean>>();

  function setup(returnUrl: string | null = null, language: 'de' | 'en' = 'de') {
    login.mockReset();
    TestBed.configureTestingModule({
      imports: [LoginComponent, translocoTesting(language)],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { login } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { queryParamMap: convertToParamMap(returnUrl ? { returnUrl } : {}) },
          },
        },
      ],
    });
    const router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    return { fixture, router, el: fixture.nativeElement as HTMLElement };
  }

  // Template-driven forms register their controls asynchronously
  async function fillAndSubmit(
    fixture: { whenStable(): Promise<unknown> },
    el: HTMLElement,
    email: string,
    password: string,
  ) {
    await fixture.whenStable();
    const emailInput = el.querySelector<HTMLInputElement>('input[type=email]')!;
    const passwordInput = el.querySelector<HTMLInputElement>('input[type=password]')!;
    emailInput.value = email;
    emailInput.dispatchEvent(new Event('input'));
    passwordInput.value = password;
    passwordInput.dispatchEvent(new Event('input'));
    el.querySelector('form')!.dispatchEvent(new Event('submit'));
  }

  it('renders labelled fields in the active language', () => {
    const { el } = setup(null, 'en');

    const email = el.querySelector<HTMLInputElement>('input[type=email]')!;
    const password = el.querySelector<HTMLInputElement>('input[type=password]')!;
    expect(el.querySelector(`label[for="${email.id}"]`)?.textContent).toContain('Email');
    expect(el.querySelector(`label[for="${password.id}"]`)?.textContent).toContain('Password');
    expect(email.autocomplete).toBe('username');
    expect(password.autocomplete).toBe('current-password');
    expect(el.querySelector('h1')?.textContent).toContain('Sign in');
    expect(TestBed.inject(Title).getTitle()).toBe('Sign in · Transcription');
  });

  it('signs in and returns to the requested page', async () => {
    const { fixture, router, el } = setup('/jobs/42');
    login.mockResolvedValue(true);

    await fillAndSubmit(fixture, el, 'alice@example.com', 'secret');
    await fixture.whenStable();

    expect(login).toHaveBeenCalledWith('alice@example.com', 'secret');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/jobs/42');
  });

  it('never redirects to external addresses after login', async () => {
    const { fixture, router, el } = setup('https://evil.example.org');
    login.mockResolvedValue(true);

    await fillAndSubmit(fixture, el, 'alice@example.com', 'secret');
    await fixture.whenStable();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('announces a failed login', async () => {
    const { fixture, router, el } = setup();
    login.mockResolvedValue(false);

    await fillAndSubmit(fixture, el, 'alice@example.com', 'wrong');
    await fixture.whenStable();
    fixture.detectChanges();

    const alert = el.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Anmeldung fehlgeschlagen');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('does not submit without email and password', async () => {
    const { fixture, el } = setup();

    await fillAndSubmit(fixture, el, '', '');
    await fixture.whenStable();

    expect(login).not.toHaveBeenCalled();
  });
});
