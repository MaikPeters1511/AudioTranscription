import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface CurrentUser {
  email: string;
}

/** Cookie-based session against the API's ASP.NET Core Identity endpoints (ADR 0003). */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);

  /** undefined: not checked yet, null: signed out. */
  private readonly currentUser = signal<CurrentUser | null | undefined>(undefined);
  readonly user = this.currentUser.asReadonly();
  readonly isLoggedIn = computed(() => !!this.currentUser());

  async loadCurrentUser(): Promise<boolean> {
    try {
      this.currentUser.set(await firstValueFrom(this.http.get<CurrentUser>('/api/auth/me')));
      return true;
    } catch {
      // 401 (or API unreachable): treat as signed out, the login page shows the next step
      this.currentUser.set(null);
      return false;
    }
  }

  async login(email: string, password: string): Promise<boolean> {
    try {
      await firstValueFrom(this.http.post('/api/auth/login?useCookies=true', { email, password }));
    } catch {
      this.currentUser.set(null);
      return false;
    }
    return this.loadCurrentUser();
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/auth/logout', null));
    } finally {
      this.currentUser.set(null);
    }
  }

  /** Called when the API rejects the session (e.g. expired cookie). */
  markLoggedOut(): void {
    this.currentUser.set(null);
  }
}
