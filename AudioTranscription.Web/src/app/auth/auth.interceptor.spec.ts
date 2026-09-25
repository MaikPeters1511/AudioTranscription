import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let router: Router;
  const markLoggedOut = vi.fn();

  beforeEach(() => {
    markLoggedOut.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { markLoggedOut } },
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    vi.spyOn(router, 'url', 'get').mockReturnValue('/jobs/42');
  });

  it('sends the user to the login page when the session expired', () => {
    http.get('/api/audio-jobs').subscribe({ error: () => undefined });
    controller
      .expectOne('/api/audio-jobs')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(markLoggedOut).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/login'], {
      queryParams: { returnUrl: '/jobs/42' },
    });
  });

  it('leaves 401 from auth endpoints to the caller', () => {
    http.post('/api/auth/login?useCookies=true', {}).subscribe({ error: () => undefined });
    controller
      .expectOne('/api/auth/login?useCookies=true')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('ignores other errors', () => {
    http.get('/api/audio-jobs').subscribe({ error: () => undefined });
    controller
      .expectOne('/api/audio-jobs')
      .flush(null, { status: 500, statusText: 'Server Error' });

    expect(router.navigate).not.toHaveBeenCalled();
  });
});
