import { Component, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { NETWORK_ERROR, OfflineUploadQueueService } from '../../services/offline-upload-queue.service';
import { StoredRecording } from '../../services/recording-store';

/** The list "Waiting for upload" (S33): local recordings with progress, retry and delete. */
@Component({
  selector: 'app-pending-uploads',
  standalone: true,
  imports: [TranslocoPipe],
  template: `
    @if (queue.items().length > 0) {
      <section class="card bg-base-200 shadow-sm mt-6" aria-labelledby="pending-uploads-title">
        <div class="card-body gap-3">
          <h2 id="pending-uploads-title" class="card-title text-base">{{ 'offline.pending.title' | transloco }}</h2>
          <ul class="flex flex-col gap-3">
            @for (item of queue.items(); track item.id) {
              <li class="rounded-box bg-base-100 p-3 flex flex-col gap-2" [attr.data-status]="item.status">
                <div class="flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <p class="font-medium">{{ 'offline.pending.recordedAt' | transloco: { date: dateOf(item) } }}</p>
                    <p class="text-sm text-base-content/70">
                      {{ statusLabel(item) }}
                      <span class="ml-1">· {{ sizeOf(item) }}</span>
                    </p>
                  </div>
                  <div class="flex gap-2">
                    @if (item.status === 'failed') {
                      <button
                        type="button"
                        class="btn btn-sm btn-primary min-h-11"
                        data-action="retry"
                        [attr.aria-label]="'offline.pending.retryLabel' | transloco: { date: dateOf(item) }"
                        (click)="queue.retry(item.id)"
                      >
                        {{ 'offline.pending.retry' | transloco }}
                      </button>
                    }
                    @if (confirming() === item.id) {
                      <button
                        type="button"
                        class="btn btn-sm btn-error min-h-11"
                        data-action="confirm-delete"
                        [attr.aria-label]="'offline.pending.confirmDeleteLabel' | transloco: { date: dateOf(item) }"
                        (click)="remove(item.id)"
                      >
                        {{ 'offline.pending.confirmDelete' | transloco }}
                      </button>
                      <button
                        type="button"
                        class="btn btn-sm btn-ghost min-h-11"
                        data-action="cancel-delete"
                        [attr.aria-label]="('offline.pending.cancel' | transloco) + ' – ' + dateOf(item)"
                        (click)="confirming.set(null)"
                      >
                        {{ 'offline.pending.cancel' | transloco }}
                      </button>
                    } @else {
                      <button
                        type="button"
                        class="btn btn-sm btn-ghost min-h-11"
                        data-action="delete"
                        [attr.aria-label]="'offline.pending.deleteLabel' | transloco: { date: dateOf(item) }"
                        (click)="confirming.set(item.id)"
                      >
                        {{ 'offline.pending.delete' | transloco }}
                      </button>
                    }
                  </div>
                </div>
                @if (item.status === 'uploading') {
                  <progress
                    class="progress progress-primary w-full"
                    max="100"
                    [value]="queue.progress().get(item.id) ?? 0"
                    [attr.aria-label]="'offline.pending.progressLabel' | transloco: { date: dateOf(item) }"
                  ></progress>
                }
                @if (item.status === 'failed' && item.lastError) {
                  <p class="text-sm text-error">{{ item.lastError === networkError ? ('offline.pending.networkError' | transloco) : item.lastError }}</p>
                }
              </li>
            }
          </ul>
          <p class="text-xs text-base-content/60">{{ 'offline.pending.privacy' | transloco }}</p>
        </div>
      </section>
    }
  `,
})
export class PendingUploadsComponent {
  protected queue = inject(OfflineUploadQueueService);
  private transloco = inject(TranslocoService);

  protected readonly networkError = NETWORK_ERROR;
  /** The recording whose deletion waits for the second click. */
  protected readonly confirming = signal<string | null>(null);

  protected dateOf(item: StoredRecording): string {
    return new Date(item.createdAtUtc).toLocaleString(this.transloco.getActiveLang());
  }

  protected sizeOf(item: StoredRecording): string {
    return item.sizeBytes >= 1024 * 1024 ? `${(item.sizeBytes / (1024 * 1024)).toFixed(1)} MB` : `${Math.max(1, Math.round(item.sizeBytes / 1024))} KB`;
  }

  protected statusLabel(item: StoredRecording): string {
    const key = `offline.pending.status.${item.status}`;
    return this.transloco.translate(key, { percent: this.queue.progress().get(item.id) ?? 0 });
  }

  protected async remove(id: string): Promise<void> {
    this.confirming.set(null);
    await this.queue.remove(id);
  }
}
