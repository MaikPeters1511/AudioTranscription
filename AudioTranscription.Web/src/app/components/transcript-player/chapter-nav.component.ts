import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { ChapterService } from '../../services/chapter.service';
import { Chapter } from '../../models/audio-job.model';
import { formatTimestamp } from './segment-time';

/**
 * Chapters of a recording (S25): markers on a timeline and a list to jump to a topic. Owners can generate, rename
 * and delete chapters; readers of a shared job only navigate.
 */
@Component({
  selector: 'app-chapter-nav',
  standalone: true,
  imports: [TranslocoPipe],
  template: `
    @if (visible()) {
      <section class="space-y-2" [attr.aria-label]="'chapters.title' | transloco" data-testid="chapter-nav">
        <div class="flex items-center justify-between gap-2">
          <h3 class="font-medium">{{ 'chapters.title' | transloco }}</h3>
          @if (canEdit() && status() !== 'Pending') {
            <button type="button" class="btn btn-xs" data-testid="chapter-generate" [disabled]="starting()" (click)="generate()">
              {{ (chapters().length > 0 ? 'chapters.regenerate' : 'chapters.generate') | transloco }}
            </button>
          }
        </div>

        @if (status() === 'Pending') {
          <p role="status" class="text-sm text-base-content/70">
            <span class="loading loading-spinner loading-xs mr-1" aria-hidden="true"></span>{{ 'chapters.pending' | transloco }}
          </p>
        }
        @if (status() === 'Failed') {
          <p role="alert" class="text-error text-sm">{{ 'chapters.failed' | transloco }}</p>
        }
        @if (error()) {
          <p role="alert" class="text-error text-sm">{{ (error() === 'unavailable' ? 'chapters.unavailable' : 'chapters.error') | transloco }}</p>
        }

        @if (chapters().length > 0) {
          <!-- Timeline: one marker per chapter, positioned by its start; the same actions as the list below -->
          <ol class="relative h-4 rounded bg-base-300" [attr.aria-label]="'chapters.timeline' | transloco">
            @for (chapter of chapters(); track chapter.id; let i = $index) {
              <li class="contents">
                <button
                  type="button"
                  data-testid="chapter-marker"
                  class="absolute top-0 h-4 w-1.5 rounded bg-primary hover:bg-primary-content focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
                  [class.opacity-100]="i === activeIndex()"
                  [class.opacity-60]="i !== activeIndex()"
                  [style.left.%]="(chapter.startMs / totalMs()) * 100"
                  [attr.aria-label]="'chapters.jumpTo' | transloco: { title: chapter.title, time: timestamp(chapter.startMs) }"
                  (click)="seek.emit(chapter.startMs)"
                ></button>
              </li>
            }
          </ol>

          <ol class="space-y-1">
            @for (chapter of chapters(); track chapter.id; let i = $index) {
              <li class="rounded-md border border-base-300 bg-base-100 p-2" data-testid="chapter-row">
                @if (editingId() === chapter.id) {
                  <div class="space-y-2">
                    <label class="sr-only" [for]="'chapter-title-' + chapter.id">{{ 'chapters.titleLabel' | transloco }}</label>
                    <input
                      [id]="'chapter-title-' + chapter.id"
                      data-testid="chapter-title-input"
                      type="text"
                      class="input input-sm input-bordered w-full"
                      maxlength="200"
                      [value]="draftTitle()"
                      (input)="draftTitle.set($any($event.target).value)"
                      (keydown.escape)="cancelEdit()"
                    />
                    <label class="sr-only" [for]="'chapter-summary-' + chapter.id">{{ 'chapters.summaryLabel' | transloco }}</label>
                    <textarea
                      [id]="'chapter-summary-' + chapter.id"
                      class="textarea textarea-bordered textarea-sm w-full"
                      rows="2"
                      maxlength="1000"
                      [value]="draftSummary()"
                      (input)="draftSummary.set($any($event.target).value)"
                      (keydown.escape)="cancelEdit()"
                    ></textarea>
                    <div class="flex gap-2">
                      <button type="button" class="btn btn-xs btn-primary" data-testid="chapter-save" [disabled]="!draftTitle().trim() || saving()" (click)="save(chapter)">
                        {{ 'chapters.save' | transloco }}
                      </button>
                      <button type="button" class="btn btn-xs" (click)="cancelEdit()">{{ 'chapters.cancel' | transloco }}</button>
                    </div>
                  </div>
                } @else {
                  <div class="flex items-start gap-2">
                    <button
                      type="button"
                      data-testid="chapter-jump"
                      class="grow text-left rounded px-1 hover:bg-base-200 focus-visible:outline-2 focus-visible:outline-primary"
                      [attr.aria-current]="i === activeIndex() ? 'true' : null"
                      (click)="seek.emit(chapter.startMs)"
                    >
                      <span class="font-mono text-xs text-base-content/70 mr-2">{{ timestamp(chapter.startMs) }}</span>
                      <span [class.font-semibold]="i === activeIndex()">{{ chapter.title }}</span>
                      @if (chapter.summary) {
                        <span class="block text-sm text-base-content/70">{{ chapter.summary }}</span>
                      }
                    </button>
                    @if (canEdit()) {
                      <button type="button" class="btn btn-ghost btn-xs px-1" data-testid="chapter-edit"
                        [attr.aria-label]="'chapters.edit' | transloco: { title: chapter.title }" (click)="startEdit(chapter)">✎</button>
                      @if (confirmDeleteId() === chapter.id) {
                        <button type="button" class="btn btn-error btn-xs" data-testid="chapter-delete-confirm"
                          [attr.aria-label]="'chapters.deleteConfirmLabel' | transloco: { title: chapter.title }" (click)="remove(chapter)">
                          {{ 'chapters.deleteConfirm' | transloco }}
                        </button>
                        <button type="button" class="btn btn-ghost btn-xs" (click)="confirmDeleteId.set(null)">{{ 'chapters.cancel' | transloco }}</button>
                      } @else {
                        <button type="button" class="btn btn-ghost btn-xs px-1" data-testid="chapter-delete"
                          [attr.aria-label]="'chapters.delete' | transloco: { title: chapter.title }" (click)="confirmDeleteId.set(chapter.id)">🗑</button>
                      }
                    }
                  </div>
                }
              </li>
            }
          </ol>
        }
      </section>
    }
  `,
})
export class ChapterNavComponent {
  readonly jobId = input.required<string>();
  /** Owners may generate, rename and delete chapters. */
  readonly canEdit = input(false);
  /** Current playback position, for highlighting the chapter being played. */
  readonly currentMs = input(0);
  /** Asks the player to jump to this time. */
  readonly seek = output<number>();

  private service = inject(ChapterService);

  readonly status = computed(() => this.service.state().status);
  readonly chapters = computed(() => this.service.state().chapters);
  readonly totalMs = computed(() => Math.max(1, this.chapters().at(-1)?.endMs ?? 1));
  readonly activeIndex = computed(() => {
    const now = this.currentMs();
    return this.chapters().reduce((active, c, i) => (c.startMs <= now ? i : active), -1);
  });
  /** Readers only see the section when there is something to read; owners can always start the generation. */
  readonly visible = computed(() => this.canEdit() || this.chapters().length > 0 || this.status() === 'Pending');

  readonly timestamp = formatTimestamp;
  readonly starting = signal(false);
  readonly error = signal<'unavailable' | 'failed' | null>(null);
  readonly editingId = signal<string | null>(null);
  readonly draftTitle = signal('');
  readonly draftSummary = signal('');
  readonly saving = signal(false);
  readonly confirmDeleteId = signal<string | null>(null);

  constructor() {
    effect(() => {
      const id = this.jobId();
      untracked(() => {
        this.error.set(null);
        this.editingId.set(null);
        this.confirmDeleteId.set(null);
        this.service.load(id);
      });
    });
  }

  generate(): void {
    this.starting.set(true);
    this.error.set(null);
    this.service.generate(this.jobId()).subscribe({
      next: () => this.starting.set(false),
      error: (err: { status?: number }) => {
        this.starting.set(false);
        this.error.set(err.status === 503 ? 'unavailable' : 'failed');
      },
    });
  }

  startEdit(chapter: Chapter): void {
    this.editingId.set(chapter.id);
    this.draftTitle.set(chapter.title);
    this.draftSummary.set(chapter.summary);
    this.confirmDeleteId.set(null);
  }

  cancelEdit(): void {
    this.editingId.set(null);
  }

  save(chapter: Chapter): void {
    const title = this.draftTitle().trim();
    if (!title) {
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.service.update(this.jobId(), chapter, title, this.draftSummary().trim()).subscribe({
      next: () => {
        this.saving.set(false);
        this.editingId.set(null);
      },
      error: () => {
        this.saving.set(false);
        this.error.set('failed');
      },
    });
  }

  remove(chapter: Chapter): void {
    this.confirmDeleteId.set(null);
    this.error.set(null);
    this.service.delete(this.jobId(), chapter).subscribe({ error: () => this.error.set('failed') });
  }
}
