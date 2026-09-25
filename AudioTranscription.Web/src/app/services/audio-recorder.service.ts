import { Injectable, signal } from '@angular/core';

export type RecorderState = 'idle' | 'recording' | 'stopped' | 'error';
export type RecorderErrorKind = 'permission-denied' | 'unsupported' | 'recording-failed';

/**
 * Wraps getUserMedia + MediaRecorder (S12): records a microphone take entirely in the browser,
 * picking whichever container format the browser actually supports.
 */
@Injectable({ providedIn: 'root' })
export class AudioRecorderService {
  /** Preferred first: Opus in WebM (Chrome/Firefox), then plain WebM, then MP4 (Safari). */
  private static readonly CANDIDATE_MIME_TYPES = ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4'];

  readonly state = signal<RecorderState>('idle');
  readonly errorKind = signal<RecorderErrorKind | null>(null);
  readonly recordedBlob = signal<Blob | null>(null);
  readonly mimeType = signal<string | null>(null);

  private mediaRecorder?: MediaRecorder;
  private stream?: MediaStream;
  private chunks: Blob[] = [];

  async start(): Promise<void> {
    this.errorKind.set(null);
    this.recordedBlob.set(null);
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

    this.mediaRecorder.ondataavailable = (event) => {
      if (event.data.size > 0) {
        this.chunks.push(event.data);
      }
    };
    this.mediaRecorder.onstop = () => {
      this.recordedBlob.set(new Blob(this.chunks, { type: this.mimeType() ?? 'audio/webm' }));
      this.releaseStream();
    };
    this.mediaRecorder.onerror = () => this.fail('recording-failed');

    this.mediaRecorder.start();
    this.state.set('recording');
  }

  stop(): void {
    if (this.state() !== 'recording') {
      return;
    }
    this.mediaRecorder?.stop();
    this.state.set('stopped');
  }

  /** Discards the current take (if any) and returns to idle, ready to record again. */
  reset(): void {
    this.releaseStream();
    this.mediaRecorder = undefined;
    this.chunks = [];
    this.recordedBlob.set(null);
    this.errorKind.set(null);
    this.state.set('idle');
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
