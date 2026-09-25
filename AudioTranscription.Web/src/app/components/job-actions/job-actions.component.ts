import { Component, ElementRef, computed, inject, input, signal, viewChild } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AudioJobStatus } from '../../models/audio-job.model';
import { AudioJobService } from '../../services/audio-job.service';
import { ToastService } from '../../services/toast.service';

export interface JobActionTarget {
  id: string;
  fileName: string;
  status: AudioJobStatus;
}

let nextId = 0;

/** Cancel, retry and delete actions for one job (S09). Buttons depend on the job status. */
@Component({
  selector: 'app-job-actions',
  standalone: true,
  imports: [TranslocoPipe],
  template: `
    <div class="flex items-center gap-1 flex-wrap">
      @if (canCancel()) {
        <button
          type="button"
          data-action="cancel"
          class="btn btn-sm"
          [class.btn-ghost]="compact()"
          [class.btn-square]="compact()"
          [disabled]="busy()"
          [attr.aria-label]="
            compact() ? ('jobActions.cancelFor' | transloco: { fileName: job().fileName }) : null
          "
          [title]="'jobActions.cancel' | transloco"
          (click)="cancel()"
        >
          <svg
            xmlns="http://www.w3.org/2000/svg"
            class="h-4 w-4"
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            aria-hidden="true"
          >
            <path
              stroke-linecap="round"
              stroke-linejoin="round"
              stroke-width="2"
              d="M18.364 18.364A9 9 0 005.636 5.636m12.728 12.728A9 9 0 015.636 5.636m12.728 12.728L5.636 5.636"
            />
          </svg>
          @if (!compact()) {
            {{ 'jobActions.cancel' | transloco }}
          }
        </button>
      }
      @if (canRetry()) {
        <button
          type="button"
          data-action="retry"
          class="btn btn-sm"
          [class.btn-ghost]="compact()"
          [class.btn-square]="compact()"
          [disabled]="busy()"
          [attr.aria-label]="
            compact() ? ('jobActions.retryFor' | transloco: { fileName: job().fileName }) : null
          "
          [title]="'jobActions.retry' | transloco"
          (click)="retry()"
        >
          <svg
            xmlns="http://www.w3.org/2000/svg"
            class="h-4 w-4"
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            aria-hidden="true"
          >
            <path
              stroke-linecap="round"
              stroke-linejoin="round"
              stroke-width="2"
              d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"
            />
          </svg>
          @if (!compact()) {
            {{ 'jobActions.retry' | transloco }}
          }
        </button>
      }
      <button
        type="button"
        data-action="delete"
        class="btn btn-sm"
        [class.btn-ghost]="compact()"
        [class.btn-square]="compact()"
        [class.btn-error]="!compact()"
        [class.btn-outline]="!compact()"
        [disabled]="busy()"
        [attr.aria-label]="
          compact() ? ('jobActions.deleteFor' | transloco: { fileName: job().fileName }) : null
        "
        [title]="'jobActions.delete' | transloco"
        (click)="confirmDelete()"
      >
        <svg
          xmlns="http://www.w3.org/2000/svg"
          class="h-4 w-4"
          fill="none"
          viewBox="0 0 24 24"
          stroke="currentColor"
          aria-hidden="true"
        >
          <path
            stroke-linecap="round"
            stroke-linejoin="round"
            stroke-width="2"
            d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"
          />
        </svg>
        @if (!compact()) {
          {{ 'jobActions.delete' | transloco }}
        }
      </button>
    </div>

    <!-- Native modal dialog: focus trap, Esc to close and focus return come from the browser -->
    <dialog
      #confirmDialog
      class="modal"
      [attr.aria-labelledby]="dialogId + '-title'"
      [attr.aria-describedby]="dialogId + '-text'"
    >
      <div class="modal-box">
        <h2 class="font-bold text-lg" [id]="dialogId + '-title'">
          {{ 'jobActions.confirmTitle' | transloco }}
        </h2>
        <p class="py-4" [id]="dialogId + '-text'">
          {{ 'jobActions.confirmText' | transloco: { fileName: job().fileName } }}
        </p>
        <div class="modal-action">
          <button type="button" class="btn" data-confirm="abort" (click)="closeDialog()">
            {{ 'jobActions.confirmAbort' | transloco }}
          </button>
          <button
            type="button"
            class="btn btn-error"
            data-confirm="delete"
            [disabled]="busy()"
            (click)="delete()"
          >
            {{ 'jobActions.confirmDelete' | transloco }}
          </button>
        </div>
      </div>
    </dialog>
  `,
})
export class JobActionsComponent {
  private jobService = inject(AudioJobService);
  private toast = inject(ToastService);
  private transloco = inject(TranslocoService);

  job = input.required<JobActionTarget>();
  /** Icon-only buttons (e.g. table rows); labels then include the file name. */
  compact = input(false);

  readonly dialogId = `job-actions-${nextId++}`;
  private dialog = viewChild.required<ElementRef<HTMLDialogElement>>('confirmDialog');

  busy = signal(false);
  canCancel = computed(() =>
    [AudioJobStatus.Pending, AudioJobStatus.Processing].includes(this.job().status),
  );
  canRetry = computed(() =>
    [AudioJobStatus.Failed, AudioJobStatus.Cancelled].includes(this.job().status),
  );

  async cancel(): Promise<void> {
    await this.run(async () => {
      await this.jobService.cancelJob(this.job().id);
      this.toast.show(this.transloco.translate('jobActions.cancelRequested'), 'info');
    });
  }

  async retry(): Promise<void> {
    await this.run(async () => {
      await this.jobService.retryJob(this.job().id);
      this.toast.success(this.transloco.translate('jobActions.retried'));
    });
  }

  confirmDelete(): void {
    this.dialog().nativeElement.showModal();
  }

  closeDialog(): void {
    this.dialog().nativeElement.close();
  }

  async delete(): Promise<void> {
    await this.run(async () => {
      // Pages react to AudioJobService.jobDeleted$; this component may already be gone by then
      await this.jobService.deleteJob(this.job().id);
      this.closeDialog();
      this.toast.success(this.transloco.translate('jobActions.deleted'));
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
    } catch (error: unknown) {
      const gone = error instanceof HttpErrorResponse && error.status === 410;
      this.toast.error(
        this.transloco.translate(gone ? 'jobActions.retryGone' : 'jobActions.failed'),
      );
    } finally {
      this.busy.set(false);
    }
  }
}
