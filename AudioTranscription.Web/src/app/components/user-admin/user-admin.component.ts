import { Component, OnInit, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { ADMIN_ROLE, AdminUser, AdminUserService } from '../../services/admin-user.service';
import { AuthService } from '../../auth/auth.service';
import { ToastService } from '../../services/toast.service';
import { PageTitleService } from '../../i18n/page-title.service';

/**
 * User administration for admins (S18): list users, create accounts, grant or revoke the Admin
 * role and deactivate or reactivate accounts. The API enforces every rule (e.g. the last active
 * admin is protected); this page only shows clean, translated messages for the outcome.
 */
@Component({
  selector: 'app-user-admin',
  standalone: true,
  imports: [FormsModule, TranslocoPipe],
  template: `
    <section class="max-w-4xl mx-auto" [attr.aria-labelledby]="'user-admin-title'">
      <div class="flex items-center justify-between gap-4 mb-6">
        <h1 id="user-admin-title" class="text-3xl font-bold">{{ 'admin.users.title' | transloco }}</h1>
        <button type="button" class="btn btn-primary" (click)="openCreate()">
          {{ 'admin.users.add' | transloco }}
        </button>
      </div>

      @if (loading()) {
        <div class="skeleton h-32 w-full" role="status">
          <span class="sr-only">{{ 'admin.users.loading' | transloco }}</span>
        </div>
      } @else if (loadFailed()) {
        <div class="alert alert-error" role="alert">
          <span>{{ 'admin.users.loadFailed' | transloco }}</span>
          <button type="button" class="btn btn-sm" (click)="load()">{{ 'admin.users.retry' | transloco }}</button>
        </div>
      } @else {
        <div class="overflow-x-auto">
          <table class="table bg-base-200 rounded-lg">
            <caption class="sr-only">{{ 'admin.users.caption' | transloco }}</caption>
            <thead>
              <tr>
                <th scope="col">{{ 'admin.users.email' | transloco }}</th>
                <th scope="col">{{ 'admin.users.admin' | transloco }}</th>
                <th scope="col">{{ 'admin.users.status' | transloco }}</th>
                <th scope="col"><span class="sr-only">{{ 'admin.users.actions' | transloco }}</span></th>
              </tr>
            </thead>
            <tbody>
              @for (user of users(); track user.id) {
                <tr>
                  <th scope="row" class="font-medium">{{ user.email }}</th>
                  <td>
                    <input
                      type="checkbox"
                      class="toggle toggle-sm"
                      [checked]="isAdmin(user)"
                      [attr.aria-label]="'admin.users.adminFor' | transloco: { email: user.email }"
                      (change)="toggleAdmin(user, $any($event.target).checked)"
                    />
                  </td>
                  <td>
                    <span class="badge" [class.badge-success]="user.isActive" [class.badge-ghost]="!user.isActive">
                      {{ (user.isActive ? 'admin.users.active' : 'admin.users.inactive') | transloco }}
                    </span>
                  </td>
                  <td class="text-right">
                    @if (user.isActive) {
                      @if (user.email !== auth.user()?.email) {
                        <button
                          type="button"
                          class="btn btn-ghost btn-sm"
                          [attr.aria-label]="'admin.users.deactivateFor' | transloco: { email: user.email }"
                          (click)="confirmTarget.set(user)"
                        >
                          {{ 'admin.users.deactivate' | transloco }}
                        </button>
                      }
                    } @else {
                      <button
                        type="button"
                        class="btn btn-ghost btn-sm"
                        [attr.aria-label]="'admin.users.activateFor' | transloco: { email: user.email }"
                        (click)="activate(user)"
                      >
                        {{ 'admin.users.activate' | transloco }}
                      </button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <p class="text-xs text-base-content/60 mt-3">{{ 'admin.users.count' | transloco: { count: total() } }}</p>
      }

      @if (showCreate()) {
        <div
          class="modal modal-open"
          role="dialog"
          aria-modal="true"
          aria-labelledby="create-user-title"
          (keydown.escape)="closeCreate()"
        >
          <form class="modal-box flex flex-col gap-4" (ngSubmit)="create()" novalidate>
            <h2 id="create-user-title" class="text-xl font-bold">{{ 'admin.users.createTitle' | transloco }}</h2>

            @if (createError(); as key) {
              <div class="alert alert-error" role="alert">
                <span>{{ key | transloco }}</span>
              </div>
            }

            <div class="flex flex-col gap-1">
              <label for="new-user-email" class="font-medium">{{ 'admin.users.email' | transloco }}</label>
              <input
                id="new-user-email"
                name="email"
                type="email"
                class="input w-full"
                autocomplete="off"
                required
                [(ngModel)]="newEmail"
              />
            </div>
            <div class="flex flex-col gap-1">
              <label for="new-user-password" class="font-medium">{{ 'admin.users.password' | transloco }}</label>
              <input
                id="new-user-password"
                name="password"
                type="password"
                class="input w-full"
                autocomplete="new-password"
                required
                aria-describedby="new-user-password-hint"
                [(ngModel)]="newPassword"
              />
              <p id="new-user-password-hint" class="text-xs text-base-content/60">
                {{ 'admin.users.passwordHint' | transloco }}
              </p>
            </div>
            <label class="flex items-center gap-2 cursor-pointer">
              <input type="checkbox" class="checkbox checkbox-sm" name="makeAdmin" [(ngModel)]="newAdmin" />
              <span>{{ 'admin.users.makeAdmin' | transloco }}</span>
            </label>

            <div class="modal-action">
              <button type="button" class="btn" (click)="closeCreate()">{{ 'admin.users.cancel' | transloco }}</button>
              <button type="submit" class="btn btn-primary" [disabled]="submitting()" [attr.aria-busy]="submitting()">
                {{ 'admin.users.create' | transloco }}
              </button>
            </div>
          </form>
        </div>
      }

      @if (confirmTarget(); as target) {
        <div
          class="modal modal-open"
          role="alertdialog"
          aria-modal="true"
          aria-labelledby="confirm-title"
          aria-describedby="confirm-text"
          (keydown.escape)="confirmTarget.set(null)"
        >
          <div class="modal-box">
            <h2 id="confirm-title" class="text-xl font-bold">{{ 'admin.users.confirmTitle' | transloco }}</h2>
            <p id="confirm-text" class="py-4">{{ 'admin.users.confirmText' | transloco: { email: target.email } }}</p>
            <div class="modal-action">
              <button type="button" class="btn" (click)="confirmTarget.set(null)">
                {{ 'admin.users.cancel' | transloco }}
              </button>
              <button type="button" class="btn btn-error" (click)="deactivate(target)">
                {{ 'admin.users.deactivate' | transloco }}
              </button>
            </div>
          </div>
        </div>
      }
    </section>
  `,
})
export class UserAdminComponent implements OnInit {
  private api = inject(AdminUserService);
  private toast = inject(ToastService);
  private transloco = inject(TranslocoService);
  private pageTitle = inject(PageTitleService);
  readonly auth = inject(AuthService);

  readonly users = signal<AdminUser[]>([]);
  readonly total = signal(0);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);

  readonly showCreate = signal(false);
  readonly submitting = signal(false);
  readonly createError = signal<string | null>(null);
  readonly confirmTarget = signal<AdminUser | null>(null);

  newEmail = '';
  newPassword = '';
  newAdmin = false;

  ngOnInit(): void {
    this.pageTitle.set('admin.users.pageTitle');
    void this.load();
  }

  isAdmin(user: AdminUser): boolean {
    return user.roles.includes(ADMIN_ROLE);
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailed.set(false);
    try {
      const page = await this.api.list();
      this.users.set(page.items);
      this.total.set(page.total);
    } catch {
      this.loadFailed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  openCreate(): void {
    this.newEmail = '';
    this.newPassword = '';
    this.newAdmin = false;
    this.createError.set(null);
    this.showCreate.set(true);
  }

  closeCreate(): void {
    this.showCreate.set(false);
  }

  async create(): Promise<void> {
    if (this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.createError.set(null);
    try {
      await this.api.create({
        email: this.newEmail.trim(),
        password: this.newPassword,
        roles: this.newAdmin ? [ADMIN_ROLE] : [],
      });
      this.showCreate.set(false);
      this.toast.success(this.transloco.translate('admin.users.created'));
      await this.load();
    } catch (error) {
      this.createError.set(this.createErrorKey(error));
    } finally {
      this.submitting.set(false);
    }
  }

  async toggleAdmin(user: AdminUser, makeAdmin: boolean): Promise<void> {
    const roles = makeAdmin ? [ADMIN_ROLE] : [];
    try {
      await this.api.setRoles(user.id, roles);
      this.toast.success(this.transloco.translate('admin.users.rolesSaved'));
    } catch (error) {
      this.toast.error(this.transloco.translate(this.changeErrorKey(error, 'admin.users.error.lastAdminRole')));
    }
    // Reload either way so the toggle always shows what the server really has
    await this.load();
  }

  async deactivate(user: AdminUser): Promise<void> {
    this.confirmTarget.set(null);
    try {
      await this.api.deactivate(user.id);
      this.toast.success(this.transloco.translate('admin.users.deactivated'));
    } catch (error) {
      this.toast.error(this.transloco.translate(this.changeErrorKey(error, 'admin.users.error.lastAdminDeactivate')));
    }
    await this.load();
  }

  async activate(user: AdminUser): Promise<void> {
    try {
      await this.api.activate(user.id);
      this.toast.success(this.transloco.translate('admin.users.activated'));
    } catch (error) {
      this.toast.error(this.transloco.translate(this.changeErrorKey(error, 'admin.users.error.generic')));
    }
    await this.load();
  }

  private createErrorKey(error: unknown): string {
    const status = error instanceof HttpErrorResponse ? error.status : 0;
    if (status === 409) return 'admin.users.error.duplicate';
    if (status === 400) return 'admin.users.error.invalid';
    return 'admin.users.error.generic';
  }

  private changeErrorKey(error: unknown, conflictKey: string): string {
    const status = error instanceof HttpErrorResponse ? error.status : 0;
    return status === 409 ? conflictKey : 'admin.users.error.generic';
  }
}
