import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { WEBHOOK_EVENTS, Webhook, WebhookDelivery, WebhookEvent } from '../../models/automation.model';
import { WebhookService } from '../../services/webhook.service';
import { ToastService } from '../../services/toast.service';
import { PageTitleService } from '../../i18n/page-title.service';

/**
 * Manage the user's webhooks (S28): create/edit, switch on and off, send a test, read the delivery log. The signing
 * secret is shown exactly once, right after creation.
 */
@Component({
  selector: 'app-webhooks',
  standalone: true,
  imports: [FormsModule, TranslocoPipe],
  template: `
    <div class="max-w-3xl mx-auto space-y-8">
      <h1 class="text-3xl font-bold">{{ 'webhooks.title' | transloco }}</h1>
      <p class="text-sm text-base-content/70">{{ 'webhooks.intro' | transloco }}</p>

      @if (loadFailed()) {
        <div class="alert alert-error" role="alert">{{ 'webhooks.loadFailed' | transloco }}</div>
      }

      @if (createdSecret(); as secret) {
        <section
          class="alert alert-warning flex-col items-stretch gap-3"
          role="status"
          aria-labelledby="webhook-new-title"
          data-testid="webhook-new"
        >
          <h2 id="webhook-new-title" class="font-semibold">{{ 'webhooks.newSecretTitle' | transloco }}</h2>
          <p data-testid="webhook-once" class="text-sm">{{ 'webhooks.once' | transloco }}</p>
          <div class="flex gap-2">
            <input
              type="text"
              readonly
              class="input input-sm flex-1 font-mono"
              data-testid="webhook-secret"
              [value]="secret"
              [attr.aria-label]="'webhooks.secretLabel' | transloco"
              (focus)="selectAll($event)"
            />
            <button type="button" class="btn btn-sm" data-testid="webhook-copy" (click)="copy(secret)">
              {{ 'webhooks.copy' | transloco }}
            </button>
          </div>
          <div>
            <button type="button" class="btn btn-sm btn-primary" data-testid="webhook-dismiss" (click)="createdSecret.set(null)">
              {{ 'webhooks.dismiss' | transloco }}
            </button>
          </div>
        </section>
      }

      <form data-testid="webhook-form" class="space-y-3" (ngSubmit)="save()" novalidate>
        <h2 class="text-xl font-semibold">
          {{ (editingId() ? 'webhooks.editTitle' : 'webhooks.createTitle') | transloco }}
        </h2>
        <div class="flex flex-col gap-1">
          <label for="webhook-url" class="text-sm font-medium">{{ 'webhooks.url' | transloco }}</label>
          <input
            id="webhook-url"
            name="webhookUrl"
            type="url"
            data-testid="webhook-url"
            class="input w-full"
            maxlength="2048"
            autocomplete="off"
            aria-describedby="webhook-url-hint"
            [ngModel]="url()"
            (ngModelChange)="url.set($event)"
          />
          <p id="webhook-url-hint" class="text-xs text-base-content/60">{{ 'webhooks.urlHint' | transloco }}</p>
        </div>
        <fieldset class="space-y-1">
          <legend class="text-sm font-medium">{{ 'webhooks.events' | transloco }}</legend>
          @for (event of eventOptions; track event) {
            <label class="flex items-center gap-2 text-sm cursor-pointer">
              <input
                type="checkbox"
                class="checkbox checkbox-sm"
                [attr.data-testid]="'event-' + event"
                [checked]="events().includes(event)"
                (change)="toggleEvent(event)"
              />
              <span>{{ 'webhooks.event.' + event | transloco }}</span>
            </label>
          }
        </fieldset>
        @if (errorKey(); as key) {
          <p class="text-error text-sm" role="alert" data-testid="webhook-error">{{ key | transloco }}</p>
        }
        <div class="flex gap-2">
          <button type="submit" class="btn btn-primary btn-sm" data-testid="webhook-create" [disabled]="busy() || !canSave()">
            {{ (editingId() ? 'webhooks.save' : 'webhooks.create') | transloco }}
          </button>
          @if (editingId()) {
            <button type="button" class="btn btn-sm" (click)="cancelEdit()">{{ 'webhooks.cancel' | transloco }}</button>
          }
        </div>
      </form>

      <p
        data-testid="webhook-test-result"
        role="status"
        aria-live="polite"
        [class.alert]="testMessage() !== ''"
        [class.alert-info]="testMessage() !== ''"
      >{{ testMessage() }}</p>

      <section aria-labelledby="webhooks-list-title">
        <h2 id="webhooks-list-title" class="text-xl font-semibold mb-3">{{ 'webhooks.listTitle' | transloco }}</h2>
        @if (service.webhooks().length === 0) {
          <p class="text-sm text-base-content/60">{{ 'webhooks.empty' | transloco }}</p>
        } @else {
          <ul class="divide-y divide-base-300 rounded-box bg-base-200">
            @for (hook of service.webhooks(); track hook.id) {
              <li class="p-3 space-y-2">
                <div class="flex flex-wrap items-start justify-between gap-3">
                  <div class="min-w-0 space-y-1">
                    <div class="font-mono text-sm break-all">{{ hook.url }}</div>
                    <ul class="flex flex-wrap gap-1" [attr.aria-label]="'webhooks.events' | transloco">
                      @for (event of hook.events; track event) {
                        <li class="badge badge-outline">{{ 'webhooks.event.' + event | transloco }}</li>
                      }
                    </ul>
                  </div>
                  <label class="flex items-center gap-2 text-sm cursor-pointer shrink-0">
                    <input
                      type="checkbox"
                      class="toggle toggle-sm"
                      data-action="toggle-webhook"
                      [checked]="hook.isActive"
                      [disabled]="busy()"
                      (change)="setActive(hook, !hook.isActive)"
                    />
                    <span>{{ (hook.isActive ? 'webhooks.active' : 'webhooks.inactive') | transloco }}</span>
                  </label>
                </div>
                <div class="flex flex-wrap items-center gap-1">
                  <button
                    type="button" class="btn btn-ghost btn-xs" data-action="test-webhook" [disabled]="busy()"
                    [attr.aria-label]="'webhooks.testFor' | transloco: { url: hook.url }"
                    (click)="sendTest(hook)"
                  >{{ 'webhooks.test' | transloco }}</button>
                  <button
                    type="button" class="btn btn-ghost btn-xs" data-action="show-deliveries"
                    [attr.aria-expanded]="openId() === hook.id"
                    [attr.aria-controls]="'deliveries-' + hook.id"
                    [attr.aria-label]="'webhooks.historyFor' | transloco: { url: hook.url }"
                    (click)="toggleDeliveries(hook)"
                  >{{ 'webhooks.history' | transloco }}</button>
                  <button
                    type="button" class="btn btn-ghost btn-xs" data-action="edit-webhook"
                    [attr.aria-label]="'webhooks.editFor' | transloco: { url: hook.url }"
                    (click)="startEdit(hook)"
                  >{{ 'webhooks.edit' | transloco }}</button>
                  @if (confirmId() === hook.id) {
                    <span class="flex items-center gap-2">
                      <span class="text-sm">{{ 'webhooks.confirmDelete' | transloco }}</span>
                      <button type="button" class="btn btn-error btn-xs" data-action="confirm-delete-webhook" [disabled]="busy()" (click)="remove(hook)">{{ 'webhooks.delete' | transloco }}</button>
                      <button type="button" class="btn btn-xs" (click)="confirmId.set(null)">{{ 'webhooks.cancel' | transloco }}</button>
                    </span>
                  } @else {
                    <button
                      type="button" class="btn btn-ghost btn-xs" data-action="delete-webhook"
                      [attr.aria-label]="'webhooks.deleteFor' | transloco: { url: hook.url }"
                      (click)="confirmId.set(hook.id)"
                    >{{ 'webhooks.delete' | transloco }}</button>
                  }
                </div>
                @if (openId() === hook.id) {
                  <div [id]="'deliveries-' + hook.id" class="overflow-x-auto">
                    @if (historyFailed()) {
                      <p class="text-error text-sm" role="alert">{{ 'webhooks.historyFailed' | transloco }}</p>
                    } @else if (deliveries().length === 0) {
                      <p class="text-sm text-base-content/60">{{ 'webhooks.noDeliveries' | transloco }}</p>
                    } @else {
                      <table class="table table-xs">
                        <caption class="sr-only">{{ 'webhooks.history' | transloco }}</caption>
                        <thead>
                          <tr>
                            <th scope="col">{{ 'webhooks.columns.time' | transloco }}</th>
                            <th scope="col">{{ 'webhooks.columns.event' | transloco }}</th>
                            <th scope="col">{{ 'webhooks.columns.status' | transloco }}</th>
                            <th scope="col">{{ 'webhooks.columns.attempts' | transloco }}</th>
                            <th scope="col">{{ 'webhooks.columns.result' | transloco }}</th>
                          </tr>
                        </thead>
                        <tbody>
                          @for (d of deliveries(); track d.id) {
                            <tr data-testid="delivery-row">
                              <td>{{ formatDate(d.createdAt) }}</td>
                              <td>{{ d.event }}</td>
                              <td>
                                <span class="badge badge-sm" [class.badge-success]="d.status === 'succeeded'" [class.badge-error]="d.status === 'failed'" [class.badge-warning]="d.status === 'pending'">
                                  {{ 'webhooks.status.' + d.status | transloco }}
                                </span>
                              </td>
                              <td>{{ d.attempts }}</td>
                              <td>{{ resultText(d) }}</td>
                            </tr>
                          }
                        </tbody>
                      </table>
                    }
                  </div>
                }
              </li>
            }
          </ul>
        }
      </section>

      <section aria-labelledby="webhooks-verify-title" class="space-y-2">
        <h2 id="webhooks-verify-title" class="text-xl font-semibold">{{ 'webhooks.verifyTitle' | transloco }}</h2>
        <p class="text-sm text-base-content/70">{{ 'webhooks.verifyIntro' | transloco }}</p>
        <pre class="bg-base-200 rounded-box p-3 text-xs overflow-x-auto" tabindex="0"><code>X-Webhook-Signature: v1=HMAC_SHA256_HEX(secret, "&lt;X-Webhook-Timestamp&gt;.&lt;body&gt;")</code></pre>
      </section>
    </div>
  `,
})
export class WebhooksComponent implements OnInit {
  readonly service = inject(WebhookService);
  private toast = inject(ToastService);
  private transloco = inject(TranslocoService);
  private pageTitle = inject(PageTitleService);

  readonly eventOptions = WEBHOOK_EVENTS;

  readonly url = signal('');
  readonly events = signal<WebhookEvent[]>(['job.completed']);
  readonly busy = signal(false);
  readonly loadFailed = signal(false);
  readonly errorKey = signal<string | null>(null);
  readonly editingId = signal<string | null>(null);
  readonly confirmId = signal<string | null>(null);
  readonly openId = signal<string | null>(null);
  readonly deliveries = signal<WebhookDelivery[]>([]);
  readonly historyFailed = signal(false);
  readonly testMessage = signal('');
  /** The secret just created. Held only until the user dismisses it. */
  readonly createdSecret = signal<string | null>(null);

  readonly canSave = computed(() => this.url().trim().length > 0 && this.events().length > 0);

  async ngOnInit(): Promise<void> {
    this.pageTitle.set('webhooks.pageTitle');
    try {
      await this.service.load();
    } catch {
      this.loadFailed.set(true);
    }
  }

  toggleEvent(event: WebhookEvent): void {
    this.events.update((current) =>
      current.includes(event) ? current.filter((e) => e !== event) : WEBHOOK_EVENTS.filter((e) => e === event || current.includes(e)),
    );
  }

  startEdit(hook: Webhook): void {
    this.editingId.set(hook.id);
    this.url.set(hook.url);
    this.events.set([...hook.events]);
    this.errorKey.set(null);
  }

  cancelEdit(): void {
    this.editingId.set(null);
    this.url.set('');
    this.events.set(['job.completed']);
    this.errorKey.set(null);
  }

  async save(): Promise<void> {
    if (!this.canSave() || this.busy()) return;
    this.busy.set(true);
    this.errorKey.set(null);
    try {
      const url = this.url().trim();
      const id = this.editingId();
      if (id) {
        const current = this.service.webhooks().find((w) => w.id === id);
        await this.service.update(id, url, this.events(), current?.isActive ?? true);
      } else {
        const created = await this.service.create(url, this.events());
        this.createdSecret.set(created.secret);
      }
      this.cancelEdit();
    } catch (error) {
      this.errorKey.set(this.keyFor(error));
    } finally {
      this.busy.set(false);
    }
  }

  async setActive(hook: Webhook, active: boolean): Promise<void> {
    this.busy.set(true);
    try {
      await this.service.update(hook.id, hook.url, hook.events, active);
    } catch {
      this.toast.error(this.transloco.translate('webhooks.failed'));
    } finally {
      this.busy.set(false);
    }
  }

  async sendTest(hook: Webhook): Promise<void> {
    this.busy.set(true);
    this.testMessage.set('');
    try {
      const delivery = await this.service.test(hook.id);
      this.testMessage.set(
        delivery.status === 'succeeded'
          ? this.transloco.translate('webhooks.testOk', { status: delivery.lastStatusCode ?? '' })
          : this.transloco.translate('webhooks.testFailed', { reason: this.resultText(delivery) }),
      );
      if (this.openId() === hook.id) await this.loadDeliveries(hook.id);
    } catch (error) {
      const limited = error instanceof HttpErrorResponse && error.status === 429;
      this.testMessage.set(this.transloco.translate(limited ? 'webhooks.testLimited' : 'webhooks.testError'));
    } finally {
      this.busy.set(false);
    }
  }

  async toggleDeliveries(hook: Webhook): Promise<void> {
    if (this.openId() === hook.id) {
      this.openId.set(null);
      return;
    }
    this.openId.set(hook.id);
    await this.loadDeliveries(hook.id);
  }

  async remove(hook: Webhook): Promise<void> {
    this.confirmId.set(null);
    this.busy.set(true);
    try {
      await this.service.delete(hook.id);
      if (this.editingId() === hook.id) this.cancelEdit();
      if (this.openId() === hook.id) this.openId.set(null);
    } catch {
      this.toast.error(this.transloco.translate('webhooks.deleteFailed'));
    } finally {
      this.busy.set(false);
    }
  }

  async copy(secret: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(secret);
      this.toast.success(this.transloco.translate('webhooks.copied'));
    } catch {
      this.toast.error(this.transloco.translate('webhooks.copyFailed'));
    }
  }

  selectAll(event: Event): void {
    (event.target as HTMLInputElement).select();
  }

  formatDate(iso: string): string {
    return new Date(iso).toLocaleString(this.transloco.getActiveLang());
  }

  /** HTTP status and the short error class the server logged ("http 500", "timeout"); never a response body. */
  resultText(delivery: WebhookDelivery): string {
    return [delivery.lastStatusCode, delivery.lastError].filter((part) => part !== null && part !== '').join(' · ') || '–';
  }

  private async loadDeliveries(id: string): Promise<void> {
    this.historyFailed.set(false);
    try {
      this.deliveries.set(await this.service.deliveries(id));
    } catch {
      this.deliveries.set([]);
      this.historyFailed.set(true);
    }
  }

  /** 409 = limit reached, 400 = address or events refused; anything else is generic. Never the server's own text. */
  private keyFor(error: unknown): string {
    const status = error instanceof HttpErrorResponse ? error.status : 0;
    if (status === 409) return 'webhooks.limit';
    if (status === 400) return 'webhooks.invalid';
    return 'webhooks.failed';
  }
}
