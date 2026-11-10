import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { TranslocoPipe } from '@jsverse/transloco';
import { AudioJobService } from '../../services/audio-job.service';
import { SegmentVersion, TranscriptSegment } from '../../models/audio-job.model';
import { DiffPart, diffWords } from './text-diff';

interface HistoryEntry {
  version: SegmentVersion;
  isCurrent: boolean;
  /** Changes against the previous (older) version; undefined for the original. */
  diff?: DiffPart[];
}

/** Version history of one segment with a word diff per version and a restore action (S32). */
@Component({
  selector: 'app-segment-history',
  standalone: true,
  imports: [TranslocoPipe, DatePipe],
  template: `
    <div class="bg-base-200 rounded-md p-3 space-y-2" data-testid="segment-history">
      <h3 class="font-medium text-sm">{{ 'editor.history.title' | transloco }}</h3>
      @if (loading()) {
        <div class="skeleton h-10 w-full" role="status">
          <span class="sr-only">{{ 'editor.history.loading' | transloco }}</span>
        </div>
      } @else if (failed()) {
        <p role="alert" class="text-error text-sm">{{ 'editor.history.loadFailed' | transloco }}</p>
      } @else {
        <ol class="space-y-2">
          @for (entry of entries(); track entry.version.version) {
            <li class="border border-base-300 rounded-md p-2 bg-base-100" data-testid="history-entry">
              <div class="flex flex-wrap items-center gap-2 text-xs text-base-content/70">
                <span class="badge badge-sm">
                  @if (entry.version.version === 0) {
                    {{ 'editor.history.original' | transloco }}
                  } @else {
                    {{ 'editor.history.version' | transloco: { version: entry.version.version } }}
                  }
                </span>
                @if (entry.version.editedAtUtc) {
                  <time [attr.datetime]="entry.version.editedAtUtc">{{ entry.version.editedAtUtc | date: 'short' }}</time>
                }
                @if (entry.isCurrent) {
                  <span class="badge badge-sm badge-primary">{{ 'editor.history.current' | transloco }}</span>
                }
                @if (!entry.isCurrent && canRestore()) {
                  <button
                    type="button"
                    class="btn btn-xs ml-auto"
                    data-testid="history-restore"
                    [disabled]="restoring()"
                    [attr.aria-label]="'editor.history.restoreLabel' | transloco: { version: entry.version.version }"
                    (click)="restore(entry.version)"
                  >
                    {{ 'editor.history.restore' | transloco }}
                  </button>
                }
              </div>
              <p class="mt-1 text-sm" data-testid="history-text">
                @if (entry.diff; as parts) {
                  @for (part of parts; track $index) {
                    @switch (part.kind) {
                      @case ('added') {
                        <ins class="bg-success/20 no-underline rounded-sm" data-diff="added">{{ part.text }}</ins>
                      }
                      @case ('removed') {
                        <del class="bg-error/20 rounded-sm" data-diff="removed">{{ part.text }}</del>
                      }
                      @default {
                        <span>{{ part.text }}</span>
                      }
                    }
                  }
                } @else {
                  {{ entry.version.text }}
                }
              </p>
            </li>
          }
        </ol>
        @if (restoreError()) {
          <p role="alert" class="text-error text-sm">
            {{ (restoreError() === 'conflict' ? 'editor.error.conflict' : 'editor.error.failed') | transloco }}
          </p>
        }
      }
    </div>
  `,
})
export class SegmentHistoryComponent {
  readonly jobId = input.required<string>();
  readonly segment = input.required<TranscriptSegment>();
  /** Restoring needs owner access; readers only see the history. */
  readonly canRestore = input(true);
  /** Emits the segment as it is after a restore. */
  readonly restored = output<TranscriptSegment>();
  /** Emits when the history is stale because the segment changed elsewhere (conflict on restore). */
  readonly conflict = output<void>();

  private jobService = inject(AudioJobService);

  readonly loading = signal(true);
  readonly failed = signal(false);
  readonly restoring = signal(false);
  readonly restoreError = signal<'conflict' | 'failed' | null>(null);
  private readonly versions = signal<SegmentVersion[]>([]);

  /** Newest first; each correction is compared with the version before it. */
  readonly entries = computed<HistoryEntry[]>(() => {
    const sorted = [...this.versions()].sort((a, b) => b.version - a.version);
    return sorted.map((version, i) => {
      const older = sorted[i + 1];
      return {
        version,
        isCurrent: i === 0,
        diff: older ? diffWords(older.text, version.text) : undefined,
      };
    });
  });

  constructor() {
    // Reload whenever the segment's version changes (new edit, restore)
    effect((onCleanup) => {
      const segment = this.segment();
      void segment.version;
      untracked(() => {
        this.loading.set(true);
        this.failed.set(false);
      });
      const subscription = this.jobService.loadSegmentHistory(this.jobId(), segment.index).subscribe({
        next: (versions) => {
          this.versions.set(versions);
          this.loading.set(false);
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
        },
      });
      onCleanup(() => subscription.unsubscribe());
    });
  }

  restore(version: SegmentVersion): void {
    const segment = this.segment();
    this.restoring.set(true);
    this.restoreError.set(null);
    this.jobService.restoreSegment(this.jobId(), segment.index, version.version, segment.version ?? 0).subscribe({
      next: (restored) => {
        this.restoring.set(false);
        this.restored.emit(restored);
      },
      error: (error: { status?: number }) => {
        this.restoring.set(false);
        this.restoreError.set(error.status === 409 ? 'conflict' : 'failed');
        if (error.status === 409) {
          this.conflict.emit();
        }
      },
    });
  }
}
