import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface CurrentUser {
  email: string;
  roles?: string[];
}

const LAST_USER_KEY = 'lastUserEmail';

function readLastUser(): string | null {
  try {
    return localStorage.getItem(LAST_USER_KEY);
  } catch {
    return null; // storage blocked: no offline start, nothing else lost
  }
}

function writeLastUser(email: string | null): void {
  try {
    if (email) {
      localStorage.setItem(LAST_USER_KEY, email);
    } else {
      localStorage.removeItem(LAST_USER_KEY);
    }
  } catch {
    // storage blocked: see readLastUser
  }
}

/** Cookie-based session against the API's ASP.NET Core Identity endpoints (ADR 0003). */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);

  /** undefined: not checked yet, null: signed out. */
  private readonly currentUser = signal<CurrentUser | null | undefined>(undefined);
  readonly user = this.currentUser.asReadonly();
  /** True while the user is only assumed from the last session because the API could not be reached (S33). */
  readonly unverified = signal(false);
  readonly isLoggedIn = computed(() => !!this.currentUser());
  readonly isAdmin = computed(() => this.currentUser()?.roles?.includes('Admin') ?? false);

  /** Changes the own password via the Identity manage endpoint; resolves false when the API rejects it. */
  async changePassword(oldPassword: string, newPassword: string): Promise<boolean> {
    try {
      await firstValueFrom(this.http.post('/api/auth/manage/info', { oldPassword, newPassword }));
      return true;
    } catch {
      return false;
    }
  }

  async loadCurrentUser(): Promise<boolean> {
    try {
      const user = await firstValueFrom(this.http.get<CurrentUser>('/api/auth/me'));
      this.currentUser.set(user);
      this.unverified.set(false);
      writeLastUser(user.email);
      return true;
    } catch (error) {
      // No connection (status 0): open the app for the last user so a recording can be made offline (ADR 0012).
      // The API still checks every request, so this only unlocks the screens, never any data.
      const lastUser = readLastUser();
      if (error instanceof HttpErrorResponse && error.status === 0 && lastUser) {
        this.currentUser.set({ email: lastUser });
        this.unverified.set(true);
        return true;
      }
      // 401: treat as signed out, the login page shows the next step
      this.currentUser.set(null);
      this.unverified.set(false);
      writeLastUser(null);
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
      this.markLoggedOut();
    }
  }

  /** Called when the API rejects the session (e.g. expired cookie). */
  markLoggedOut(): void {
    this.currentUser.set(null);
    this.unverified.set(false);
    writeLastUser(null);
  }
}
