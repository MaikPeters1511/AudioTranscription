import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient, HttpEvent, HttpEventType } from '@angular/common/http';
import {
  AudioJob,
  AudioJobListItem,
  AudioJobStatus,
  CreateAudioJobResponse,
  JobSpeaker,
  PaginatedResult,
  PostProcessingMode,
  SubtitleFormat,
  TranscriptionOptions,
  TranscriptionSettings,
  TranscriptSegment,
  TranscriptVariant,
} from '../models/audio-job.model';
import { Observable, Subject, tap, map, filter, firstValueFrom } from 'rxjs';

function upsertVariant(variants: TranscriptVariant[], variant: TranscriptVariant): TranscriptVariant[] {
  const index = variants.findIndex((v) => v.id === variant.id);
  if (index >= 0) {
    const updated = [...variants];
    updated[index] = variant;
    return updated;
  }
  return [...variants, variant];
}

@Injectable({ providedIn: 'root' })
export class AudioJobService {
  private http = inject(HttpClient);
  private baseUrl = '/api/audio-jobs';

  // Reactive state with signals
  readonly jobs = signal<AudioJobListItem[]>([]);
  readonly totalCount = signal(0);
  readonly currentPage = signal(1);
  readonly pageSize = signal(20);
  readonly loading = signal(false);
  readonly selectedJob = signal<AudioJob | null>(null);
  readonly selectedJobLoading = signal(false);

  /** Progress in percent of running jobs, from "JobProgress" events and loaded jobs. */
  private readonly progress = signal<ReadonlyMap<string, number>>(new Map());

  readonly totalPages = computed(() =>
    Math.ceil(this.totalCount() / this.pageSize())
  );

  loadJobs(page = 1): void {
    this.loading.set(true);
    this.currentPage.set(page);

    this.http
      .get<PaginatedResult<AudioJobListItem>>(this.baseUrl, {
        params: { page: page.toString(), pageSize: this.pageSize().toString() },
      })
      .subscribe({
        next: (result) => {
          this.jobs.set(result.items);
          result.items.forEach((job) => this.takeProgress(job));
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: () => this.loading.set(false),
      });
  }

  loadJob(id: string, silent = false): void {
    if (!silent) {
      this.selectedJobLoading.set(true);
      this.selectedJob.set(null);
    }

    this.http.get<AudioJob>(`${this.baseUrl}/${id}`).subscribe({
      next: (job) => {
        this.selectedJob.set(job);
        this.takeProgress(job);
        this.selectedJobLoading.set(false);
      },
      error: () => this.selectedJobLoading.set(false),
    });
  }

  /** Merges a live status update (e.g. from SignalR) into the currently open job detail, if it matches. */
  updateSelectedJobFromListItem(job: AudioJobListItem): void {
    const current = this.selectedJob();
    if (!current || current.id !== job.id) {
      return;
    }
    this.selectedJob.set({
      ...current,
      status: job.status,
      language: job.language,
      durationSeconds: job.durationSeconds,
      completedAtUtc: job.completedAtUtc,
    });
  }

  loadSegments(id: string): Observable<TranscriptSegment[]> {
    return this.http.get<TranscriptSegment[]>(`${this.baseUrl}/${id}/segments`);
  }

  // --- speaker diarization (S11) ---------------------------------------------

  loadSpeakers(jobId: string): Observable<JobSpeaker[]> {
    return this.http.get<JobSpeaker[]>(`${this.baseUrl}/${jobId}/speakers`);
  }

  renameSpeaker(jobId: string, index: number, displayName: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${jobId}/speakers/${index}`, { displayName });
  }

  // --- transcript variants (S10) --------------------------------------------

  /** Variants of the currently open job, keyed by id; empty until {@link loadVariants} resolves. */
  readonly variants = signal<TranscriptVariant[]>([]);

  loadVariants(jobId: string): void {
    this.http.get<TranscriptVariant[]>(`${this.baseUrl}/${jobId}/variants`).subscribe({
      next: (variants) => this.variants.set(variants),
      error: () => this.variants.set([]),
    });
  }

  /** Generates (or regenerates) a variant; the result arrives via loadVariants once "VariantCompleted" fires. */
  generateVariant(jobId: string, mode: PostProcessingMode, targetLanguage?: string): Observable<TranscriptVariant> {
    return this.http
      .post<TranscriptVariant>(`${this.baseUrl}/${jobId}/variants`, { mode, targetLanguage })
      .pipe(tap((variant) => this.variants.update((current) => upsertVariant(current, variant))));
  }

  /** Called when "VariantCompleted" arrives for the currently open job. */
  refreshVariant(jobId: string): void {
    if (this.selectedJob()?.id === jobId) {
      this.loadVariants(jobId);
    }
  }

  /** Same-origin URL: the browser sends the session cookie for downloads and the audio element. */
  subtitleUrl(id: string, format: SubtitleFormat): string {
    return `${this.baseUrl}/${id}/subtitles?format=${format}`;
  }

  audioUrl(id: string): string {
    return `${this.baseUrl}/${id}/audio`;
  }

  loadTranscriptionOptions(): Observable<TranscriptionOptions> {
    return this.http.get<TranscriptionOptions>('/api/transcription-options');
  }

  /** Unset settings are left to the server (default model, language detection). */
  uploadFile(
    file: File,
    settings: TranscriptionSettings = {},
  ): Observable<{ progress: number; jobId?: string }> {
    const formData = new FormData();
    formData.append('file', file, file.name);
    if (settings.model) {
      formData.append('model', settings.model);
    }
    if (settings.language) {
      formData.append('language', settings.language);
    }
    if (settings.diarize) {
      formData.append('diarize', 'true');
    }

    return this.http
      .post<CreateAudioJobResponse>(this.baseUrl, formData, {
        reportProgress: true,
        observe: 'events',
      })
      .pipe(
        filter(
          (event): event is HttpEvent<CreateAudioJobResponse> => event !== null
        ),
        map((event) => {
          switch (event.type) {
            case HttpEventType.UploadProgress:
              return {
                progress: event.total
                  ? Math.round((100 * event.loaded) / event.total)
                  : 0,
              };
            case HttpEventType.Response:
              return { progress: 100, jobId: event.body?.id };
            default:
              return { progress: 0 };
          }
        })
      );
  }

  /** Deletes job, transcript and upload. Errors (HttpErrorResponse) are left to the caller. */
  async deleteJob(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.baseUrl}/${id}`));
    this.removeJobFromList(id);
  }

  /** Requests cancellation, then refreshes the job (SignalR may be disconnected). */
  async cancelJob(id: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.baseUrl}/${id}/cancel`, null));
    await this.refreshJob(id);
  }

  /** Re-queues a failed or cancelled job (410: its upload is gone), then refreshes it. */
  async retryJob(id: string): Promise<void> {
    await firstValueFrom(this.http.post(`${this.baseUrl}/${id}/retry`, null));
    await this.refreshJob(id);
  }

  /** Reloads one job and merges it into the open detail view and the list. */
  private async refreshJob(id: string): Promise<void> {
    const job = await firstValueFrom(this.http.get<AudioJob>(`${this.baseUrl}/${id}`));
    if (this.selectedJob()?.id === id) {
      this.selectedJob.set(job);
    }
    this.jobs.update((current) =>
      current.map((item) =>
        item.id === id
          ? {
              ...item,
              status: job.status,
              language: job.language,
              durationSeconds: job.durationSeconds,
              completedAtUtc: job.completedAtUtc,
            }
          : item,
      ),
    );
  }

  private readonly deletedJobs = new Subject<string>();
  /**
   * Emits the id of every deleted job, whether deleted here or by another client (SignalR).
   * Emitted before the open job is cleared, so listeners can still compare with selectedJob().
   */
  readonly jobDeleted$ = this.deletedJobs.asObservable();

  /** Removes a deleted job locally (own action or JobDeleted event from another client). */
  removeJobFromList(id: string): void {
    this.deletedJobs.next(id);
    const before = this.jobs().length;
    this.jobs.update((current) => current.filter((j) => j.id !== id));
    if (this.jobs().length < before) {
      this.totalCount.update((count) => Math.max(0, count - 1));
    }
    if (this.selectedJob()?.id === id) {
      this.selectedJob.set(null);
    }
  }

  progressOf(id: string): number | undefined {
    return this.progress().get(id);
  }

  /** Values only rise; a lower one arriving late is ignored. */
  setProgress(id: string, percent: number): void {
    const current = this.progress().get(id);
    if (current !== undefined && percent <= current) {
      return;
    }
    this.progress.update((map) => new Map(map).set(id, percent));
  }

  private clearProgress(id: string): void {
    if (this.progress().has(id)) {
      this.progress.update((map) => {
        const next = new Map(map);
        next.delete(id);
        return next;
      });
    }
  }

  /** Keeps the progress of a (re)loaded or updated job in sync with its status. */
  private takeProgress(job: AudioJobListItem | AudioJob): void {
    if (job.status !== AudioJobStatus.Processing) {
      this.clearProgress(job.id);
    } else if (job.progressPercent != null) {
      this.setProgress(job.id, job.progressPercent);
    }
  }

  updateJobInList(job: AudioJobListItem): void {
    this.takeProgress(job);
    this.jobs.update((current) => {
      const index = current.findIndex((j) => j.id === job.id);
      if (index >= 0) {
        const updated = [...current];
        updated[index] = job;
        return updated;
      }
      return [job, ...current];
    });
  }

  /** Translation key of the status label (see public/i18n). */
  getStatusLabelKey(status: AudioJobStatus): string {
    switch (status) {
      case AudioJobStatus.Pending:
        return 'status.pending';
      case AudioJobStatus.Processing:
        return 'status.processing';
      case AudioJobStatus.Completed:
        return 'status.completed';
      case AudioJobStatus.Failed:
        return 'status.failed';
      case AudioJobStatus.Cancelled:
        return 'status.cancelled';
    }
  }

  getStatusBadgeClass(status: AudioJobStatus): string {
    switch (status) {
      case AudioJobStatus.Pending:
        return 'badge-warning';
      case AudioJobStatus.Processing:
        return 'badge-info';
      case AudioJobStatus.Completed:
        return 'badge-success';
      case AudioJobStatus.Failed:
        return 'badge-error';
      case AudioJobStatus.Cancelled:
        return 'badge-neutral';
    }
  }

  formatFileSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  formatDuration(seconds?: number): string {
    if (!seconds) return '-';
    const mins = Math.floor(seconds / 60);
    const secs = Math.floor(seconds % 60);
    return `${mins}:${secs.toString().padStart(2, '0')}`;
  }
}
