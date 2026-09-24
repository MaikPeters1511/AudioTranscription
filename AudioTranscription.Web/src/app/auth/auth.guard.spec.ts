import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  provideRouter,
  Router,
  RouterStateSnapshot,
  UrlTree,
} from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';

describe('authGuard', () => {
  const loadCurrentUser = vi.fn<() => Promise<boolean>>();
  const isLoggedIn = vi.fn<() => boolean>();

  beforeEach(() => {
    loadCurrentUser.mockReset();
    isLoggedIn.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            loadCurrentUser,
            isLoggedIn,
            user: () => (isLoggedIn() ? { email: 'a' } : undefined),
          },
        },
      ],
    });
  });

  const run = (url: string) =>
    TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
    );

  it('lets signed-in users through', async () => {
    isLoggedIn.mockReturnValue(true);

    expect(await run('/jobs')).toBe(true);
    expect(loadCurrentUser).not.toHaveBeenCalled();
  });

  it('checks the session once when it is unknown', async () => {
    isLoggedIn.mockReturnValue(false);
    loadCurrentUser.mockResolvedValue(true);

    expect(await run('/jobs')).toBe(true);
    expect(loadCurrentUser).toHaveBeenCalledOnce();
  });

  it('redirects to the login page and remembers the target', async () => {
    isLoggedIn.mockReturnValue(false);
    loadCurrentUser.mockResolvedValue(false);

    const result = (await run('/jobs/42')) as UrlTree;

    expect(TestBed.inject(Router).serializeUrl(result).split('?')[0]).toBe('/login');
    expect(result.queryParams).toEqual({ returnUrl: '/jobs/42' });
  });
});
