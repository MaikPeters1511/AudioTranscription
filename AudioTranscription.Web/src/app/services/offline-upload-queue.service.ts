import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { TranslocoService } from '@jsverse/transloco';
import { Subscription } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { AudioJobService } from './audio-job.service';
import { AudioRecorderService } from './audio-recorder.service';
import { ConnectivityService } from './connectivity.service';
import { StoredRecording } from './recording-store';
import { RECORDING_STORE } from './recording-store.provider';
import { ToastService } from './toast.service';

/** A take that has not been written to for this long is taken for crashed (nobody is recording into it). */
export const OFFLINE_RECOVERY_IDLE_MS = 15_000;
/** `lastError` of a recording whose upload failed for lack of a connection (it is retried by itself). */
export const NETWORK_ERROR = 'network';

/**
 * Local recordings waiting for their upload (S33, ADR 0012). Uploads them one after the other as soon as the
 * browser is online and a user is signed in, through the normal upload path (so large files use the resumable
 * upload of S27), and deletes each one locally once the server has it.
 */
@Injectable({ providedIn: 'root' })
export class OfflineUploadQueueService {
  private store = inject(RECORDING_STORE);
  private auth = inject(AuthService);
  private connectivity = inject(ConnectivityService);
  private audioJobs = inject(AudioJobService);
  private recorder = inject(AudioRecorderService);
  private toast = inject(ToastService);
  private transloco = inject(TranslocoService);

  /** All local recordings of the signed-in user, whatever their state. */
  private readonly all = signal<StoredRecording[]>([]);
  /** The ones the user sees in the list "waiting for upload". */
  readonly items = computed(() => this.all().filter((r) => r.status === 'pending' || r.status === 'uploading' || r.status === 'failed'));
  readonly count = computed(() => this.items().length);
  /** Upload progress in percent of the recording being uploaded. */
  readonly progress = signal<ReadonlyMap<string, number>>(new Map());
  /** True once the stored recordings have been read for the first time. */
  readonly ready = signal(false);

  private active?: { id: string; subscription: Subscription; finish: () => void };
  /** The recording being uploaded, from the first step on (the upload itself only starts after reading it). */
  private uploadingId?: string;
  private recoveryTimer?: ReturnType<typeof setTimeout>;
  private processing = false;
  private processAgain = false;

  constructor() {
    effect(() => {
      const online = this.connectivity.online();
      const email = this.auth.user()?.email;
      // A change of connection or user is the moment to look again at what is waiting
      void this.contextChanged(online, email);
    });
  }

  /** Moves a stopped take into the queue and starts uploading when possible. */
  async enqueue(id: string): Promise<void> {
    await this.store.setStatus(id, 'pending');
    await this.refresh();
    void this.process();
  }

  async retry(id: string): Promise<void> {
    await this.enqueue(id);
  }

  /** Deletes a recording locally; a running upload of it is cancelled. */
  async remove(id: string): Promise<void> {
    if (this.active?.id === id) {
      this.active.subscription.unsubscribe();
      this.active.finish();
      this.active = undefined;
    }
    await this.store.delete(id);
    await this.refresh();
  }

  /** True when this user still has recordings on this device, finished or not (to warn before logout). */
  async hasLocalRecordings(): Promise<boolean> {
    await this.refresh();
    return this.all().length > 0;
  }

  /** Deletes every local recording of the signed-in user (logout, ADR 0012). */
  async discardAllOfCurrentUser(): Promise<void> {
    await this.refresh();
    for (const recording of this.all()) {
      await this.remove(recording.id);
    }
  }

  async refresh(): Promise<void> {
    const email = this.auth.user()?.email;
    const recordings = email ? (await this.store.list()).filter((r) => r.userEmail === email) : [];
    this.all.set(recordings.sort((a, b) => a.createdAtUtc.localeCompare(b.createdAtUtc)));
  }

  private async contextChanged(online: boolean, email: string | undefined): Promise<void> {
    if (!email) {
      this.all.set([]);
      this.ready.set(true);
      return;
    }
    try {
      if (await this.recoverInterrupted(email)) {
        // A take is still being written to, or was cut off just now: look again once it would count as idle
        clearTimeout(this.recoveryTimer);
        this.recoveryTimer = setTimeout(() => void this.contextChanged(this.connectivity.online(), this.auth.user()?.email), OFFLINE_RECOVERY_IDLE_MS + 1000);
      }
      if (online) {
        await this.retryNetworkFailures(email);
      }
      await this.refresh();
    } catch (error) {
      console.error('Reading the local recordings failed', error);
    }
    this.ready.set(true);
    if (online) {
      void this.process();
    }
  }

  /** Takes that were cut off (crash, closed tab, reload during upload) go to the queue instead of being lost. */
  private async recoverInterrupted(email: string): Promise<boolean> {
    let waiting = false;
    const now = Date.now();
    const current = this.recorder.recordingId();
    for (const recording of await this.store.list()) {
      const idle = now - Date.parse(recording.updatedAtUtc);
      const interrupted =
        recording.userEmail === email &&
        recording.id !== current &&
        recording.id !== this.uploadingId &&
        ((recording.status === 'recording' && idle > OFFLINE_RECOVERY_IDLE_MS) || recording.status === 'draft' || recording.status === 'uploading');
      if (interrupted) {
        await this.store.setStatus(recording.id, 'pending');
      } else if (recording.userEmail === email && recording.status === 'recording' && recording.id !== current) {
        waiting = true;
      }
    }
    return waiting;
  }

  private async retryNetworkFailures(email: string): Promise<void> {
    for (const recording of await this.store.list()) {
      if (recording.userEmail === email && recording.status === 'failed' && recording.lastError === NETWORK_ERROR) {
        await this.store.setStatus(recording.id, 'pending');
      }
    }
  }

  private async process(): Promise<void> {
    if (this.processing) {
      this.processAgain = true;
      return;
    }
    this.processing = true;
    try {
      do {
        this.processAgain = false;
        while (this.connectivity.online() && this.auth.user()) {
          await this.refresh();
          const next = this.all().find((r) => r.status === 'pending');
          if (!next) {
            break;
          }
          await this.upload(next);
        }
      } while (this.processAgain);
    } catch (error) {
      console.error('Uploading the local recordings failed', error);
    } finally {
      this.processing = false;
    }
  }

  private async upload(recording: StoredRecording): Promise<void> {
    this.uploadingId = recording.id;
    try {
      await this.uploadRecording(recording);
    } finally {
      this.uploadingId = undefined;
    }
  }

  private async uploadRecording(recording: StoredRecording): Promise<void> {
    await this.store.setStatus(recording.id, 'uploading');
    await this.refresh();
    const blob = await this.store.assemble(recording.id);
    if (!blob || blob.size === 0) {
      await this.store.delete(recording.id); // nothing was ever captured, there is nothing to keep
      return;
    }
    const extension = recording.mimeType.includes('mp4') ? 'mp4' : 'webm';
    const stamp = recording.createdAtUtc.replace(/[:.]/g, '-');
    const file = new File([blob], `recording-${stamp}.${extension}`, { type: recording.mimeType });

    const outcome = await new Promise<{ ok: true } | { ok: false; error: unknown } | { ok: 'cancelled' }>((resolve) => {
      let jobId: string | undefined;
      const subscription = this.audioJobs.uploadFile(file).subscribe({
        next: (event) => {
          this.setProgress(recording.id, event.progress);
          jobId = event.jobId ?? jobId;
        },
        error: (error: unknown) => resolve({ ok: false, error }),
        complete: () => resolve(jobId ? { ok: true } : { ok: false, error: new Error('The upload ended without a job') }),
      });
      this.active = { id: recording.id, subscription, finish: () => resolve({ ok: 'cancelled' }) };
    });
    this.active = undefined;
    this.setProgress(recording.id, undefined);

    if (outcome.ok === 'cancelled') {
      return; // remove() takes care of the rest
    }
    if (outcome.ok === true) {
      await this.store.delete(recording.id);
      this.toast.success(this.transloco.translate('offline.uploaded'));
      return;
    }
    await this.handleFailure(recording.id, outcome.error);
  }

  private async handleFailure(id: string, error: unknown): Promise<void> {
    const status = error instanceof HttpErrorResponse ? error.status : undefined;
    if (status === 401) {
      // The session ended; the recording waits for the next sign-in (the interceptor already leads to the login)
      await this.store.setStatus(id, 'pending');
      return;
    }
    if (status === 0) {
      await this.store.setStatus(id, 'failed', NETWORK_ERROR);
      return;
    }
    const detail = error instanceof HttpErrorResponse ? (error.error?.detail ?? error.error?.title) : undefined;
    await this.store.setStatus(id, 'failed', typeof detail === 'string' && detail ? detail : this.transloco.translate('upload.failed'));
  }

  private setProgress(id: string, percent: number | undefined): void {
    this.progress.update((current) => {
      const next = new Map(current);
      if (percent === undefined) {
        next.delete(id);
      } else {
        next.set(id, percent);
      }
      return next;
    });
  }
}
