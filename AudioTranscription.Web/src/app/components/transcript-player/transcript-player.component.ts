import { Component, ElementRef, computed, effect, inject, input, signal, untracked, viewChild, viewChildren } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { AudioJobService } from '../../services/audio-job.service';
import { TranscriptSegment } from '../../models/audio-job.model';
import { findActiveSegment, formatTimestamp } from './segment-time';

/**
 * Audio player with the timed transcript: the segment being spoken is highlighted and kept in view,
 * clicking a segment jumps there. Without audio (upload already deleted) only the timed segments are shown.
 */
@Component({
  selector: 'app-transcript-player',
  standalone: true,
  imports: [TranslocoPipe],
  template: `
    <section class="card bg-base-200 shadow-sm mt-6" aria-labelledby="player-title">
      <div class="card-body gap-4">
        <h2 id="player-title" class="card-title text-lg">{{ 'player.title' | transloco }}</h2>

        @if (segments(); as list) {
          @if (list.length === 0) {
            <p class="text-sm text-base-content/70">{{ 'player.noSegments' | transloco }}</p>
          } @else {
            @if (audioAvailable()) {
              <audio
                #audio
                controls
                preload="metadata"
                class="w-full"
                [src]="audioUrl()"
                [attr.aria-label]="'player.audio' | transloco"
                (timeupdate)="onTimeUpdate()"
                (seeked)="onTimeUpdate()"
                (error)="audioAvailable.set(false)"
              ></audio>
            } @else {
              <p class="text-sm text-base-content/70">{{ 'player.noAudio' | transloco }}</p>
            }

            <ol
              #segmentList
              class="relative max-h-96 overflow-y-auto bg-base-100 rounded-lg p-2 space-y-1"
              [attr.aria-label]="'player.segments' | transloco"
            >
              @for (segment of list; track segment.index; let i = $index) {
                <li #segmentItem>
                  @if (audioAvailable()) {
                    <button
                      type="button"
                      class="w-full text-left flex gap-3 rounded-md px-2 py-1 hover:bg-base-200 focus-visible:outline-2 focus-visible:outline-primary"
                      [class.bg-primary/15]="i === activeIndex()"
                      [class.font-medium]="i === activeIndex()"
                      [attr.aria-current]="i === activeIndex() ? 'true' : null"
                      (click)="seek(segment)"
                    >
                      <time class="font-mono text-xs text-base-content/70 pt-0.5 shrink-0" [attr.datetime]="isoDuration(segment.startMs)">{{ timestamp(segment.startMs) }}</time>&ngsp;<span>{{ segment.text }}</span>
                    </button>
                  } @else {
                    <div class="flex gap-3 px-2 py-1">
                      <time class="font-mono text-xs text-base-content/70 pt-0.5 shrink-0" [attr.datetime]="isoDuration(segment.startMs)">{{ timestamp(segment.startMs) }}</time>&ngsp;<span>{{ segment.text }}</span>
                    </div>
                  }
                </li>
              }
            </ol>
          }
        } @else {
          <div class="skeleton h-24 w-full" role="status">
            <span class="sr-only">{{ 'player.loading' | transloco }}</span>
          </div>
        }
      </div>
    </section>
  `,
})
export class TranscriptPlayerComponent {
  readonly jobId = input.required<string>();

  private jobService = inject(AudioJobService);
  private audio = viewChild<ElementRef<HTMLAudioElement>>('audio');
  private segmentList = viewChild<ElementRef<HTMLElement>>('segmentList');
  private segmentItems = viewChildren<ElementRef<HTMLElement>>('segmentItem');

  /** null while loading. */
  readonly segments = signal<TranscriptSegment[] | null>(null);
  readonly audioAvailable = signal(true);
  private readonly currentMs = signal(0);
  readonly activeIndex = computed(() => findActiveSegment(this.segments() ?? [], this.currentMs()));
  readonly audioUrl = computed(() => this.jobService.audioUrl(this.jobId()));
  readonly timestamp = formatTimestamp;
  /** Machine-readable offset for &lt;time datetime&gt;, e.g. "PT65.5S". */
  isoDuration(ms: number): string {
    return `PT${ms / 1000}S`;
  }

  constructor() {
    effect((onCleanup) => {
      const id = this.jobId();
      untracked(() => {
        this.segments.set(null);
        this.audioAvailable.set(true);
        this.currentMs.set(0);
      });
      const subscription = this.jobService.loadSegments(id).subscribe({
        next: (segments) => this.segments.set(segments),
        // Segments are optional extras; the transcript above stays usable
        error: () => this.segments.set([]),
      });
      onCleanup(() => subscription.unsubscribe());
    });

    // Keep the active segment visible inside the list without scrolling the page
    effect(() => {
      const index = this.activeIndex();
      const list = this.segmentList()?.nativeElement;
      const item = untracked(() => this.segmentItems()[index]?.nativeElement);
      if (index < 0 || !list || !item) {
        return;
      }
      const top = item.offsetTop;
      const bottom = top + item.offsetHeight;
      if (top < list.scrollTop || bottom > list.scrollTop + list.clientHeight) {
        const reducedMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
        list.scrollTo?.({ top: Math.max(0, top - list.clientHeight / 3), behavior: reducedMotion ? 'auto' : 'smooth' });
      }
    });
  }

  onTimeUpdate(): void {
    const audio = this.audio()?.nativeElement;
    if (audio) {
      this.currentMs.set(audio.currentTime * 1000);
    }
  }

  seek(segment: TranscriptSegment): void {
    const audio = this.audio()?.nativeElement;
    if (audio) {
      audio.currentTime = segment.startMs / 1000;
    }
    this.currentMs.set(segment.startMs);
  }
}
