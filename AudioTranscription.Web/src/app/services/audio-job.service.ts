import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient, HttpEvent, HttpEventType } from '@angular/common/http';
import {
  AudioJob,
  AudioJobListItem,
  AudioJobStatus,
  CreateAudioJobResponse,
  PaginatedResult,
} from '../models/audio-job.model';
import { Observable, tap, map, filter } from 'rxjs';

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

  uploadFile(file: File): Observable<{ progress: number; jobId?: string }> {
    const formData = new FormData();
    formData.append('file', file, file.name);

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
