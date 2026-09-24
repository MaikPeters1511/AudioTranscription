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
