import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { QueueService } from '../../services/queue.service';
import { ToastService } from '../../services/toast.service';
import { PageTitleService } from '../../i18n/page-title.service';
import { AudioJobStatus, QueueEntry } from '../../models/audio-job.model';

/** The priority levels offered in the UI; the API accepts any whole number from -100 to 100. */
const PRIORITY_LEVELS = [
  { value: -10, key: 'queue.priority.low' },
  { value: 0, key: 'queue.priority.normal' },
  { value: 10, key: 'queue.priority.high' },
];

const REFRESH_INTERVAL_MS = 5000;

/** Own jobs in the transcription queue with position, estimated wait, priority and cancel (S34). */
@Component({
  selector: 'app-queue',
  standalone: true,
  imports: [RouterLink, TranslocoPipe],
  template: `
    <div class="max-w-4xl mx-auto">
      <h1 class="text-3xl font-bold mb-2">{{ 'queue.title' | transloco }}</h1>
      <p class="text-base-content/70 mb-6">{{ 'queue.intro' | transloco }}</p>

      @if (service.entries().length === 0) {
        <p class="text-base-content/60" role="status">{{ 'queue.empty' | transloco }}</p>
      } @else {
        <p class="text-sm text-base-content/60 mb-3" role="status">
          {{ 'queue.totalWaiting' | transloco: { count: service.totalWaiting() } }}
        </p>
        <div class="overflow-x-auto">
          <table class="table">
            <caption class="sr-only">{{ 'queue.title' | transloco }}</caption>
            <thead>
              <tr>
                <th scope="col">{{ 'queue.columns.position' | transloco }}</th>
                <th scope="col">{{ 'queue.columns.file' | transloco }}</th>
                <th scope="col">{{ 'queue.columns.priority' | transloco }}</th>
                <th scope="col">{{ 'queue.columns.wait' | transloco }}</th>
                <th scope="col"><span class="sr-only">{{ 'queue.columns.actions' | transloco }}</span></th>
              </tr>
            </thead>
            <tbody>
              @for (entry of service.entries(); track entry.jobId) {
                <tr>
                  <td>
                    @if (isRunning(entry)) {
                      <span class="badge badge-info">{{ 'queue.running' | transloco }}</span>
                    } @else {
                      <span data-testid="queue-position">{{ entry.position }}</span>
                    }
                  </td>
                  <td class="max-w-xs truncate">
                    <a [routerLink]="['/jobs', entry.jobId]" class="link">{{ entry.fileName }}</a>
                  </td>
                  <td>
                    <select
                      class="select select-sm"
                      data-testid="queue-priority"
                      [attr.aria-label]="'queue.priorityOf' | transloco: { name: entry.fileName }"
                      [disabled]="isRunning(entry)"
                      (change)="changePriority(entry, $any($event.target).value)"
                    >
                      @for (level of levelsFor(entry); track level.value) {
                        <option [value]="level.value" [selected]="level.value === entry.priority">
                          {{ level.key ? (level.key | transloco) : ('queue.priority.custom' | transloco: { value: level.value }) }}
                        </option>
                      }
                    </select>
                  </td>
                  <td data-testid="queue-wait">{{ isRunning(entry) ? '' : formatWait(entry.estimatedWaitSeconds) }}</td>
                  <td class="text-right">
                    <button
                      type="button"
                      class="btn btn-sm btn-ghost"
                      data-testid="queue-cancel"
                      [attr.aria-label]="'queue.cancelJob' | transloco: { name: entry.fileName }"
                      (click)="cancel(entry)"
                    >
                      {{ 'queue.cancel' | transloco }}
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>
  `,
})
export class QueueComponent implements OnInit {
  readonly service = inject(QueueService);
  private toast = inject(ToastService);
  private pageTitle = inject(PageTitleService);
  private transloco = inject(TranslocoService);
  private destroyRef = inject(DestroyRef);

  /** Set while a request runs so the periodic refresh does not overtake a user action. */
  private busy = signal(false);

  ngOnInit(): void {
    this.pageTitle.set('queue.pageTitle');
    void this.refresh();
    const timer = setInterval(() => void this.refresh(), REFRESH_INTERVAL_MS);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }

  isRunning(entry: QueueEntry): boolean {
    return entry.status === AudioJobStatus.Processing;
  }

  /** The offered levels plus the job's own value when the API set one outside them. */
  levelsFor(entry: QueueEntry): { value: number; key?: string }[] {
    return PRIORITY_LEVELS.some((l) => l.value === entry.priority)
      ? PRIORITY_LEVELS
      : [...PRIORITY_LEVELS, { value: entry.priority }];
  }

  formatWait(seconds: number | null): string {
    if (seconds === null) return '–';
    const minutes = Math.round(seconds / 60);
    if (minutes < 1) return this.transloco.translate('queue.lessThanMinute');
    if (minutes < 60) return this.transloco.translate('queue.minutes', { count: minutes });
    return this.transloco.translate('queue.hoursMinutes', { hours: Math.floor(minutes / 60), minutes: minutes % 60 });
  }

  async changePriority(entry: QueueEntry, value: string): Promise<void> {
    await this.run(() => this.service.setPriority(entry.jobId, Number(value)));
  }

  async cancel(entry: QueueEntry): Promise<void> {
    await this.run(() => this.service.cancel(entry.jobId));
  }

  private async refresh(): Promise<void> {
    if (this.busy()) return;
    try {
      await this.service.load();
    } catch {
      // A failed background refresh keeps the last known queue; the next tick tries again
    }
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
    } catch (error) {
      const key = error instanceof HttpErrorResponse && error.status === 409 ? 'queue.errors.started' : 'queue.errors.generic';
      this.toast.error(this.transloco.translate(key));
      try {
        await this.service.load();
      } catch {
        // The error toast above already told the user; the list refreshes on the next tick
      }
    } finally {
      this.busy.set(false);
    }
  }
}
