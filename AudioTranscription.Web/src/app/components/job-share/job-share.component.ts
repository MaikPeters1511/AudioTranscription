import { Component, ElementRef, inject, input, signal, viewChild } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { JobShare } from '../../models/audio-job.model';
import { AudioJobService } from '../../services/audio-job.service';
import { ToastService } from '../../services/toast.service';

let nextId = 0;

/**
 * Lets the owner of a job share it read-only with other users by e-mail address and revoke it again
 * (S19, ADR 0007). Rendered only for the owner; the API enforces that anyway.
 */
@Component({
  selector: 'app-job-share',
  standalone: true,
  imports: [FormsModule, TranslocoPipe],
  template: `
    <button type="button" class="btn btn-sm" data-action="share" (click)="open()">
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
          d="M8.684 13.342C8.886 12.938 9 12.482 9 12c0-.482-.114-.938-.316-1.342m0 2.684a3 3 0 110-2.684m0 2.684l6.632 3.316m-6.632-6l6.632-3.316m0 0a3 3 0 105.367-2.684 3 3 0 00-5.367 2.684zm0 9.316a3 3 0 105.368 2.684 3 3 0 00-5.368-2.684z"
        />
      </svg>
      {{ 'jobShare.open' | transloco }}
    </button>

    <!-- Native modal dialog: focus trap, Esc to close and focus return come from the browser -->
    <dialog #shareDialog class="modal" [attr.aria-labelledby]="dialogId + '-title'">
      <div class="modal-box">
        <h2 class="font-bold text-lg" [id]="dialogId + '-title'">{{ 'jobShare.title' | transloco }}</h2>
        <p class="py-2 text-sm text-base-content/70">{{ 'jobShare.hint' | transloco }}</p>

        <form class="flex gap-2 items-end" (ngSubmit)="add()" novalidate>
          <div class="flex flex-col gap-1 flex-1">
            <label [for]="dialogId + '-email'" class="text-sm font-medium">{{ 'jobShare.email' | transloco }}</label>
            <input
              [id]="dialogId + '-email'"
              name="email"
              type="email"
              class="input w-full"
              autocomplete="off"
              [attr.aria-invalid]="error() ? true : null"
              [attr.aria-describedby]="error() ? dialogId + '-error' : null"
              [(ngModel)]="email"
            />
          </div>
          <button type="submit" class="btn btn-primary" [disabled]="busy()" [attr.aria-busy]="busy()">
            {{ 'jobShare.add' | transloco }}
          </button>
        </form>

        @if (error(); as key) {
          <p class="text-error text-sm mt-2" role="alert" [id]="dialogId + '-error'">{{ key | transloco }}</p>
        }

        <h3 class="font-medium mt-4 mb-1">{{ 'jobShare.sharedWith' | transloco }}</h3>
        @if (shares().length === 0) {
          <p class="text-sm text-base-content/60">{{ 'jobShare.none' | transloco }}</p>
        } @else {
          <ul class="divide-y divide-base-300">
            @for (share of shares(); track share.userId) {
              <li class="flex items-center justify-between py-2">
                <span class="truncate">{{ share.email }}</span>
                <button
                  type="button"
                  class="btn btn-ghost btn-sm"
                  data-action="revoke"
                  [disabled]="busy()"
                  [attr.aria-label]="'jobShare.revokeFor' | transloco: { email: share.email }"
                  (click)="revoke(share)"
                >
                  {{ 'jobShare.revoke' | transloco }}
                </button>
              </li>
            }
          </ul>
        }

        <div class="modal-action">
          <button type="button" class="btn" data-action="close" (click)="close()">
            {{ 'jobShare.close' | transloco }}
          </button>
        </div>
      </div>
    </dialog>
  `,
})
export class JobShareComponent {
  private jobService = inject(AudioJobService);
  private toast = inject(ToastService);
  private transloco = inject(TranslocoService);

  jobId = input.required<string>();

  readonly dialogId = `job-share-${nextId++}`;
  private dialog = viewChild.required<ElementRef<HTMLDialogElement>>('shareDialog');

  email = '';
  readonly shares = signal<JobShare[]>([]);
  readonly busy = signal(false);
  /** Translation key of the last error, shown inside the dialog. */
  readonly error = signal<string | null>(null);

  async open(): Promise<void> {
    this.error.set(null);
    this.email = '';
    this.dialog().nativeElement.showModal();
    await this.load();
  }

  close(): void {
    this.dialog().nativeElement.close();
  }

  async add(): Promise<void> {
    const email = this.email.trim();
    if (!email || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.jobService.addShare(this.jobId(), email);
      this.email = '';
      this.toast.success(this.transloco.translate('jobShare.added'));
      await this.load();
    } catch (error) {
      this.error.set(error instanceof HttpErrorResponse && error.status === 400 ? 'jobShare.error.unknownUser' : 'jobShare.error.generic');
    } finally {
      this.busy.set(false);
    }
  }

  async revoke(share: JobShare): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.jobService.removeShare(this.jobId(), share.userId);
      this.toast.success(this.transloco.translate('jobShare.revoked'));
      await this.load();
    } catch {
      this.error.set('jobShare.error.generic');
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.shares.set(await this.jobService.getShares(this.jobId()));
    } catch {
      this.error.set('jobShare.error.generic');
    }
  }
}
