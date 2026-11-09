import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthService } from './auth.service';
import { PageTitleService } from '../i18n/page-title.service';

/** Lets a signed-in user change their own password; the current password is required. */
@Component({
  selector: 'app-change-password',
  standalone: true,
  imports: [FormsModule, TranslocoPipe],
  template: `
    <div class="max-w-sm mx-auto">
      <h1 class="text-3xl font-bold mb-6">{{ 'auth.changePassword.title' | transloco }}</h1>

      <form class="card bg-base-200 shadow-sm" (ngSubmit)="submit()" novalidate>
        <div class="card-body gap-4">
          @if (failed()) {
            <div class="alert alert-error" role="alert">
              <span>{{ 'auth.changePassword.failed' | transloco }}</span>
            </div>
          }

          <div class="flex flex-col gap-1">
            <label for="old-password" class="font-medium">{{ 'auth.changePassword.current' | transloco }}</label>
            <input
              id="old-password"
              name="oldPassword"
              type="password"
              class="input w-full"
              autocomplete="current-password"
              required
              [(ngModel)]="oldPassword"
            />
          </div>

          <div class="flex flex-col gap-1">
            <label for="new-password" class="font-medium">{{ 'auth.changePassword.new' | transloco }}</label>
            <input
              id="new-password"
              name="newPassword"
              type="password"
              class="input w-full"
              autocomplete="new-password"
              required
              aria-describedby="new-password-hint"
              [(ngModel)]="newPassword"
            />
            <p id="new-password-hint" class="text-xs text-base-content/60">
              {{ 'auth.changePassword.hint' | transloco }}
            </p>
          </div>

          <button type="submit" class="btn btn-primary" [disabled]="submitting()" [attr.aria-busy]="submitting()">
            {{ 'auth.changePassword.submit' | transloco }}
          </button>
        </div>
      </form>
    </div>
  `,
})
export class ChangePasswordComponent implements OnInit {
  private auth = inject(AuthService);
  private router = inject(Router);
  private pageTitle = inject(PageTitleService);

  oldPassword = '';
  newPassword = '';
  readonly submitting = signal(false);
  readonly failed = signal(false);

  ngOnInit(): void {
    this.pageTitle.set('auth.changePassword.pageTitle');
  }

  async submit(): Promise<void> {
    if (this.submitting() || !this.oldPassword || !this.newPassword) {
      return;
    }
    this.submitting.set(true);
    const ok = await this.auth.changePassword(this.oldPassword, this.newPassword);
    this.submitting.set(false);
    this.failed.set(!ok);
    if (ok) {
      this.oldPassword = '';
      this.newPassword = '';
      // A new password ends every session of this user, so sign in again with it
      this.auth.markLoggedOut();
      await this.router.navigate(['/login'], { queryParams: { passwordChanged: '1' } });
    }
  }
}
