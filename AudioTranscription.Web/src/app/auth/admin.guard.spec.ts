import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  provideRouter,
  Router,
  RouterStateSnapshot,
  UrlTree,
} from '@angular/router';
import { adminGuard } from './admin.guard';
import { AuthService } from './auth.service';

describe('adminGuard', () => {
  const loadCurrentUser = vi.fn<() => Promise<boolean>>();
  let user: unknown;
  let isAdmin: boolean;

  beforeEach(() => {
    loadCurrentUser.mockReset();
    user = { email: 'a@example.com', roles: [] };
    isAdmin = false;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { loadCurrentUser, user: () => user, isAdmin: () => isAdmin } },
      ],
    });
  });

  const run = () =>
    TestBed.runInInjectionContext(() =>
      adminGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );

  it('lets administrators through', async () => {
    isAdmin = true;

    expect(await run()).toBe(true);
  });

  it('redirects regular users to the start page', async () => {
    const result = await run();

    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/');
  });

  it('loads the session first when it is still unknown', async () => {
    user = undefined;
    loadCurrentUser.mockImplementation(async () => {
      user = { email: 'a@example.com', roles: ['Admin'] };
      isAdmin = true;
      return true;
    });

    expect(await run()).toBe(true);
    expect(loadCurrentUser).toHaveBeenCalledOnce();
  });
});
