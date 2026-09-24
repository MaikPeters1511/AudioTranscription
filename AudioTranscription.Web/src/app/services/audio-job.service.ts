import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient, HttpEvent, HttpEventType } from '@angular/common/http';
import {
  AudioJob,
  AudioJobListItem,
  AudioJobStatus,
  CreateAudioJobResponse,
  PaginatedResult,
  TranscriptionOptions,
  TranscriptionSettings,
} from '../models/audio-job.model';
import { Observable, Subject, tap, map, filter, firstValueFrom } from 'rxjs';

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

  updateJobInList(job: AudioJobListItem): void {
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
