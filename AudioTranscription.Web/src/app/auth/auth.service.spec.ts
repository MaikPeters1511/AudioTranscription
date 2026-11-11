import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('starts with an unknown session', () => {
    expect(service.user()).toBeUndefined();
    expect(service.isLoggedIn()).toBe(false);
  });

  it('loads the current user', async () => {
    const result = service.loadCurrentUser();
    http.expectOne('/api/auth/me').flush({ email: 'alice@example.com' });

    expect(await result).toBe(true);
    expect(service.user()).toEqual({ email: 'alice@example.com' });
    expect(service.isLoggedIn()).toBe(true);
  });

  it('treats 401 from /me as signed out', async () => {
    const result = service.loadCurrentUser();
    http.expectOne('/api/auth/me').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(await result).toBe(false);
    expect(service.user()).toBeNull();
  });

  it('logs in with a cookie session and loads the user', async () => {
    const result = service.login('alice@example.com', 'secret');

    const login = http.expectOne('/api/auth/login?useCookies=true');
    expect(login.request.method).toBe('POST');
    expect(login.request.body).toEqual({ email: 'alice@example.com', password: 'secret' });
    login.flush(null);
    await Promise.resolve();
    http.expectOne('/api/auth/me').flush({ email: 'alice@example.com' });

    expect(await result).toBe(true);
    expect(service.isLoggedIn()).toBe(true);
  });

  it('reports a failed login', async () => {
    const result = service.login('alice@example.com', 'wrong');
    http
      .expectOne('/api/auth/login?useCookies=true')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(await result).toBe(false);
    expect(service.isLoggedIn()).toBe(false);
  });

  it('logs out and forgets the user', async () => {
    service.markLoggedOut();
    const load = service.loadCurrentUser();
    http.expectOne('/api/auth/me').flush({ email: 'alice@example.com' });
    await load;

    const result = service.logout();
    const logout = http.expectOne('/api/auth/logout');
    expect(logout.request.method).toBe('POST');
    logout.flush(null, { status: 204, statusText: 'No Content' });
    await result;

    expect(service.user()).toBeNull();
  });
});

/** S33 / ADR 0012: the app opens without a connection for recording, but only for a user known from before. */
describe('AuthService offline start', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    // Node's own localStorage is unusable without a file; an in-memory one behaves like the browser's
    const data = new Map<string, string>();
    vi.stubGlobal('localStorage', {
      getItem: (key: string) => data.get(key) ?? null,
      setItem: (key: string, value: string) => void data.set(key, value),
      removeItem: (key: string) => void data.delete(key),
      clear: () => data.clear(),
    });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.unstubAllGlobals();
  });

  async function signIn(email = 'alice@example.com') {
    const load = service.loadCurrentUser();
    http.expectOne('/api/auth/me').flush({ email });
    await load;
  }

  it('remembers only the e-mail address of the last signed-in user', async () => {
    await signIn();

    expect(localStorage.getItem('lastUserEmail')).toBe('alice@example.com');
    expect(service.unverified()).toBe(false);
  });

  it('keeps the remembered user when the API cannot be reached, and marks the session as unverified', async () => {
    localStorage.setItem('lastUserEmail', 'alice@example.com');

    const load = service.loadCurrentUser();
    http.expectOne('/api/auth/me').error(new ProgressEvent('error'));

    expect(await load).toBe(true);
    expect(service.user()).toEqual({ email: 'alice@example.com' });
    expect(service.unverified()).toBe(true);
  });

  it('stays signed out when the API cannot be reached and nobody was signed in before', async () => {
    const load = service.loadCurrentUser();
    http.expectOne('/api/auth/me').error(new ProgressEvent('error'));

    expect(await load).toBe(false);
    expect(service.user()).toBeNull();
  });

  it('forgets the remembered user when the session is rejected', async () => {
    localStorage.setItem('lastUserEmail', 'alice@example.com');

    const load = service.loadCurrentUser();
    http.expectOne('/api/auth/me').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(await load).toBe(false);
    expect(localStorage.getItem('lastUserEmail')).toBeNull();
  });

  it('forgets the remembered user on logout, even when the logout request fails', async () => {
    await signIn();

    const logout = service.logout().catch(() => undefined);
    http.expectOne('/api/auth/logout').error(new ProgressEvent('error'));
    await logout;

    expect(localStorage.getItem('lastUserEmail')).toBeNull();
    expect(service.user()).toBeNull();
  });
});
