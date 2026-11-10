import { Component, DestroyRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AudioJobService } from '../../services/audio-job.service';
import { ToastService } from '../../services/toast.service';
import { TranscriptionSettings } from '../../models/audio-job.model';
import { validateUploadFile } from '../upload/upload-validation';

type ItemStatus = 'waiting' | 'uploading' | 'done' | 'error' | 'cancelled';

interface BatchItem {
  readonly id: number;
  readonly file: File;
  status: ItemStatus;
  progress: number;
  error?: string;
}

/** Uploads run in parallel up to this limit; every file is its own streaming request (S34). */
const MAX_PARALLEL_UPLOADS = 3;

/**
 * Uploads several files with the same settings: a few at a time, progress per file, and a failure
 * of one file never stops the others.
 */
@Component({
  selector: 'app-upload-batch',
  standalone: true,
  imports: [RouterLink, TranslocoPipe],
  template: `
    <section [attr.aria-label]="'uploadBatch.title' | transloco">
      <div class="flex items-center justify-between gap-4 mb-4">
        <h2 class="text-xl font-semibold">{{ 'uploadBatch.title' | transloco }}</h2>
        @if (!settled()) {
          <button type="button" class="btn btn-sm btn-ghost" data-testid="batch-cancel-all" (click)="cancelAll()">
            {{ 'uploadBatch.cancelAll' | transloco }}
          </button>
        }
      </div>

      <!-- Screen readers hear the totals, not every progress tick -->
      <p class="text-sm mb-4" role="status" aria-live="polite" aria-atomic="true">
        {{ 'uploadBatch.summary' | transloco: { done: counts().done, total: items().length } }}
        @if (counts().error > 0) {
          · {{ 'uploadBatch.failedCount' | transloco: { count: counts().error } }}
        }
        @if (counts().cancelled > 0) {
          · {{ 'uploadBatch.cancelledCount' | transloco: { count: counts().cancelled } }}
        }
      </p>

      <ul class="flex flex-col gap-3">
        @for (item of items(); track item.id) {
          <li class="rounded-lg border border-base-300 p-3" data-testid="batch-item">
            <div class="flex items-center justify-between gap-3">
              <span class="font-medium truncate">{{ item.file.name }}</span>
              <span class="flex items-center gap-2 shrink-0">
                <span
                  class="badge"
                  data-testid="batch-status"
                  [class.badge-success]="item.status === 'done'"
                  [class.badge-error]="item.status === 'error'"
                  [class.badge-info]="item.status === 'uploading'"
                  [class.badge-ghost]="item.status === 'waiting' || item.status === 'cancelled'"
                  >{{ 'uploadBatch.status.' + item.status | transloco }}</span
                >
                @if (item.status === 'waiting' || item.status === 'uploading') {
                  <button
                    type="button"
                    class="btn btn-xs btn-ghost"
                    data-testid="batch-cancel"
                    [attr.aria-label]="'uploadBatch.cancelFile' | transloco: { name: item.file.name }"
                    (click)="cancel(item)"
                  >
                    {{ 'upload.cancel' | transloco }}
                  </button>
                }
              </span>
            </div>
            @if (item.status === 'uploading' || item.status === 'waiting') {
              <progress
                class="progress progress-primary w-full mt-2"
                [value]="item.progress"
                max="100"
                [attr.aria-label]="'uploadBatch.fileProgress' | transloco: { name: item.file.name }"
              ></progress>
            }
            @if (item.error) {
              <p class="text-sm text-error mt-1">{{ item.error }}</p>
            }
          </li>
        }
      </ul>

      @if (settled()) {
        <div class="flex flex-wrap gap-3 mt-6" data-testid="batch-done">
          <a routerLink="/queue" class="btn btn-primary">{{ 'uploadBatch.showQueue' | transloco }}</a>
          <a routerLink="/jobs" class="btn btn-ghost">{{ 'uploadBatch.showJobs' | transloco }}</a>
          <button type="button" class="btn btn-ghost" data-testid="batch-more" (click)="closed.emit()">
            {{ 'uploadBatch.uploadMore' | transloco }}
          </button>
        </div>
      }
    </section>
  `,
})
export class UploadBatchComponent implements OnInit {
  readonly files = input.required<File[]>();
  readonly settings = input<TranscriptionSettings>({});
  /** The user is done with the finished batch and wants to pick more files. */
  readonly closed = output<void>();

  private audioJobService = inject(AudioJobService);
  private toast = inject(ToastService);
  private transloco = inject(TranslocoService);

  readonly items = signal<BatchItem[]>([]);
  private subscriptions = new Map<number, Subscription>();
  private announced = false;

  readonly counts = computed(() => {
    const items = this.items();
    const count = (status: ItemStatus) => items.filter((i) => i.status === status).length;
    return { done: count('done'), error: count('error'), cancelled: count('cancelled') };
  });

  /** Nothing is waiting or uploading anymore. */
  readonly settled = computed(() => this.items().every((i) => i.status !== 'waiting' && i.status !== 'uploading'));

  constructor() {
    inject(DestroyRef).onDestroy(() => this.subscriptions.forEach((s) => s.unsubscribe()));
  }

  ngOnInit(): void {
    this.items.set(
      this.files().map((file, id) => {
        const rule = validateUploadFile(file);
        const error = rule
          ? this.transloco.translate(`upload.${rule}`, { size: this.audioJobService.formatFileSize(file.size) })
          : undefined;
        return { id, file, status: rule ? 'error' : 'waiting', progress: 0, error } satisfies BatchItem;
      }),
    );
    this.pump();
  }

  cancel(item: BatchItem): void {
    if (item.status !== 'waiting' && item.status !== 'uploading') return;
    this.subscriptions.get(item.id)?.unsubscribe();
    this.subscriptions.delete(item.id);
    this.update(item.id, { status: 'cancelled' });
    this.pump();
  }

  cancelAll(): void {
    // Mark everything first so freeing a slot does not start a file that is about to be cancelled too
    const open = this.items().filter((i) => i.status === 'waiting' || i.status === 'uploading');
    for (const item of open) {
      this.subscriptions.get(item.id)?.unsubscribe();
      this.subscriptions.delete(item.id);
    }
    const ids = new Set(open.map((i) => i.id));
    this.items.update((items) => items.map((i) => (ids.has(i.id) ? { ...i, status: 'cancelled' as const } : i)));
    this.announceWhenSettled();
  }

  /** Starts waiting files until the parallel limit is reached; announces the result once all are settled. */
  private pump(): void {
    let running = this.items().filter((i) => i.status === 'uploading').length;
    for (const item of this.items()) {
      if (running >= MAX_PARALLEL_UPLOADS) break;
      if (item.status === 'waiting') {
        this.start(item);
        running++;
      }
    }
    this.announceWhenSettled();
  }

  private start(item: BatchItem): void {
    this.update(item.id, { status: 'uploading' });
    const subscription = this.audioJobService.uploadFile(item.file, this.settings()).subscribe({
      next: (event) => {
        this.update(item.id, { progress: event.progress, ...(event.jobId ? { status: 'done' as const } : {}) });
        if (event.jobId) this.finish(item.id);
      },
      error: (err) => {
        const detail = err?.error?.detail || err?.error?.title || this.transloco.translate('upload.failed');
        this.update(item.id, { status: 'error', error: detail });
        this.finish(item.id);
      },
    });
    this.subscriptions.set(item.id, subscription);
  }

  private finish(id: number): void {
    this.subscriptions.get(id)?.unsubscribe();
    this.subscriptions.delete(id);
    this.pump();
  }

  private update(id: number, change: Partial<BatchItem>): void {
    this.items.update((items) => items.map((i) => (i.id === id ? { ...i, ...change } : i)));
  }

  private announceWhenSettled(): void {
    if (this.announced || !this.settled()) return;
    this.announced = true;
    const { done, error, cancelled } = this.counts();
    if (done > 0 && error === 0 && cancelled === 0) {
      this.toast.success(this.transloco.translate('uploadBatch.allDone', { count: done }));
    }
  }
}
