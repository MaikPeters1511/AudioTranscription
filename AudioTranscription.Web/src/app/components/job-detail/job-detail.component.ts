import { Component, ElementRef, inject, OnInit, signal, computed, effect, linkedSignal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { JobActionsComponent } from '../job-actions/job-actions.component';
import { JobShareComponent } from '../job-share/job-share.component';
import { JobOrganizeComponent } from '../job-organize/job-organize.component';
import { JobExportComponent } from '../job-export/job-export.component';
import { OrganizeService } from '../../services/organize.service';
import { TranscriptPlayerComponent } from '../transcript-player/transcript-player.component';
import { JobProgressComponent } from '../job-progress/job-progress.component';
import { TemplateService } from '../../services/template.service';
import { AudioJobService } from '../../services/audio-job.service';
import { AudioJob, AudioJobStatus, PostProcessingMode, SubtitleFormat, TranscriptionOptions, TranscriptVariant, VariantStatus } from '../../models/audio-job.model';
import { ToastService } from '../../services/toast.service';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { PageTitleService } from '../../i18n/page-title.service';
import { languageName } from '../../i18n/language-names';

@Component({
  selector: 'app-job-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, TranslocoPipe, JobActionsComponent, JobShareComponent, JobOrganizeComponent, JobExportComponent, TranscriptPlayerComponent, JobProgressComponent],
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
            @if (job.isOwner === false && job.sharedByEmail) {
              <p class="text-sm text-base-content/70 mt-1" data-testid="shared-by">{{ 'jobDetail.sharedBy' | transloco: { email: job.sharedByEmail } }}</p>
            }
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
            <div class="flex items-center gap-2 flex-wrap justify-end">
              @if (job.isOwner !== false) {
                <app-job-share [jobId]="job.id" />
              }
              @if (job.status === AudioJobStatus.Completed) {
                <a class="btn btn-sm btn-outline" data-testid="job-ask" [routerLink]="['/chat']" [queryParams]="{ job: job.id }">
                  {{ 'jobDetail.askAbout' | transloco }}
                </a>
              }
              <app-job-actions [job]="job" />
            </div>
          </div>
        </div>

        <app-job-organize [job]="job" />

        <!-- Automatic deletion (S30): shown only when it applies or the job was locked against it -->
        @if (job.deleteAtUtc || job.retentionLocked) {
          <div class="flex flex-wrap items-center gap-x-4 gap-y-2 text-sm mb-6" data-testid="retention">
            @if (job.deleteAtUtc) {
              <span>{{ 'jobDetail.retention.deleteAt' | transloco: { date: (job.deleteAtUtc | date: ('format.dateTime' | transloco)) } }}</span>
            }
            @if (job.isOwner !== false) {
              <label class="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  class="toggle toggle-sm"
                  data-testid="retention-lock"
                  [checked]="job.retentionLocked"
                  (change)="setRetentionLock(job, $any($event.target).checked)"
                />
                <span>{{ 'jobDetail.retention.lock' | transloco }}</span>
              </label>
            } @else if (job.retentionLocked) {
              <span>{{ 'jobDetail.retention.lockedByOwner' | transloco }}</span>
            }
          </div>
        }

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
              @if (job.language || job.requestedLanguage) {
                <p class="text-xs text-base-content/70">
                  {{ (job.requestedLanguage ? 'jobDetail.languageRequested' : 'jobDetail.languageDetected') | transloco }}
                </p>
              }
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
              @if (job.status === AudioJobStatus.Processing) {
                <app-job-progress class="block w-full max-w-md" [percent]="jobService.progressOf(job.id)" [announce]="true" />
              } @else {
                <span class="loading loading-dots loading-lg text-primary"></span>
              }
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

        <!-- Transcript variants (S10) -->
        @if (postProcessingEnabled() && job.status === AudioJobStatus.Completed) {
          <div class="card bg-base-200 shadow-sm mb-6">
            <div class="card-body gap-3">
              <h2 class="card-title text-lg">{{ 'jobDetail.variants.title' | transloco }}</h2>
              <div class="flex flex-wrap items-center gap-2">
                @for (mode of generateModes; track mode) {
                  <button
                    type="button"
                    class="btn btn-sm btn-outline gap-1"
                    [attr.aria-busy]="isPending(mode) ? 'true' : null"
                    [disabled]="isPending(mode)"
                    (click)="requestVariant(mode)"
                  >
                    @if (isPending(mode)) {
                      <span class="loading loading-spinner loading-xs"></span>
                    }
                    {{ 'jobDetail.versions.' + modeKey(mode) | transloco }}
                  </button>
                }
                <div class="flex items-center gap-1">
                  <label for="translate-language" class="sr-only">{{ 'jobDetail.variants.translateLanguage' | transloco }}</label>
                  <select
                    id="translate-language"
                    class="select select-sm"
                    [value]="selectedTranslateLanguage()"
                    (change)="selectedTranslateLanguage.set($any($event.target).value)"
                  >
                    @for (lang of translateLanguages(); track lang.code) {
                      <option [value]="lang.code">{{ lang.name }}</option>
                    }
                  </select>
                  <button
                    type="button"
                    class="btn btn-sm btn-outline gap-1"
                    [attr.aria-busy]="isPending(PostProcessingMode.Translate, selectedTranslateLanguage()) ? 'true' : null"
                    [disabled]="!selectedTranslateLanguage() || isPending(PostProcessingMode.Translate, selectedTranslateLanguage())"
                    (click)="requestVariant(PostProcessingMode.Translate, selectedTranslateLanguage())"
                  >
                    @if (isPending(PostProcessingMode.Translate, selectedTranslateLanguage())) {
                      <span class="loading loading-spinner loading-xs"></span>
                    }
                    {{ 'jobDetail.variants.translate' | transloco }}
                  </button>
                </div>
              </div>
              @if (templates().length > 0) {
                <div class="flex items-center gap-1" data-testid="template-picker">
                  <label for="template-select" class="sr-only">{{ 'jobDetail.variants.templateLabel' | transloco }}</label>
                  <select
                    id="template-select"
                    class="select select-sm"
                    [value]="selectedTemplateId()"
                    (change)="selectedTemplateId.set($any($event.target).value)"
                  >
                    <option value="">{{ 'jobDetail.variants.templatePlaceholder' | transloco }}</option>
                    @for (template of templates(); track template.id) {
                      <option [value]="template.id">{{ template.name }}</option>
                    }
                  </select>
                  <button
                    type="button"
                    class="btn btn-sm btn-outline gap-1"
                    data-testid="template-apply"
                    [attr.aria-busy]="isPending(PostProcessingMode.Template, undefined, selectedTemplateId()) ? 'true' : null"
                    [disabled]="!selectedTemplateId() || isPending(PostProcessingMode.Template, undefined, selectedTemplateId())"
                    (click)="requestVariant(PostProcessingMode.Template, undefined, selectedTemplateId())"
                  >
                    @if (isPending(PostProcessingMode.Template, undefined, selectedTemplateId())) {
                      <span class="loading loading-spinner loading-xs"></span>
                    }
                    {{ 'jobDetail.variants.applyTemplate' | transloco }}
                  </button>
                </div>
              }
              @if (failedVariants().length) {
                <ul class="text-sm text-error">
                  @for (v of failedVariants(); track v.id) {
                    <li>{{ variantTabLabel(v) }}: {{ v.errorMessage || ('jobDetail.variants.unknownError' | transloco) }}</li>
                  }
                </ul>
              }
            </div>
          </div>
        }

        <!-- Transcript -->
        @if (job.status === AudioJobStatus.Completed && (job.rawTranscript || completedVariants().length > 0)) {
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
                <div class="flex gap-2 flex-wrap">
                  @for (format of subtitleFormats; track format) {
                    <a
                      class="btn btn-sm btn-outline"
                      [attr.data-subtitles]="format"
                      [href]="jobService.subtitleUrl(job.id, format)"
                      download
                      [attr.aria-label]="'jobDetail.subtitles.' + format | transloco"
                    >{{ format.toUpperCase() }}</a>
                  }
                  <app-job-export [jobId]="job.id" [variantId]="transcriptView() === 'raw' ? undefined : transcriptView()" />
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
              @if (hasVariantTabs()) {
                <div
                  role="tablist"
                  class="tabs tabs-box tabs-sm mb-3 w-fit"
                  [attr.aria-label]="'jobDetail.versions.label' | transloco"
                  (keydown)="onVersionKeydown($event)"
                >
                  @for (tab of variantTabs(); track tab.key) {
                    <button
                      type="button"
                      role="tab"
                      class="tab"
                      [id]="'transcript-tab-' + tab.key"
                      [class.tab-active]="transcriptView() === tab.key"
                      [attr.aria-selected]="transcriptView() === tab.key"
                      aria-controls="transcript-panel"
                      [tabIndex]="transcriptView() === tab.key ? 0 : -1"
                      (click)="transcriptView.set(tab.key)"
                    >
                      {{ tab.label }}
                    </button>
                  }
                </div>
              }
              <div
                id="transcript-panel"
                class="bg-base-100 rounded-lg p-4 whitespace-pre-wrap leading-relaxed text-sm max-h-[500px] overflow-y-auto"
                [attr.role]="hasVariantTabs() ? 'tabpanel' : null"
                [attr.aria-labelledby]="hasVariantTabs() ? 'transcript-tab-' + transcriptView() : null"
                tabindex="0"
              >{{ transcriptText }}</div>
            </div>
          </div>
        }

        @if (job.status === AudioJobStatus.Completed) {
          <app-transcript-player [jobId]="job.id" [initialSeekMs]="initialSeekMs" [canEdit]="job.isOwner !== false" />
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
  private templateService = inject(TemplateService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private toastService = inject(ToastService);
  private organize = inject(OrganizeService);
  private pageTitle = inject(PageTitleService);
  private transloco = inject(TranslocoService);
  private activeLang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });
  AudioJobStatus = AudioJobStatus;
  readonly subtitleFormats: readonly SubtitleFormat[] = ['srt', 'vtt'];
  copied = signal(false);
  /** From a search result deep-link (S13): the segment time (ms) to jump to in the player. */
  readonly initialSeekMs = (() => {
    const raw = inject(ActivatedRoute).snapshot.queryParamMap.get('t');
    const parsed = raw !== null ? Number(raw) : NaN;
    return Number.isFinite(parsed) ? parsed : undefined;
  })();

  private host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly PostProcessingMode = PostProcessingMode;
  readonly generateModes: readonly Exclude<PostProcessingMode, PostProcessingMode.Translate | PostProcessingMode.Template>[] = [
    PostProcessingMode.Cleanup,
    PostProcessingMode.Summary,
    PostProcessingMode.BulletPoints,
    PostProcessingMode.ActionItems,
  ];

  private transcriptionOptions = signal<TranscriptionOptions | null>(null);
  postProcessingEnabled = computed(() => this.transcriptionOptions()?.postProcessingEnabled ?? false);
  translateLanguages = computed(() => {
    const uiLanguage = this.activeLang();
    return (this.transcriptionOptions()?.languages ?? []).map((code) => ({ code, name: languageName(code, uiLanguage) }));
  });
  selectedTranslateLanguage = signal('');
  /** Post-processing templates (S26) to choose from. */
  readonly templates = this.templateService.templates;
  selectedTemplateId = signal('');

  completedVariants = computed(() => this.jobService.variants().filter((v) => v.status === VariantStatus.Completed));
  failedVariants = computed(() => this.jobService.variants().filter((v) => v.status === VariantStatus.Failed));

  /** "Original" plus one tab per generated variant. */
  variantTabs = computed(() => {
    void this.activeLang(); // recompute labels on language change
    return [
      { key: 'raw', label: this.transloco.translate('jobDetail.versions.raw') },
      ...this.completedVariants().map((v) => ({ key: v.id, label: this.variantTabLabel(v) })),
    ];
  });
  hasVariantTabs = computed(() => this.completedVariants().length > 0);

  /** Selected version; resets to "Original" whenever another job is opened. */
  transcriptView = linkedSignal<string | undefined, string>({
    source: () => this.jobService.selectedJob()?.id,
    computation: () => 'raw',
  });

  /** Displayed transcript; copy, download and counts always use this version. */
  transcript = computed(() => {
    const job = this.jobService.selectedJob();
    const view = this.transcriptView();
    const variant = view !== 'raw' ? this.completedVariants().find((v) => v.id === view) : undefined;
    return variant?.text ?? job?.rawTranscript ?? '';
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

  /** The toggle always shows what the server has: it is reloaded after the call, and on failure too. */
  async setRetentionLock(job: AudioJob, locked: boolean): Promise<void> {
    try {
      await this.jobService.setRetentionLock(job.id, locked);
    } catch {
      this.toastService.error(this.transloco.translate('jobDetail.retention.failed'));
      this.jobService.loadJob(job.id, true);
    }
  }

  /** Language code named in the UI language, e.g. "de" -> "German". */
  languageLabel(code: string): string {
    return languageName(code, this.activeLang());
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.jobService.loadJob(id);
      this.jobService.loadVariants(id);
    }
    // Tags and folders for the organize controls; the page works without them if loading fails
    this.organize.load().catch(() => undefined);
    this.jobService.loadTranscriptionOptions().subscribe({
      next: (options) => {
        this.transcriptionOptions.set(options);
        if (options.postProcessingEnabled) {
          this.templateService.load();
        }
        if (options.languages.length) {
          this.selectedTranslateLanguage.set(options.languages[0]);
        }
      },
      // Variant buttons stay hidden without options; the rest of the page still works
      error: () => this.transcriptionOptions.set(null),
    });
  }

  /**
   * Translation-key suffix for a mode, shared by the generate buttons and the variant tabs.
   * Translate is never passed in here (its tab and button use their own dedicated text), but the
   * switch still needs to be exhaustive for TypeScript.
   */
  modeKey(mode: Exclude<PostProcessingMode, PostProcessingMode.Translate | PostProcessingMode.Template>): string {
    switch (mode) {
      case PostProcessingMode.Cleanup:
        return 'processed';
      case PostProcessingMode.Summary:
        return 'summary';
      case PostProcessingMode.BulletPoints:
        return 'bulletPoints';
      case PostProcessingMode.ActionItems:
        return 'actionItems';
    }
  }

  variantTabLabel(v: TranscriptVariant): string {
    if (v.mode === PostProcessingMode.Translate) {
      return this.transloco.translate('jobDetail.versions.translate', {
        language: languageName(v.targetLanguage!, this.activeLang()),
      });
    }
    if (v.mode === PostProcessingMode.Template) {
      return v.templateName ?? this.transloco.translate('jobDetail.versions.template');
    }
    return this.transloco.translate('jobDetail.versions.' + this.modeKey(v.mode as Exclude<PostProcessingMode, PostProcessingMode.Translate | PostProcessingMode.Template>));
  }

  isPending(mode: PostProcessingMode, targetLanguage?: string, templateId?: string): boolean {
    return this.jobService
      .variants()
      .some(
        (v) =>
          v.mode === mode &&
          (v.targetLanguage ?? '') === (targetLanguage ?? '') &&
          (v.templateId ?? '') === (templateId ?? '') &&
          v.status === VariantStatus.Pending,
      );
  }

  requestVariant(mode: PostProcessingMode, targetLanguage?: string, templateId?: string): void {
    const job = this.jobService.selectedJob();
    if (!job) {
      return;
    }
    this.jobService.generateVariant(job.id, mode, targetLanguage, templateId).subscribe({
      error: (err) => {
        const detail = err.error?.errors?.mode?.[0] ?? err.error?.errors?.targetLanguage?.[0] ?? err.error?.errors?.templateId?.[0] ?? err.error?.detail;
        this.toastService.error(detail || this.transloco.translate('jobDetail.variants.failed'));
      },
    });
  }

  /** WAI-ARIA tabs keyboard pattern: arrows (wrapping), Home and End select and focus a tab. */
  onVersionKeydown(event: KeyboardEvent): void {
    const keys = this.variantTabs().map((tab) => tab.key);
    const current = keys.indexOf(this.transcriptView());
    const next = {
      ArrowRight: (current + 1) % keys.length,
      ArrowLeft: (current - 1 + keys.length) % keys.length,
      Home: 0,
      End: keys.length - 1,
    }[event.key];
    if (next === undefined) {
      return;
    }

    event.preventDefault();
    this.transcriptView.set(keys[next]);
    this.host.nativeElement.querySelector<HTMLElement>(`#transcript-tab-${keys[next]}`)?.focus();
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


