import { Injectable, inject, signal } from '@angular/core';
import { AuthService } from '../auth/auth.service';
import { isQuotaError } from './recording-store';
import { RECORDING_STORE } from './recording-store.provider';

export type RecorderState = 'idle' | 'recording' | 'stopped' | 'error';
export type RecorderErrorKind = 'permission-denied' | 'unsupported' | 'recording-failed';
/** The take could not be (fully) kept in the local store; recording itself is not affected (S33). */
export type StorageWarning = 'storage-full' | 'storage-failed';

/** A new section is handed over (and stored) this often, so a crash costs at most this much audio. */
export const RECORDING_SECTION_MS = 5000;

/**
 * Wraps getUserMedia + MediaRecorder (S12): records a microphone take entirely in the browser,
 * picking whichever container format the browser actually supports. Since S33 every section is also
 * written to the local store while recording, so a take survives a crash or a closed tab (ADR 0012).
 */
@Injectable({ providedIn: 'root' })
export class AudioRecorderService {
  /** Preferred first: Opus in WebM (Chrome/Firefox), then plain WebM, then MP4 (Safari). */
  private static readonly CANDIDATE_MIME_TYPES = ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4'];

  readonly state = signal<RecorderState>('idle');
  readonly errorKind = signal<RecorderErrorKind | null>(null);
  readonly recordedBlob = signal<Blob | null>(null);
  readonly mimeType = signal<string | null>(null);

  /** Id of the take in the local store; null while nothing is stored (not started, or the store failed). */
  readonly recordingId = signal<string | null>(null);
  readonly storageWarning = signal<StorageWarning | null>(null);

  private store = inject(RECORDING_STORE);
  private auth = inject(AuthService);
  /** Store writes run one after the other, in the order the sections arrive. */
  private writes: Promise<void> = Promise.resolve();
  private persisting = false;

  private mediaRecorder?: MediaRecorder;
  private stream?: MediaStream;
  private chunks: Blob[] = [];

  async start(): Promise<void> {
    this.errorKind.set(null);
    this.recordedBlob.set(null);
    this.storageWarning.set(null);
    this.chunks = [];

    if (typeof MediaRecorder === 'undefined' || !navigator.mediaDevices?.getUserMedia) {
      this.fail('unsupported');
      return;
    }

    try {
      this.stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch {
      this.fail('permission-denied');
      return;
    }

    const mimeType = AudioRecorderService.CANDIDATE_MIME_TYPES.find((type) => MediaRecorder.isTypeSupported(type));
    this.mimeType.set(mimeType ?? null);
    this.mediaRecorder = mimeType ? new MediaRecorder(this.stream, { mimeType }) : new MediaRecorder(this.stream);

    await this.openStoredRecording(this.mimeType() ?? 'audio/webm');

    this.mediaRecorder.ondataavailable = (event) => {
      if (event.data.size > 0) {
        this.chunks.push(event.data);
        this.persist(event.data);
      }
    };
    this.mediaRecorder.onstop = () => {
      this.recordedBlob.set(new Blob(this.chunks, { type: this.mimeType() ?? 'audio/webm' }));
      this.releaseStream();
      this.markDraft();
    };
    this.mediaRecorder.onerror = () => this.fail('recording-failed');

    this.mediaRecorder.start(RECORDING_SECTION_MS);
    this.state.set('recording');
  }

  stop(): void {
    if (this.state() !== 'recording') {
      return;
    }
    this.mediaRecorder?.stop();
    this.state.set('stopped');
  }

  /** Resolves when everything received so far is written to the local store. */
  flush(): Promise<void> {
    return this.writes;
  }

  /**
   * Discards the current take (if any) and returns to idle, ready to record again. The stored copy is deleted
   * as well, unless it moves on to the upload queue (keepStored).
   */
  reset(options: { keepStored?: boolean } = {}): void {
    const id = this.recordingId();
    if (id && !options.keepStored) {
      this.enqueueWrite(() => this.store.delete(id));
    }
    this.recordingId.set(null);
    this.persisting = false;
    this.releaseStream();
    this.mediaRecorder = undefined;
    this.chunks = [];
    this.recordedBlob.set(null);
    this.errorKind.set(null);
    this.storageWarning.set(null);
    this.state.set('idle');
  }

  private async openStoredRecording(mimeType: string): Promise<void> {
    this.recordingId.set(null);
    this.persisting = false;
    // Best effort: lets the browser keep the sections instead of clearing them when space gets short
    void navigator.storage?.persist?.().catch((error: unknown) => console.warn('Persistent storage was not granted', error));
    try {
      const stored = await this.store.create(this.auth.user()?.email ?? '', mimeType);
      this.recordingId.set(stored.id);
      this.persisting = true;
    } catch (error) {
      this.handleStorageError(error);
    }
  }

  private persist(chunk: Blob): void {
    const id = this.recordingId();
    if (!id || !this.persisting) {
      return;
    }
    this.enqueueWrite(() => (this.persisting ? this.store.appendChunk(id, chunk) : Promise.resolve()));
  }

  private markDraft(): void {
    const id = this.recordingId();
    if (id) {
      this.enqueueWrite(() => (this.persisting ? this.store.setStatus(id, 'draft') : Promise.resolve()));
    }
  }

  private enqueueWrite(write: () => Promise<void>): void {
    this.writes = this.writes.then(write).catch((error: unknown) => this.handleStorageError(error));
  }

  private handleStorageError(error: unknown): void {
    this.persisting = false;
    this.storageWarning.set(isQuotaError(error) ? 'storage-full' : 'storage-failed');
    if (isQuotaError(error)) {
      // Full: stop here with what we have rather than keep recording audio that cannot be saved
      this.stop();
    } else {
      console.error('Local recording storage failed', error);
    }
  }

  private fail(kind: RecorderErrorKind): void {
    this.releaseStream();
    this.errorKind.set(kind);
    this.state.set('error');
  }

  private releaseStream(): void {
    this.stream?.getTracks().forEach((track) => track.stop());
    this.stream = undefined;
  }
}
