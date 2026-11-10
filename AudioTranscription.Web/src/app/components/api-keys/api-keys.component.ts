import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { API_KEY_SCOPES, ApiKey, ApiKeyScope } from '../../models/automation.model';
import { ApiKeyService } from '../../services/api-key.service';
import { ToastService } from '../../services/toast.service';
import { PageTitleService } from '../../i18n/page-title.service';

const DAY_MS = 86_400_000;

/**
 * Manage the user's API keys (S28): create with scopes and expiry, see the key exactly once with a copy button,
 * revoke. The list never contains a secret.
 */
@Component({
  selector: 'app-api-keys',
  standalone: true,
  imports: [FormsModule, TranslocoPipe],
  template: `
    <div class="max-w-3xl mx-auto space-y-8">
      <h1 class="text-3xl font-bold">{{ 'apiKeys.title' | transloco }}</h1>
      <p class="text-sm text-base-content/70">{{ 'apiKeys.intro' | transloco }}</p>

      @if (loadFailed()) {
        <div class="alert alert-error" role="alert">{{ 'apiKeys.loadFailed' | transloco }}</div>
      }

      @if (createdKey(); as secret) {
        <section
          class="alert alert-warning flex-col items-stretch gap-3"
          role="status"
          aria-labelledby="api-key-new-title"
          data-testid="api-key-new"
        >
          <h2 id="api-key-new-title" class="font-semibold">{{ 'apiKeys.newKeyTitle' | transloco }}</h2>
          <p data-testid="api-key-once" class="text-sm">{{ 'apiKeys.once' | transloco }}</p>
          <div class="flex gap-2">
            <input
              type="text"
              readonly
              class="input input-sm flex-1 font-mono"
              data-testid="api-key-secret"
              [value]="secret"
              [attr.aria-label]="'apiKeys.newKeyLabel' | transloco"
              (focus)="selectAll($event)"
            />
            <button type="button" class="btn btn-sm" data-testid="api-key-copy" (click)="copy(secret)">
              {{ 'apiKeys.copy' | transloco }}
            </button>
          </div>
          <div>
            <button type="button" class="btn btn-sm btn-primary" data-testid="api-key-dismiss" (click)="createdKey.set(null)">
              {{ 'apiKeys.dismiss' | transloco }}
            </button>
          </div>
        </section>
      }

      <form data-testid="api-key-form" class="space-y-3" (ngSubmit)="create()" novalidate>
        <h2 class="text-xl font-semibold">{{ 'apiKeys.createTitle' | transloco }}</h2>
        <div class="flex flex-col gap-1">
          <label for="api-key-name" class="text-sm font-medium">{{ 'apiKeys.name' | transloco }}</label>
          <input
            id="api-key-name"
            name="apiKeyName"
            data-testid="api-key-name"
            class="input w-full"
            maxlength="100"
            autocomplete="off"
            [ngModel]="name()"
            (ngModelChange)="name.set($event)"
          />
        </div>
        <fieldset class="space-y-1">
          <legend class="text-sm font-medium">{{ 'apiKeys.scopes' | transloco }}</legend>
          @for (scope of scopeOptions; track scope) {
            <label class="flex items-center gap-2 text-sm cursor-pointer">
              <input
                type="checkbox"
                class="checkbox checkbox-sm"
                [attr.data-testid]="'scope-' + scope"
                [checked]="scopes().includes(scope)"
                (change)="toggleScope(scope)"
              />
              <span><code>{{ scope }}</code> – {{ 'apiKeys.scope.' + scope | transloco }}</span>
            </label>
          }
        </fieldset>
        <div class="flex flex-col gap-1">
          <label for="api-key-expiry" class="text-sm font-medium">{{ 'apiKeys.expiry' | transloco }}</label>
          <select
            id="api-key-expiry"
            data-testid="api-key-expiry"
            class="select w-full sm:w-64"
            [value]="expiryDays()"
            (change)="expiryDays.set($any($event.target).value)"
          >
            <option value="30">{{ 'apiKeys.expiryOption.d30' | transloco }}</option>
            <option value="90">{{ 'apiKeys.expiryOption.d90' | transloco }}</option>
            <option value="365">{{ 'apiKeys.expiryOption.d365' | transloco }}</option>
            <option value="">{{ 'apiKeys.expiryOption.never' | transloco }}</option>
          </select>
        </div>
        @if (errorKey(); as key) {
          <p class="text-error text-sm" role="alert" data-testid="api-key-error">{{ key | transloco }}</p>
        }
        <button type="submit" class="btn btn-primary btn-sm" data-testid="api-key-create" [disabled]="busy() || !canCreate()">
          {{ 'apiKeys.create' | transloco }}
        </button>
      </form>

      <section aria-labelledby="api-keys-list-title">
        <h2 id="api-keys-list-title" class="text-xl font-semibold mb-3">{{ 'apiKeys.listTitle' | transloco }}</h2>
        @if (keysService.keys().length === 0) {
          <p class="text-sm text-base-content/60">{{ 'apiKeys.empty' | transloco }}</p>
        } @else {
          <ul class="divide-y divide-base-300 rounded-box bg-base-200">
            @for (key of keysService.keys(); track key.id) {
              <li class="p-3 flex items-start justify-between gap-3">
                <div class="min-w-0 space-y-1">
                  <div class="font-medium break-words">{{ key.name }}</div>
                  <div class="font-mono text-xs text-base-content/70">at_{{ key.prefix }}_…</div>
                  <ul class="flex flex-wrap gap-1" [attr.aria-label]="'apiKeys.scopes' | transloco">
                    @for (scope of key.scopes; track scope) {
                      <li class="badge badge-outline">{{ scope }}</li>
                    }
                  </ul>
                  <div class="text-xs text-base-content/60">
                    {{ expiryText(key) }} · {{ lastUsedText(key) }}
                  </div>
                </div>
                @if (confirmId() === key.id) {
                  <span class="flex items-center gap-2 shrink-0">
                    <span class="text-sm">{{ 'apiKeys.confirmRevoke' | transloco }}</span>
                    <button type="button" class="btn btn-error btn-xs" data-action="confirm-delete-api-key" [disabled]="busy()" (click)="remove(key)">
                      {{ 'apiKeys.revoke' | transloco }}
                    </button>
                    <button type="button" class="btn btn-xs" (click)="confirmId.set(null)">{{ 'apiKeys.cancel' | transloco }}</button>
                  </span>
                } @else {
                  <button
                    type="button"
                    class="btn btn-ghost btn-xs shrink-0"
                    data-action="delete-api-key"
                    [attr.aria-label]="'apiKeys.revokeFor' | transloco: { name: key.name }"
                    (click)="confirmId.set(key.id)"
                  >
                    {{ 'apiKeys.revoke' | transloco }}
                  </button>
                }
              </li>
            }
          </ul>
        }
      </section>

      <section aria-labelledby="api-keys-usage-title" class="space-y-2">
        <h2 id="api-keys-usage-title" class="text-xl font-semibold">{{ 'apiKeys.usageTitle' | transloco }}</h2>
        <p class="text-sm text-base-content/70">{{ 'apiKeys.usageIntro' | transloco }}</p>
        <pre class="bg-base-200 rounded-box p-3 text-xs overflow-x-auto" tabindex="0"><code>curl -H "Authorization: Bearer at_…" {{ origin }}/api/audio-jobs</code></pre>
      </section>
    </div>
  `,
})
export class ApiKeysComponent implements OnInit {
  readonly keysService = inject(ApiKeyService);
  private toast = inject(ToastService);
  private transloco = inject(TranslocoService);
  private pageTitle = inject(PageTitleService);

  readonly scopeOptions = API_KEY_SCOPES;
  readonly origin = typeof location === 'undefined' ? '' : location.origin;

  readonly name = signal('');
  readonly scopes = signal<ApiKeyScope[]>(['jobs:read']);
  readonly expiryDays = signal('90');
  readonly busy = signal(false);
  readonly loadFailed = signal(false);
  readonly errorKey = signal<string | null>(null);
  readonly confirmId = signal<string | null>(null);
  /** The key just created. Held only until the user dismisses it. */
  readonly createdKey = signal<string | null>(null);

  readonly canCreate = computed(() => this.name().trim().length > 0 && this.scopes().length > 0);

  async ngOnInit(): Promise<void> {
    this.pageTitle.set('apiKeys.pageTitle');
    try {
      await this.keysService.load();
    } catch {
      this.loadFailed.set(true);
    }
  }

  toggleScope(scope: ApiKeyScope): void {
    this.scopes.update((current) =>
      current.includes(scope) ? current.filter((s) => s !== scope) : API_KEY_SCOPES.filter((s) => s === scope || current.includes(s)),
    );
  }

  async create(): Promise<void> {
    if (!this.canCreate() || this.busy()) return;
    this.busy.set(true);
    this.errorKey.set(null);
    try {
      const days = Number(this.expiryDays());
      const expiresAt = this.expiryDays() === '' ? null : new Date(Date.now() + days * DAY_MS).toISOString();
      const created = await this.keysService.create(this.name().trim(), this.scopes(), expiresAt);
      this.createdKey.set(created.key);
      this.name.set('');
    } catch (error) {
      this.errorKey.set(this.keyFor(error));
    } finally {
      this.busy.set(false);
    }
  }

  async copy(key: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(key);
      this.toast.success(this.transloco.translate('apiKeys.copied'));
    } catch {
      this.toast.error(this.transloco.translate('apiKeys.copyFailed'));
    }
  }

  async remove(key: ApiKey): Promise<void> {
    this.confirmId.set(null);
    this.busy.set(true);
    try {
      await this.keysService.delete(key.id);
    } catch {
      this.toast.error(this.transloco.translate('apiKeys.deleteFailed'));
    } finally {
      this.busy.set(false);
    }
  }

  selectAll(event: Event): void {
    (event.target as HTMLInputElement).select();
  }

  expiryText(key: ApiKey): string {
    return key.expiresAt
      ? this.transloco.translate('apiKeys.expires', { date: this.formatDate(key.expiresAt) })
      : this.transloco.translate('apiKeys.noExpiry');
  }

  lastUsedText(key: ApiKey): string {
    return key.lastUsedAt
      ? this.transloco.translate('apiKeys.lastUsed', { date: this.formatDate(key.lastUsedAt) })
      : this.transloco.translate('apiKeys.neverUsed');
  }

  private formatDate(iso: string): string {
    return new Date(iso).toLocaleString(this.transloco.getActiveLang());
  }

  /** 409 = limit reached, 400 = invalid input; anything else is generic. Never the server's own text. */
  private keyFor(error: unknown): string {
    const status = error instanceof HttpErrorResponse ? error.status : 0;
    if (status === 409) return 'apiKeys.limit';
    if (status === 400) return 'apiKeys.invalid';
    return 'apiKeys.failed';
  }
}
