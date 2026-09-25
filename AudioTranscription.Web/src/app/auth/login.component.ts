import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthService } from './auth.service';
import { safeReturnUrl } from './safe-return-url';
import { PageTitleService } from '../i18n/page-title.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule, TranslocoPipe],
  template: `
    <div class="max-w-sm mx-auto">
      <h1 class="text-3xl font-bold mb-6">{{ 'auth.login.title' | transloco }}</h1>

      <form class="card bg-base-200 shadow-sm" (ngSubmit)="submit()" novalidate>
        <div class="card-body gap-4">
          @if (failed()) {
            <div class="alert alert-error" role="alert">
              <span>{{ 'auth.login.failed' | transloco }}</span>
            </div>
          }

          <div class="flex flex-col gap-1">
            <label for="login-email" class="font-medium">{{
              'auth.login.email' | transloco
            }}</label>
            <input
              id="login-email"
              name="email"
              type="email"
              class="input w-full"
              autocomplete="username"
              required
              [attr.aria-invalid]="failed() || null"
              [(ngModel)]="email"
            />
          </div>

          <div class="flex flex-col gap-1">
            <label for="login-password" class="font-medium">{{
              'auth.login.password' | transloco
            }}</label>
            <input
              id="login-password"
              name="password"
              type="password"
              class="input w-full"
              autocomplete="current-password"
              required
              [attr.aria-invalid]="failed() || null"
              [(ngModel)]="password"
            />
          </div>

          <button
            type="submit"
            class="btn btn-primary"
            [disabled]="submitting()"
            [attr.aria-busy]="submitting()"
          >
            @if (submitting()) {
              <span class="loading loading-spinner loading-sm" aria-hidden="true"></span>
              {{ 'auth.login.submitting' | transloco }}
            } @else {
              {{ 'auth.login.submit' | transloco }}
            }
          </button>
        </div>
      </form>
    </div>
  `,
})
export class LoginComponent implements OnInit {
  private auth = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private pageTitle = inject(PageTitleService);

  email = '';
  password = '';
  submitting = signal(false);
  failed = signal(false);

  ngOnInit(): void {
    this.pageTitle.set('auth.login.pageTitle');
  }

  async submit(): Promise<void> {
    if (!this.email.trim() || !this.password || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.failed.set(false);
    const success = await this.auth.login(this.email.trim(), this.password);
    this.submitting.set(false);

    if (success) {
      this.password = '';
      await this.router.navigateByUrl(
        safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl')),
      );
    } else {
      this.failed.set(true);
    }
  }
}
