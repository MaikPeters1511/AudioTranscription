import { Component, signal, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SignalRService } from './services/signalr.service';
import { ToastComponent } from './components/toast/toast.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive, ToastComponent],
  template: `
    <div class="min-h-screen bg-base-100" [attr.data-theme]="theme()">
      <!-- Navbar -->
      <div class="navbar bg-base-200 shadow-sm px-4 lg:px-8">
        <div class="navbar-start">
          <a routerLink="/" class="btn btn-ghost text-xl font-bold gap-2">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-6 w-6 text-primary" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 11a7 7 0 01-7 7m0 0a7 7 0 01-7-7m7 7v4m0 0H8m4 0h4m-4-8a3 3 0 01-3-3V5a3 3 0 116 0v6a3 3 0 01-3 3z" />
            </svg>
            Transkription
          </a>
        </div>
        <div class="navbar-center hidden sm:flex">
          <ul class="menu menu-horizontal px-1 gap-1">
            <li>
              <a routerLink="/upload" routerLinkActive="active" class="rounded-lg">
                Upload
              </a>
            </li>
            <li>
              <a routerLink="/jobs" routerLinkActive="active" class="rounded-lg">
                Jobs
              </a>
            </li>
          </ul>
        </div>
        <div class="navbar-end gap-2">
          <!-- SignalR Connection Status -->
          <div class="tooltip tooltip-bottom" [attr.data-tip]="signalR.connected() ? 'Live verbunden' : 'Nicht verbunden'">
            <div class="w-2 h-2 rounded-full" [class.bg-success]="signalR.connected()" [class.bg-error]="!signalR.connected()"></div>
          </div>

          <!-- Theme Toggle -->
          <label class="swap swap-rotate btn btn-ghost btn-circle btn-sm">
            <input type="checkbox" [checked]="theme() === 'dark'" (change)="toggleTheme()" />
            <!-- Sun -->
            <svg class="swap-off h-5 w-5" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 3v1m0 16v1m9-9h-1M4 12H3m15.364 6.364l-.707-.707M6.343 6.343l-.707-.707m12.728 0l-.707.707M6.343 17.657l-.707.707M16 12a4 4 0 11-8 0 4 4 0 018 0z" />
            </svg>
            <!-- Moon -->
            <svg class="swap-on h-5 w-5" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20.354 15.354A9 9 0 018.646 3.646 9.003 9.003 0 0012 21a9.003 9.003 0 008.354-5.646z" />
            </svg>
          </label>
        </div>
      </div>

      <!-- Mobile Navigation -->
      <div class="btm-nav sm:hidden z-50">
        <a routerLink="/upload" routerLinkActive="active">
          <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12" />
          </svg>
          <span class="btm-nav-label text-xs">Upload</span>
        </a>
        <a routerLink="/jobs" routerLinkActive="active">
          <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2" />
          </svg>
          <span class="btm-nav-label text-xs">Jobs</span>
        </a>
      </div>

      <!-- Main Content -->
      <main class="container mx-auto px-4 py-8 pb-20 sm:pb-8">
        <router-outlet />
      </main>
      
      <!-- Global Toasts -->
      <app-toast />
    </div>
  `,
})
export class App implements OnInit {
  signalR = inject(SignalRService);
  theme = signal<'light' | 'dark'>(
    (typeof localStorage !== 'undefined' && localStorage.getItem('theme') as 'light' | 'dark') || 'light'
  );

  ngOnInit(): void {
    document.documentElement.setAttribute('data-theme', this.theme());
    // Connect SignalR to the API — the proxy will forward /api requests
    this.signalR.start('');
  }

  toggleTheme(): void {
    const newTheme = this.theme() === 'light' ? 'dark' : 'light';
    this.theme.set(newTheme);
    document.documentElement.setAttribute('data-theme', newTheme);
    localStorage.setItem('theme', newTheme);
  }
}
