import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Only administrators may open a route; everyone else is sent back to the start page. */
export const adminGuard: CanActivateFn = async () => {
  // inject() only works synchronously, before the first await
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.user() === undefined) {
    await auth.loadCurrentUser();
  }
  return auth.isAdmin() ? true : router.createUrlTree(['/']);
};
