import { Component, DestroyRef, computed, effect, inject, output, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { AudioRecorderService } from '../../services/audio-recorder.service';

/**
 * Record a take directly in the browser (S12): start/stop, preview, then hand the recorded
 * file up to be transcribed like a normal upload, or discard it and record again.
 */
@Component({
  selector: 'app-recorder',
  standalone: true,
  imports: [TranslocoPipe],
  template: `
    <div class="card bg-base-200 shadow-sm">
      <div class="card-body gap-4">
        <div class="flex items-center gap-4">
          <button
            type="button"
            class="btn btn-circle btn-lg"
            [class.btn-error]="state() === 'recording'"
            [class.btn-primary]="state() !== 'recording'"
            [attr.aria-pressed]="state() === 'recording'"
            [attr.aria-label]="(state() === 'recording' ? 'recorder.stop' : 'recorder.start') | transloco"
            (click)="toggle()"
          >
            @if (state() === 'recording') {
              <span aria-hidden="true">&#9632;</span>
            } @else {
              <span aria-hidden="true">&#9679;</span>
            }
          </button>
          <div>
            <p class="font-medium">{{ (state() === 'recording' ? 'recorder.stop' : 'recorder.start') | transloco }}</p>
            <time class="font-mono text-sm text-base-content/70" [attr.datetime]="'PT' + elapsedSeconds() + 'S'">{{ elapsedLabel() }}</time>
          </div>
        </div>

        <p class="sr-only" aria-live="polite">{{ statusKey() | transloco }}</p>

        @if (state() === 'error') {
          <div class="alert alert-error">
            <span>{{ errorMessageKey() | transloco }}</span>
          </div>
        }

        @if (state() === 'stopped' && previewUrl(); as url) {
          <audio controls [src]="url" [attr.aria-label]="'recorder.preview' | transloco"></audio>
          <div class="flex gap-2">
            <button type="button" class="btn btn-primary" (click)="transcribe()">{{ 'recorder.transcribe' | transloco }}</button>
            <button type="button" class="btn btn-ghost" (click)="discard()">{{ 'recorder.discard' | transloco }}</button>
          </div>
        }

        <p class="text-xs text-base-content/60">{{ 'recorder.maxDurationHint' | transloco: { size: maxSizeLabel } }}</p>
      </div>
    </div>
  `,
})
export class RecorderComponent {
  private recorder = inject(AudioRecorderService);
  private destroyRef = inject(DestroyRef);

  /** Emits a File built from the recorded take, ready to be uploaded like a normal file. */
  readonly recorded = output<File>();

  readonly maxSizeLabel = '10 MB';

  readonly state = this.recorder.state;
  readonly previewUrl = signal<string | null>(null);
  readonly elapsedSeconds = signal(0);
  private timer?: ReturnType<typeof setInterval>;

  readonly errorMessageKey = computed(() => {
    switch (this.recorder.errorKind()) {
      case 'permission-denied':
        return 'recorder.error.permissionDenied';
      case 'unsupported':
        return 'recorder.error.unsupported';
      case 'recording-failed':
        return 'recorder.error.recordingFailed';
      default:
        return '';
    }
  });

  readonly statusKey = computed(() => {
    switch (this.state()) {
      case 'recording':
        return 'recorder.status.recording';
      case 'stopped':
        return 'recorder.status.stopped';
      case 'error':
        return this.errorMessageKey();
      default:
        return '';
    }
  });

  readonly elapsedLabel = computed(() => {
    const total = this.elapsedSeconds();
    const minutes = Math.floor(total / 60);
    const seconds = String(total % 60).padStart(2, '0');
    return `${minutes}:${seconds}`;
  });

  constructor() {
    // Stop the runtime clock as soon as we leave the recording state, however that happened
    effect(() => {
      if (this.state() !== 'recording' && this.timer) {
        clearInterval(this.timer);
        this.timer = undefined;
      }
    });

    // Keep exactly one object URL alive for the current take, and always release the previous one
    effect((onCleanup) => {
      const blob = this.recorder.recordedBlob();
      if (!blob) {
        this.previewUrl.set(null);
        return;
      }
      const url = URL.createObjectURL(blob);
      this.previewUrl.set(url);
      onCleanup(() => URL.revokeObjectURL(url));
    });

    this.destroyRef.onDestroy(() => {
      if (this.timer) {
        clearInterval(this.timer);
      }
      this.recorder.reset();
    });
  }

  async toggle(): Promise<void> {
    if (this.state() === 'recording') {
      this.recorder.stop();
      return;
    }
    this.elapsedSeconds.set(0);
    await this.recorder.start();
    if (this.state() === 'recording') {
      this.timer = setInterval(() => this.elapsedSeconds.update((s) => s + 1), 1000);
    }
  }

  transcribe(): void {
    const blob = this.recorder.recordedBlob();
    if (!blob) {
      return;
    }
    const extension = blob.type.includes('mp4') ? 'mp4' : 'webm';
    const file = new File([blob], `recording.${extension}`, { type: blob.type });
    this.recorded.emit(file);
    this.recorder.reset();
  }

  discard(): void {
    this.recorder.reset();
  }
}
