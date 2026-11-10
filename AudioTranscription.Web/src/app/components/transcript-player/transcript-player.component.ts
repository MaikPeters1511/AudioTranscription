import { Component, ElementRef, computed, effect, inject, input, signal, untracked, viewChild, viewChildren } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { AudioJobService } from '../../services/audio-job.service';
import { JobSpeaker, TranscriptSegment } from '../../models/audio-job.model';
import { findActiveSegment, formatTimestamp } from './segment-time';
import { speakerBadgeClass } from './speaker-color';
import { SegmentEditState } from './segment-edit-state';
import { SegmentHistoryComponent } from './segment-history.component';
import { ChapterNavComponent } from './chapter-nav.component';

/**
 * Audio player with the timed transcript: the segment being spoken is highlighted and kept in view,
 * clicking a segment jumps there. Without audio (upload already deleted) only the timed segments are shown.
 */
@Component({
  selector: 'app-transcript-player',
  standalone: true,
  imports: [TranslocoPipe, SegmentHistoryComponent, ChapterNavComponent],
  template: `
    <section class="card bg-base-200 shadow-sm mt-6" aria-labelledby="player-title">
      <div class="card-body gap-4">
        <h2 id="player-title" class="card-title text-lg">{{ 'player.title' | transloco }}</h2>

        @if (segments(); as list) {
          @if (list.length === 0) {
            <p class="text-sm text-base-content/70">{{ 'player.noSegments' | transloco }}</p>
          } @else {
            @if (speakers().length > 0) {
              <div data-testid="speaker-toolbar" class="flex flex-wrap gap-2" role="group" [attr.aria-label]="'player.speakers' | transloco">
                @for (speaker of speakers(); track speaker.index) {
                  @if (editingSpeakerIndex() === speaker.index) {
                    <span class="join">
                      <input
                        data-testid="speaker-rename-input"
                        type="text"
                        class="input input-sm join-item"
                        maxlength="100"
                        [attr.aria-label]="'player.renameInputLabel' | transloco: { name: speaker.displayName }"
                        [value]="editingName()"
                        (input)="editingName.set($any($event.target).value)"
                        (keydown.enter)="saveEdit(speaker)"
                        (keydown.escape)="cancelEdit()"
                      />
                      <button
                        type="button"
                        data-testid="speaker-save"
                        class="btn btn-sm btn-primary join-item"
                        [disabled]="!editingName().trim()"
                        (click)="saveEdit(speaker)"
                      >
                        {{ 'player.save' | transloco }}
                      </button>
                      <button type="button" class="btn btn-sm join-item" (click)="cancelEdit()">
                        {{ 'player.cancel' | transloco }}
                      </button>
                    </span>
                  } @else {
                    <span class="badge {{ speakerBadgeClass(speaker.index) }} gap-1 py-3">
                      {{ speaker.displayName }}
                      <button
                        type="button"
                        class="btn btn-ghost btn-xs px-1"
                        [attr.aria-label]="'player.renameSpeaker' | transloco: { name: speaker.displayName }"
                        (click)="startEdit(speaker)"
                      >
                        ✎
                      </button>
                    </span>
                  }
                }
              </div>
            }
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

            <app-chapter-nav [jobId]="jobId()" [canEdit]="canEdit()" [currentMs]="currentMs()" (seek)="seekToMs($event)" />

            <ol
              #segmentList
              class="relative max-h-96 overflow-y-auto bg-base-100 rounded-lg p-2 space-y-1"
              [attr.aria-label]="'player.segments' | transloco"
            >
              @for (segment of list; track segment.index; let i = $index) {
                <li #segmentItem>
                  @if (edit.index() === segment.index) {
                    <div class="rounded-md border border-primary bg-base-100 p-2 space-y-2">
                      <label class="sr-only" [for]="'segment-editor-' + segment.index">
                        {{ 'editor.textLabel' | transloco: { time: timestamp(segment.startMs) } }}
                      </label>
                      <textarea
                        data-testid="segment-editor"
                        class="textarea textarea-bordered w-full"
                        rows="3"
                        [id]="'segment-editor-' + segment.index"
                        [attr.aria-describedby]="'segment-editor-help-' + segment.index"
                        [value]="edit.draft()"
                        [disabled]="edit.saving()"
                        (input)="edit.draft.set($any($event.target).value)"
                        (keydown)="onEditorKeydown($event, segment)"
                      ></textarea>
                      <p class="text-xs text-base-content/70" [id]="'segment-editor-help-' + segment.index">
                        {{ 'editor.shortcuts' | transloco }}
                      </p>
                      @if (edit.error(); as error) {
                        <p role="alert" class="text-error text-sm">
                          {{ (error === 'conflict' ? 'editor.error.conflict' : 'editor.error.failed') | transloco }}
                        </p>
                      }
                      <div class="flex flex-wrap gap-2">
                        <button
                          type="button"
                          data-testid="editor-save"
                          class="btn btn-sm btn-primary"
                          [disabled]="!edit.canSave()"
                          (click)="saveSegmentEdit(segment)"
                        >
                          {{ (edit.saving() ? 'editor.saving' : 'editor.save') | transloco }}
                        </button>
                        <button type="button" data-testid="editor-cancel" class="btn btn-sm" (click)="edit.cancel()">
                          {{ 'editor.cancel' | transloco }}
                        </button>
                        @if (audioAvailable()) {
                          <button type="button" data-testid="editor-play" class="btn btn-sm btn-ghost" (click)="play(segment)">
                            {{ 'editor.play' | transloco }}
                          </button>
                        }
                      </div>
                    </div>
                  } @else {
                    <div class="flex items-start gap-1">
                      @if (audioAvailable()) {
                        <button
                          type="button"
                          data-testid="segment-text"
                          class="grow text-left flex gap-3 rounded-md px-2 py-1 hover:bg-base-200 focus-visible:outline-2 focus-visible:outline-primary"
                          [class.bg-primary/15]="i === activeIndex()"
                          [class.font-medium]="i === activeIndex()"
                          [attr.aria-current]="i === activeIndex() ? 'true' : null"
                          [attr.aria-keyshortcuts]="canEdit() ? 'Enter' : null"
                          (click)="seek(segment)"
                          (keydown.enter)="startEditFromKey($event, segment)"
                          (dblclick)="startEditFromPointer(segment)"
                        >
                          <time class="font-mono text-xs text-base-content/70 pt-0.5 shrink-0" [attr.datetime]="isoDuration(segment.startMs)">{{ timestamp(segment.startMs) }}</time>&ngsp;@if (segment.speakerName) {<span class="badge badge-sm {{ speakerBadgeClass(segment.speakerIndex!) }} shrink-0">{{ segment.speakerName }}</span>&ngsp;}<span>{{ segment.text }}</span>
                        </button>
                      } @else {
                        <div
                          data-testid="segment-text"
                          class="grow flex gap-3 px-2 py-1 rounded-md focus-visible:outline-2 focus-visible:outline-primary"
                          [attr.tabindex]="canEdit() ? 0 : null"
                          [attr.aria-keyshortcuts]="canEdit() ? 'Enter' : null"
                          (keydown.enter)="startEditFromKey($event, segment)"
                          (dblclick)="startEditFromPointer(segment)"
                        >
                          <time class="font-mono text-xs text-base-content/70 pt-0.5 shrink-0" [attr.datetime]="isoDuration(segment.startMs)">{{ timestamp(segment.startMs) }}</time>&ngsp;@if (segment.speakerName) {<span class="badge badge-sm {{ speakerBadgeClass(segment.speakerIndex!) }} shrink-0">{{ segment.speakerName }}</span>&ngsp;}<span>{{ segment.text }}</span>
                        </div>
                      }
                      @if (canEdit()) {
                        <button
                          type="button"
                          data-testid="segment-edit"
                          class="btn btn-ghost btn-xs px-1"
                          [attr.aria-label]="'editor.edit' | transloco: { time: timestamp(segment.startMs) }"
                          (click)="startSegmentEdit(segment)"
                        >
                          ✎
                        </button>
                      }
                      @if (canEdit() || (segment.version ?? 0) > 0) {
                        <button
                          type="button"
                          data-testid="segment-history-toggle"
                          class="btn btn-ghost btn-xs px-1"
                          [class.text-primary]="(segment.version ?? 0) > 0"
                          [attr.aria-expanded]="historyIndex() === segment.index"
                          [attr.aria-label]="(historyIndex() === segment.index ? 'editor.historyHide' : 'editor.historyToggle') | transloco: { time: timestamp(segment.startMs) }"
                          (click)="toggleHistory(segment)"
                        >
                          ⟲
                        </button>
                      }
                    </div>
                  }
                  @if (historyIndex() === segment.index) {
                    <app-segment-history
                      class="block mt-1"
                      [jobId]="jobId()"
                      [segment]="segment"
                      [canRestore]="canEdit()"
                      (restored)="applyEdited($event)"
                      (conflict)="reloadSegments()"
                    />
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
  /** Jump straight to this time once segments (and the audio element) are ready (S13 search deep-link). */
  readonly initialSeekMs = input<number | undefined>(undefined);
  /** Whether the user may correct segments (S32): owners only, readers of a shared job see the text read only. */
  readonly canEdit = input(false);

  private jobService = inject(AudioJobService);
  private audioSeekApplied = false;
  private audio = viewChild<ElementRef<HTMLAudioElement>>('audio');
  private segmentList = viewChild<ElementRef<HTMLElement>>('segmentList');
  private segmentItems = viewChildren<ElementRef<HTMLElement>>('segmentItem');

  /** null while loading. */
  readonly segments = signal<TranscriptSegment[] | null>(null);
  readonly audioAvailable = signal(true);
  protected readonly currentMs = signal(0);
  readonly activeIndex = computed(() => findActiveSegment(this.segments() ?? [], this.currentMs()));
  readonly audioUrl = computed(() => this.jobService.audioUrl(this.jobId()));
  readonly timestamp = formatTimestamp;
  readonly speakerBadgeClass = speakerBadgeClass;
  /** Machine-readable offset for &lt;time datetime&gt;, e.g. "PT65.5S". */
  isoDuration(ms: number): string {
    return `PT${ms / 1000}S`;
  }

  // --- transcript editor (S32) ----------------------------------------------

  readonly edit = new SegmentEditState();
  /** Index of the segment whose version history is open. */
  readonly historyIndex = signal<number | null>(null);

  startSegmentEdit(segment: TranscriptSegment): void {
    this.edit.start(segment.index, segment.text);
  }

  startEditFromKey(event: Event, segment: TranscriptSegment): void {
    if (!this.canEdit()) {
      return;
    }
    // Enter corrects the segment; Space still plays it
    event.preventDefault();
    this.startSegmentEdit(segment);
  }

  startEditFromPointer(segment: TranscriptSegment): void {
    if (this.canEdit()) {
      this.startSegmentEdit(segment);
    }
  }

  onEditorKeydown(event: KeyboardEvent, segment: TranscriptSegment): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.edit.cancel();
    } else if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      event.preventDefault();
      this.saveSegmentEdit(segment);
    } else if (event.altKey && event.key.toLowerCase() === 'p') {
      event.preventDefault();
      this.play(segment);
    }
  }

  saveSegmentEdit(segment: TranscriptSegment): void {
    if (!this.edit.canSave()) {
      return;
    }
    this.edit.beginSave();
    this.jobService.editSegment(this.jobId(), segment.index, this.edit.draft().trim(), segment.version ?? 0).subscribe({
      next: (saved) => {
        this.applyEdited(saved);
        this.edit.finish();
      },
      error: (error: { status?: number }) => this.edit.fail(error.status === 409 ? 'conflict' : 'failed'),
    });
  }

  /** Takes over a segment as returned by an edit or restore. */
  applyEdited(saved: TranscriptSegment): void {
    this.segments.update((current) =>
      (current ?? []).map((s) => (s.index === saved.index ? { ...s, text: saved.text, version: saved.version } : s)),
    );
  }

  toggleHistory(segment: TranscriptSegment): void {
    this.historyIndex.update((open) => (open === segment.index ? null : segment.index));
  }

  /** After a conflict: fetch the latest versions so the next edit uses the current version number. */
  reloadSegments(): void {
    this.jobService.loadSegments(this.jobId()).subscribe({
      next: (segments) => this.segments.set(segments),
      // Keep showing the versions we have; the next save will report the conflict again
      error: () => undefined,
    });
  }

  /** Plays the segment from its start (Alt+P in the editor). */
  play(segment: TranscriptSegment): void {
    this.seek(segment);
    // The browser may refuse playback without a user gesture or lack of audio; the segment is still selected
    void this.audio()?.nativeElement.play?.()?.catch(() => undefined);
  }

  // --- speaker diarization (S11) ----------------------------------------------

  /** Detected speakers of a diarized job; empty for a non-diarized one. */
  readonly speakers = signal<JobSpeaker[]>([]);
  readonly editingSpeakerIndex = signal<number | null>(null);
  readonly editingName = signal('');

  startEdit(speaker: JobSpeaker): void {
    this.editingSpeakerIndex.set(speaker.index);
    this.editingName.set(speaker.displayName);
  }

  cancelEdit(): void {
    this.editingSpeakerIndex.set(null);
  }

  saveEdit(speaker: JobSpeaker): void {
    const displayName = this.editingName().trim();
    if (!displayName) {
      return;
    }
    this.jobService.renameSpeaker(this.jobId(), speaker.index, displayName).subscribe(() => {
      this.speakers.update((current) =>
        current.map((s) => (s.index === speaker.index ? { ...s, displayName } : s)),
      );
      this.segments.update((current) =>
        (current ?? []).map((s) => (s.speakerIndex === speaker.index ? { ...s, speakerName: displayName } : s)),
      );
      this.editingSpeakerIndex.set(null);
    });
  }

  constructor() {
    effect((onCleanup) => {
      const id = this.jobId();
      untracked(() => {
        this.segments.set(null);
        this.audioAvailable.set(true);
        this.currentMs.set(0);
        this.speakers.set([]);
        this.editingSpeakerIndex.set(null);
        this.edit.cancel();
        this.historyIndex.set(null);
        this.audioSeekApplied = false;
      });
      const subscription = this.jobService.loadSegments(id).subscribe({
        next: (segments) => {
          this.segments.set(segments);
          const seekMs = untracked(this.initialSeekMs);
          if (seekMs != null) {
            this.currentMs.set(seekMs);
          }
        },
        // Segments are optional extras; the transcript above stays usable
        error: () => this.segments.set([]),
      });
      const speakerSubscription = this.jobService.loadSpeakers(id).subscribe({
        next: (speakers) => this.speakers.set(speakers),
        error: () => this.speakers.set([]),
      });
      onCleanup(() => {
        subscription.unsubscribe();
        speakerSubscription.unsubscribe();
      });
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

    // The <audio> element only exists once segments have loaded; apply the deep-linked time once it does
    effect(() => {
      const audio = this.audio()?.nativeElement;
      const seekMs = this.initialSeekMs();
      if (audio && seekMs != null && !this.audioSeekApplied) {
        this.audioSeekApplied = true;
        audio.currentTime = seekMs / 1000;
      }
    });
  }

  onTimeUpdate(): void {
    const audio = this.audio()?.nativeElement;
    if (audio) {
      this.currentMs.set(audio.currentTime * 1000);
    }
  }

  /** Jumps to a time, e.g. the start of a chapter (S25). */
  seekToMs(ms: number): void {
    const audio = this.audio()?.nativeElement;
    if (audio) {
      audio.currentTime = ms / 1000;
    }
    this.currentMs.set(ms);
  }

  seek(segment: TranscriptSegment): void {
    const audio = this.audio()?.nativeElement;
    if (audio) {
      audio.currentTime = segment.startMs / 1000;
    }
    this.currentMs.set(segment.startMs);
  }
}
