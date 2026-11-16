import { Component, signal, inject, OnInit, effect, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SignalRService } from './services/signalr.service';
import { ToastComponent } from './components/toast/toast.component';
import { TranslocoPipe } from '@jsverse/transloco';
import { LanguageService } from './i18n/language.service';
import { AuthService } from './auth/auth.service';
import { OfflineBannerComponent } from './components/offline/offline-banner.component';
import { LogoutConfirmDialogComponent } from './components/offline/logout-confirm-dialog.component';
import { OfflineUploadQueueService } from './services/offline-upload-queue.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive, ToastComponent, TranslocoPipe, OfflineBannerComponent, LogoutConfirmDialogComponent],
  template: `
    <div class="min-h-screen bg-base-100" [attr.data-theme]="theme()">
      <app-offline-banner />
      <!-- Navbar -->
      <header class="navbar bg-base-200 shadow-sm px-4 lg:px-8 gap-2">
        <div class="flex-none flex items-center gap-1">
          <a routerLink="/" class="btn btn-ghost text-xl font-bold gap-2 px-2">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-6 w-6 text-primary" fill="none" viewBox="0 0 24 24" stroke="currentColor" aria-hidden="true">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 11a7 7 0 01-7 7m0 0a7 7 0 01-7-7m7 7v4m0 0H8m4 0h4m-4-8a3 3 0 01-3-3V5a3 3 0 116 0v6a3 3 0 01-3 3z" />
            </svg>
            {{ 'app.name' | transloco }}
          </a>
          <!-- Compact menu: all links in one dropdown below the xl breakpoint -->
          @if (auth.user()) {
            <details class="nav-dd dropdown xl:hidden">
              <summary class="btn btn-ghost btn-square btn-sm list-none" [attr.aria-label]="'app.nav.menu' | transloco">
                <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" aria-hidden="true">
                  <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 6h16M4 12h16M4 18h16" />
                </svg>
              </summary>
              <ul class="menu menu-sm dropdown-content bg-base-200 rounded-box z-50 mt-3 w-60 p-2 shadow-lg" [attr.aria-label]="'app.nav.main' | transloco">
                @for (link of mainLinks; track link.path) {
                  <li><a [routerLink]="link.path" routerLinkActive="active">{{ link.label | transloco }}</a></li>
                }
                @if (auth.isAdmin()) {
                  <li class="menu-title mt-2">{{ 'app.nav.admin' | transloco }}</li>
                  @for (link of adminLinks; track link.path) {
                    <li><a [routerLink]="link.path" routerLinkActive="active">{{ link.label | transloco }}</a></li>
                  }
                }
                <li class="menu-title mt-2">{{ auth.user()?.email }}</li>
                @for (link of accountLinks; track link.path) {
                  <li><a [routerLink]="link.path" routerLinkActive="active">{{ link.label | transloco }}</a></li>
                }
              </ul>
            </details>
          }
        </div>

        <!-- Main navigation (xl and wider) -->
        <nav class="flex-1 hidden xl:flex justify-center" [attr.aria-label]="'app.nav.main' | transloco">
          <ul class="menu menu-horizontal menu-sm px-1 gap-1 flex-nowrap">
            @for (link of mainLinks; track link.path) {
              <li>
                <a [routerLink]="link.path" routerLinkActive="active" class="rounded-lg whitespace-nowrap">{{ link.label | transloco }}</a>
              </li>
            }
          </ul>
        </nav>
        <div class="flex-1 xl:hidden"></div>

        <div class="flex-none flex items-center gap-1">
          <!-- SignalR Connection Status -->
          <div class="tooltip tooltip-bottom" [attr.data-tip]="(signalR.connected() ? 'app.connection.live' : 'app.connection.offline') | transloco">
            <div
              class="w-2 h-2 rounded-full mx-1"
              role="status"
              [attr.aria-label]="(signalR.connected() ? 'app.connection.live' : 'app.connection.offline') | transloco"
              [class.bg-success]="signalR.connected()"
              [class.bg-error]="!signalR.connected()"
            ></div>
          </div>

          @if (auth.user(); as user) {
            @if (auth.isAdmin()) {
              <details class="nav-dd dropdown dropdown-end hidden xl:block">
                <summary class="btn btn-ghost btn-sm list-none gap-1">
                  {{ 'app.nav.admin' | transloco }}
                  <svg xmlns="http://www.w3.org/2000/svg" class="h-3 w-3" fill="none" viewBox="0 0 24 24" stroke="currentColor" aria-hidden="true">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 9l-7 7-7-7" />
                  </svg>
                </summary>
                <ul class="menu menu-sm dropdown-content bg-base-200 rounded-box z-50 mt-3 w-56 p-2 shadow-lg">
                  @for (link of adminLinks; track link.path) {
                    <li><a [routerLink]="link.path" routerLinkActive="active">{{ link.label | transloco }}</a></li>
                  }
                </ul>
              </details>
            }

            <!-- Account menu (xl and wider) -->
            <details class="nav-dd dropdown dropdown-end hidden xl:block">
              <summary class="btn btn-ghost btn-circle btn-sm list-none" [attr.aria-label]="'app.nav.userMenu' | transloco" [attr.title]="user.email">
                <span class="avatar avatar-placeholder">
                  <span class="bg-primary text-primary-content w-7 rounded-full text-xs font-semibold" aria-hidden="true">{{ initial(user.email) }}</span>
                </span>
              </summary>
              <ul class="menu menu-sm dropdown-content bg-base-200 rounded-box z-50 mt-3 w-64 p-2 shadow-lg">
                <li class="menu-title truncate">{{ 'auth.signedInAs' | transloco: { email: user.email } }}</li>
                @for (link of accountLinks; track link.path) {
                  <li><a [routerLink]="link.path" routerLinkActive="active">{{ link.label | transloco }}</a></li>
                }
              </ul>
            </details>

            <button type="button" class="btn btn-ghost btn-sm" (click)="logout()">
              {{ 'auth.logout' | transloco }}
            </button>
          }

          <!-- Language Switch -->
          <button
            type="button"
            class="btn btn-ghost btn-sm font-semibold"
            (click)="language.toggle()"
            [attr.aria-label]="'app.language.switch' | transloco"
            [title]="'app.language.' + (language.current() === 'de' ? 'en' : 'de') | transloco"
          >
            {{ language.current().toUpperCase() }}
          </button>

          <!-- Theme Toggle -->
          <label class="swap swap-rotate btn btn-ghost btn-circle btn-sm">
            <input
              type="checkbox"
              [checked]="theme() === 'dark'"
              (change)="toggleTheme()"
              [attr.aria-label]="'app.theme.toggle' | transloco"
            />
            <!-- Sun -->
            <svg class="swap-off h-5 w-5" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor" aria-hidden="true">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 3v1m0 16v1m9-9h-1M4 12H3m15.364 6.364l-.707-.707M6.343 6.343l-.707-.707m12.728 0l-.707.707M6.343 17.657l-.707.707M16 12a4 4 0 11-8 0 4 4 0 018 0z" />
            </svg>
            <!-- Moon -->
            <svg class="swap-on h-5 w-5" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor" aria-hidden="true">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20.354 15.354A9 9 0 018.646 3.646 9.003 9.003 0 0012 21a9.003 9.003 0 008.354-5.646z" />
            </svg>
          </label>
        </div>
      </header>

      <!-- Mobile Navigation -->
      <div class="btm-nav sm:hidden z-50">
        <a routerLink="/upload" routerLinkActive="active">
          <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12" />
          </svg>
          <span class="btm-nav-label text-xs">{{ 'app.nav.upload' | transloco }}</span>
        </a>
        <a routerLink="/jobs" routerLinkActive="active">
          <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2" />
          </svg>
          <span class="btm-nav-label text-xs">{{ 'app.nav.jobs' | transloco }}</span>
        </a>
        <a routerLink="/search" routerLinkActive="active">
          <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 21l-4.35-4.35M17 11a6 6 0 11-12 0 6 6 0 0112 0z" />
          </svg>
          <span class="btm-nav-label text-xs">{{ 'app.nav.search' | transloco }}</span>
        </a>
        <a routerLink="/organize" routerLinkActive="active">
          <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 7a2 2 0 012-2h4l2 2h8a2 2 0 012 2v8a2 2 0 01-2 2H5a2 2 0 01-2-2V7z" />
          </svg>
          <span class="btm-nav-label text-xs">{{ 'app.nav.organize' | transloco }}</span>
        </a>
      </div>

      <!-- Main Content -->
      <main class="container mx-auto px-4 py-8 pb-20 sm:pb-8">
        <router-outlet />
      </main>
      
      <!-- Global Toasts -->
      <app-toast />

      @if (confirmLogoutCount(); as count) {
        <app-logout-confirm-dialog [count]="count" (confirmed)="logoutAndDiscard()" (cancelled)="confirmLogoutCount.set(0)" />
      }
    </div>
  `,
})
export class App implements OnInit {
  signalR = inject(SignalRService);
  language = inject(LanguageService);
  auth = inject(AuthService);
  private router = inject(Router);
  /** Starts the queue of local recordings as soon as the app runs (S33). */
  private offlineQueue = inject(OfflineUploadQueueService);
  /** Number of unsent local recordings while the sign-out waits for the user's decision (0: no dialog). */
  readonly confirmLogoutCount = signal(0);

  readonly mainLinks = [
    { path: '/upload', label: 'app.nav.upload' },
    { path: '/jobs', label: 'app.nav.jobs' },
    { path: '/search', label: 'app.nav.search' },
    { path: '/organize', label: 'app.nav.organize' },
    { path: '/queue', label: 'app.nav.queue' },
    { path: '/chat', label: 'app.nav.chat' },
    { path: '/glossaries', label: 'app.nav.glossaries' },
  ];
  readonly adminLinks = [
    { path: '/admin/users', label: 'app.nav.users' },
    { path: '/admin/jobs', label: 'app.nav.unassignedJobs' },
    { path: '/admin/templates', label: 'app.nav.templates' },
  ];
  readonly accountLinks = [
    { path: '/api-keys', label: 'app.nav.apiKeys' },
    { path: '/webhooks', label: 'app.nav.webhooks' },
    { path: '/account', label: 'app.nav.account' },
  ];
  theme = signal<'light' | 'dark'>(
    (typeof localStorage !== 'undefined' && localStorage.getItem('theme') as 'light' | 'dark') || 'light'
  );

  /** Closes the open dropdown menus after a click on a link or outside of them. */
  @HostListener('document:click', ['$event'])
  closeMenus(event: Event): void {
    const target = event.target as Node;
    document.querySelectorAll<HTMLDetailsElement>('details.nav-dd[open]').forEach(menu => {
      const clickedSummary = target instanceof Element && target.closest('summary') !== null && menu.contains(target);
      if (!clickedSummary) {
        menu.open = false;
      }
    });
  }

  @HostListener('document:keydown.escape')
  closeMenusOnEscape(): void {
    document.querySelectorAll<HTMLDetailsElement>('details.nav-dd[open]').forEach(menu => (menu.open = false));
  }

  initial(email: string): string {
    return (email.trim().charAt(0) || '?').toUpperCase();
  }

  ngOnInit(): void {
    document.documentElement.setAttribute('data-theme', this.theme());
  }

  constructor() {
    // The hub requires a session: connect after sign-in, disconnect on sign-out
    effect(() => {
      if (this.auth.isLoggedIn()) {
        this.signalR.start('');
      } else {
        this.signalR.stop();
      }
    });
  }

  async logout(): Promise<void> {
    // Local recordings of this user are deleted on sign-out, so ask first when there are unsent ones (S33-T4)
    if (await this.offlineQueue.hasLocalRecordings()) {
      this.confirmLogoutCount.set(this.offlineQueue.items().length || 1);
      return;
    }
    await this.signOut();
  }

  async logoutAndDiscard(): Promise<void> {
    this.confirmLogoutCount.set(0);
    await this.offlineQueue.discardAllOfCurrentUser();
    await this.signOut();
  }

  private async signOut(): Promise<void> {
    try {
      await this.auth.logout();
    } catch (error) {
      // Offline or API down: the session is dropped locally in any case, the cookie expires on its own
      console.warn('Sign-out request failed', error);
    }
    await this.router.navigate(['/login']);
  }

  toggleTheme(): void {
    const newTheme = this.theme() === 'light' ? 'dark' : 'light';
    this.theme.set(newTheme);
    document.documentElement.setAttribute('data-theme', newTheme);
    localStorage.setItem('theme', newTheme);
  }
}
