import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable } from 'rxjs';
import * as tus from 'tus-js-client';
import { CreateAudioJobResponse, TranscriptionSettings } from '../models/audio-job.model';

/** From this size on an upload can be resumed after a broken connection (S27, ADR 0006). */
export const RESUMABLE_UPLOAD_THRESHOLD_BYTES = 10 * 1024 * 1024;
/** Chunk size, small enough for any reverse proxy's body limit. */
export const RESUMABLE_CHUNK_SIZE_BYTES = 5 * 1024 * 1024;
/** Waits before each automatic retry of a failed chunk (exponential-ish backoff). */
export const RESUMABLE_RETRY_DELAYS_MS = [0, 1_000, 3_000, 5_000, 10_000, 20_000];

export interface UploadProgress {
  progress: number;
  jobId?: string;
}

export const isResumableUpload = (file: File): boolean => file.size >= RESUMABLE_UPLOAD_THRESHOLD_BYTES;

interface Pausable {
  pause(): void;
  resume(): void;
}

/** Lets the UI pause and resume one resumable upload; stays inert for uploads that cannot be paused. */
export class UploadControl {
  readonly paused = signal(false);
  /** True once a resumable upload is attached. */
  readonly resumable = signal(false);
  private target?: Pausable;

  attach(target: Pausable): void {
    this.target = target;
    this.resumable.set(true);
  }

  detach(): void {
    this.target = undefined;
    this.resumable.set(false);
    this.paused.set(false);
  }

  pause(): void {
    if (this.target && !this.paused()) {
      this.target.pause();
      this.paused.set(true);
    }
  }

  resume(): void {
    if (this.target && this.paused()) {
      this.paused.set(false);
      this.target.resume();
    }
  }
}

/**
 * Uploads a file over the tus protocol (S27): in chunks, with automatic retries, resumable after a lost
 * connection or a pause. Emits the same shape as the plain upload and ends with the id of the created job.
 */
@Injectable({ providedIn: 'root' })
export class ResumableUploadService {
  private http = inject(HttpClient);

  upload(file: File, settings: TranscriptionSettings = {}, control?: UploadControl): Observable<UploadProgress> {
    return new Observable<UploadProgress>((subscriber) => {
      let finished = false;

      const upload = new tus.Upload(file, {
        endpoint: '/api/uploads',
        chunkSize: RESUMABLE_CHUNK_SIZE_BYTES,
        retryDelays: RESUMABLE_RETRY_DELAYS_MS,
        removeFingerprintOnSuccess: true,
        metadata: this.metadata(file, settings),
        onProgress: (sent, total) => subscriber.next({ progress: total ? Math.round((100 * sent) / total) : 0 }),
        onError: (error) => {
          finished = true;
          subscriber.error(this.toHttpLikeError(error));
        },
        onSuccess: () => {
          finished = true;
          const jobUrl = `${new URL(upload.url ?? '', window.location.origin).pathname}/job`;
          this.http.get<CreateAudioJobResponse>(jobUrl).subscribe({
            next: (job) => {
              subscriber.next({ progress: 100, jobId: job.id });
              subscriber.complete();
            },
            error: (error) => subscriber.error(error),
          });
        },
      });

      control?.attach({ pause: () => void upload.abort(false), resume: () => upload.start() });

      // Continue an earlier, interrupted upload of the same file if the browser still knows it
      void upload.findPreviousUploads().then((previous) => {
        if (subscriber.closed) {
          return;
        }
        if (previous.length > 0) {
          upload.resumeFromPreviousUpload(previous[0]);
        }
        upload.start();
      });

      return () => {
        control?.detach();
        // Leaving before the end means the user cancelled: drop the partial upload on the server too
        if (!finished) {
          void upload.abort(true);
        }
      };
    });
  }

  private metadata(file: File, settings: TranscriptionSettings): Record<string, string> {
    const metadata: Record<string, string> = { filename: file.name, filetype: file.type };
    if (settings.model) {
      metadata['model'] = settings.model;
    }
    if (settings.language) {
      metadata['language'] = settings.language;
    }
    if (settings.diarize) {
      metadata['diarize'] = 'true';
    }
    if (settings.glossaryId) {
      metadata['glossaryId'] = settings.glossaryId;
    }
    return metadata;
  }

  /**
   * Shapes a tus error like an HttpErrorResponse so the callers show it the same way. Only the server's own
   * message is passed on (it is written for users); technical client errors are not shown.
   */
  private toHttpLikeError(error: Error): { status: number; error: { detail?: string } } {
    const response = (error as { originalResponse?: tus.HttpResponse | null }).originalResponse;
    const status = response?.getStatus() ?? 0;
    const body = response?.getBody()?.trim();
    // Messages of the upload endpoint are short plain text; anything else (HTML error page of a proxy) stays hidden
    const readable = status >= 400 && status < 500 && !!body && body.length <= 300 && !body.startsWith('<');
    return { status, error: { detail: readable ? body : undefined } };
  }
}
