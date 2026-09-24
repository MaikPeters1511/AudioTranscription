import { Component, ElementRef, inject, OnInit, signal, computed, effect, linkedSignal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { JobActionsComponent } from '../job-actions/job-actions.component';
import { AudioJobService } from '../../services/audio-job.service';
import { AudioJobStatus } from '../../models/audio-job.model';
import { ToastService } from '../../services/toast.service';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { PageTitleService } from '../../i18n/page-title.service';
import { languageName } from '../../i18n/language-names';

type TranscriptVersion = 'processed' | 'raw';

@Component({
  selector: 'app-job-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, TranslocoPipe, JobActionsComponent],
  template: `
    <div class="max-w-4xl mx-auto">
      <a routerLink="/jobs" class="btn btn-ghost btn-sm mb-4 gap-1">
        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
        </svg>
        {{ 'jobDetail.back' | transloco }}
      </a>

      @if (jobService.selectedJobLoading()) {
        <div class="space-y-4">
          <div class="skeleton h-8 w-64"></div>
          <div class="skeleton h-40 w-full rounded-xl"></div>
          <div class="skeleton h-64 w-full rounded-xl"></div>
        </div>
      } @else if (jobService.selectedJob(); as job) {
        <!-- Header -->
        <div class="flex items-start justify-between mb-6">
          <div>
            <h1 class="text-3xl font-bold">{{ job.fileName }}</h1>
            <p class="text-sm text-base-content/60 mt-1">
              {{ jobService.formatFileSize(job.fileSizeBytes) }} •
              {{ job.contentType }} •
              {{ job.createdAtUtc | date: ('format.dateTimeSeconds' | transloco) }}
            </p>
          </div>
          <div class="flex flex-col items-end gap-2">
            <span class="badge badge-lg" [ngClass]="jobService.getStatusBadgeClass(job.status)">
              @if (job.status === AudioJobStatus.Processing) {
                <span class="loading loading-spinner loading-xs mr-1"></span>
              }
              {{ jobService.getStatusLabelKey(job.status) | transloco }}
            </span>
            <app-job-actions [job]="job" />
          </div>
        </div>

        @if (job.status === AudioJobStatus.Cancelled) {
          <div class="alert alert-info mb-6" role="status">
            <span>{{ 'jobDetail.cancelledInfo' | transloco }}</span>
          </div>
        }

        <!-- Metadata Cards -->
        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
          <div class="card bg-base-200 shadow-sm">
            <div class="card-body p-4">
              <p class="text-xs uppercase tracking-wider text-base-content/50">{{ 'jobDetail.language' | transloco }}</p>
              <p class="text-xl font-semibold">
                {{ job.language ? languageLabel(job.language) : ('jobDetail.languageUnknown' | transloco) }}
              </p>
              <p class="text-xs text-base-content/60">
                {{ (job.requestedLanguage ? 'jobDetail.languageRequested' : 'jobDetail.languageDetected') | transloco }}
              </p>
            </div>
          </div>
          <div class="card bg-base-200 shadow-sm">
            <div class="card-body p-4">
              <p class="text-xs uppercase tracking-wider text-base-content/50">{{ 'jobDetail.model' | transloco }}</p>
              <p class="text-xl font-semibold">{{ job.model || '-' }}</p>
            </div>
          </div>
          <div class="card bg-base-200 shadow-sm">
            <div class="card-body p-4">
              <p class="text-xs uppercase tracking-wider text-base-content/50">{{ 'jobDetail.duration' | transloco }}</p>
              <p class="text-xl font-semibold">{{ jobService.formatDuration(job.durationSeconds) }}</p>
            </div>
          </div>
          <div class="card bg-base-200 shadow-sm">
            <div class="card-body p-4">
              <p class="text-xs uppercase tracking-wider text-base-content/50">{{ 'jobDetail.completedAt' | transloco }}</p>
              <p class="text-xl font-semibold">
                {{ job.completedAtUtc ? (job.completedAtUtc | date: ('format.time' | transloco)) : '-' }}
              </p>
            </div>
          </div>
        </div>

        <!-- Processing State -->
        @if (job.status === AudioJobStatus.Pending || job.status === AudioJobStatus.Processing) {
          <div class="card bg-base-200 shadow-sm">
            <div class="card-body items-center text-center py-12">
              <span class="loading loading-dots loading-lg text-primary"></span>
              <p class="text-lg mt-4">
                {{ (job.status === AudioJobStatus.Pending ? 'jobDetail.waiting' : 'jobDetail.processing') | transloco }}
              </p>
              <p class="text-sm text-base-content/50">
                {{ 'jobDetail.liveHint' | transloco }}
              </p>
            </div>
          </div>
        }

        <!-- Error State -->
        @if (job.status === AudioJobStatus.Failed) {
          <div class="alert alert-error">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4c-.77-.833-1.964-.833-2.732 0L3.268 16.5c-.77.833.192 2.5 1.732 2.5z" />
            </svg>
            <div>
              <p class="font-medium">{{ 'jobDetail.failedTitle' | transloco }}</p>
              <p class="text-sm">{{ job.errorMessage || ('jobDetail.unknownError' | transloco) }}</p>
            </div>
          </div>
        }

        <!-- Transcript -->
        @if (job.status === AudioJobStatus.Completed && (job.rawTranscript || job.processedTranscript)) {
          @let transcriptText = transcript();
          <div class="card bg-base-200 shadow-sm">
            <div class="card-body">
              <div class="flex items-center justify-between mb-3 flex-wrap gap-2">
                <div class="flex items-center gap-3">
                  <h2 class="card-title text-lg">{{ 'jobDetail.transcript' | transloco }}</h2>
                  <span class="text-xs text-base-content/50">
                    {{ 'jobDetail.stats' | transloco: { words: wordCount(), chars: transcriptText.length } }}
                  </span>
                </div>
                <div class="flex gap-2">
                  <button
                    class="btn btn-sm btn-outline gap-1"
                    (click)="downloadTranscript(transcriptText, job.fileName)"
                  >
                    <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                    </svg>
                    {{ 'jobDetail.download' | transloco }}
                  </button>
                  <button
                    class="btn btn-sm gap-1"
                    [class.btn-ghost]="!copied()"
                    [class.btn-success]="copied()"
                    (click)="copyTranscript(transcriptText)"
                  >
                    @if (copied()) {
                      <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" />
                      </svg>
                      {{ 'jobDetail.copied' | transloco }}
                    } @else {
                      <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 16H6a2 2 0 01-2-2V6a2 2 0 012-2h8a2 2 0 012 2v2m-6 12h8a2 2 0 002-2v-8a2 2 0 00-2-2h-8a2 2 0 00-2 2v8a2 2 0 002 2z" />
                      </svg>
                      {{ 'jobDetail.copy' | transloco }}
                    }
                  </button>
                </div>
              </div>
              @if (hasProcessedVersion()) {
                <div
                  role="tablist"
                  class="tabs tabs-box tabs-sm mb-3 w-fit"
                  [attr.aria-label]="'jobDetail.versions.label' | transloco"
                  (keydown)="onVersionKeydown($event)"
                >
                  @for (version of transcriptVersions; track version) {
                    <button
                      type="button"
                      role="tab"
                      class="tab"
                      [id]="'transcript-tab-' + version"
                      [class.tab-active]="transcriptView() === version"
                      [attr.aria-selected]="transcriptView() === version"
                      aria-controls="transcript-panel"
                      [tabIndex]="transcriptView() === version ? 0 : -1"
                      (click)="transcriptView.set(version)"
                    >
                      {{ 'jobDetail.versions.' + version | transloco }}
                    </button>
                  }
                </div>
              }
              <div
                id="transcript-panel"
                class="bg-base-100 rounded-lg p-4 whitespace-pre-wrap leading-relaxed text-sm max-h-[500px] overflow-y-auto"
                [attr.role]="hasProcessedVersion() ? 'tabpanel' : null"
                [attr.aria-labelledby]="hasProcessedVersion() ? 'transcript-tab-' + transcriptView() : null"
                tabindex="0"
              >{{ transcriptText }}</div>
            </div>
          </div>
        }
      } @else {
        <div class="alert alert-warning">
          <span>{{ 'jobDetail.notFound' | transloco }}</span>
        </div>
      }
    </div>
  `,
})
export class JobDetailComponent implements OnInit {
  jobService = inject(AudioJobService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private toastService = inject(ToastService);
  private pageTitle = inject(PageTitleService);
  private transloco = inject(TranslocoService);
  private activeLang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });
  AudioJobStatus = AudioJobStatus;
  copied = signal(false);

  private host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly transcriptVersions = ['processed', 'raw'] as const;

  /** Whether an LLM post-processed version exists next to the raw Whisper output. */
  hasProcessedVersion = computed(() => !!this.jobService.selectedJob()?.processedTranscript);

  /** Selected version; starts on the edited one again whenever another job is opened. */
  transcriptView = linkedSignal<string | undefined, TranscriptVersion>({
    source: () => this.jobService.selectedJob()?.id,
    computation: () => 'processed',
  });

  /** Displayed transcript; copy, download and counts always use this version. */
  transcript = computed(() => {
    const job = this.jobService.selectedJob();
    if (this.hasProcessedVersion() && this.transcriptView() === 'processed') {
      return job?.processedTranscript ?? '';
    }
    return job?.rawTranscript ?? '';
  });

  wordCount = computed(() => {
    const text = this.transcript();
    return text ? text.trim().split(/\s+/).filter(Boolean).length : 0;
  });

  constructor() {
    // Leave the page when the open job is deleted, here or by another client (SignalR)
    this.jobService.jobDeleted$.pipe(takeUntilDestroyed()).subscribe((id) => {
      if (id === this.jobService.selectedJob()?.id) {
        this.router.navigate(['/jobs']);
      }
    });

    // Keep the browser tab title in sync with the currently viewed file.
    effect(() => {
      const job = this.jobService.selectedJob();
      if (job) {
        this.pageTitle.set('jobDetail.pageTitle', { fileName: job.fileName });
      } else {
        this.pageTitle.set('app.name');
      }
    });
  }

  /** Language code named in the UI language, e.g. "de" -> "German". */
  languageLabel(code: string): string {
    return languageName(code, this.activeLang());
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.jobService.loadJob(id);
    }
  }

  /** WAI-ARIA tabs keyboard pattern: arrows (wrapping), Home and End select and focus a tab. */
  onVersionKeydown(event: KeyboardEvent): void {
    const versions = this.transcriptVersions;
    const current = versions.indexOf(this.transcriptView());
    const next = {
      ArrowRight: (current + 1) % versions.length,
      ArrowLeft: (current - 1 + versions.length) % versions.length,
      Home: 0,
      End: versions.length - 1,
    }[event.key];
    if (next === undefined) {
      return;
    }

    event.preventDefault();
    this.transcriptView.set(versions[next]);
    this.host.nativeElement.querySelector<HTMLElement>(`#transcript-tab-${versions[next]}`)?.focus();
  }

  copyTranscript(text: string): void {
    navigator.clipboard.writeText(text).then(() => {
      this.copied.set(true);
      this.toastService.success(this.transloco.translate('jobDetail.copiedToast'));
      setTimeout(() => this.copied.set(false), 2000);
    });
  }

  downloadTranscript(text: string, originalFileName: string): void {
    const blob = new Blob([text], { type: 'text/plain' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${originalFileName}.txt`;
    a.click();
    window.URL.revokeObjectURL(url);
    this.toastService.success(this.transloco.translate('jobDetail.downloadStarted'));
  }
}


