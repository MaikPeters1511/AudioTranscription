import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Only signed-in users may open a route; others go to /login and come back afterwards. */
export const authGuard: CanActivateFn = async (_route, state) => {
  // inject() only works synchronously, before the first await
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isLoggedIn() || (auth.user() === undefined && (await auth.loadCurrentUser()))) {
    return true;
  }
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};
