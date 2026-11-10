import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { QueueEntry, QueueOverview } from '../models/audio-job.model';

/** The user's jobs in the transcription queue and their priority (S34). */
@Injectable({ providedIn: 'root' })
export class QueueService {
  private http = inject(HttpClient);

  readonly entries = signal<QueueEntry[]>([]);
  readonly totalWaiting = signal(0);

  async load(): Promise<void> {
    const overview = await firstValueFrom(this.http.get<QueueOverview>('/api/queue'));
    this.entries.set(overview.entries);
    this.totalWaiting.set(overview.totalWaiting);
  }

  /** Errors (HttpErrorResponse: 409 when the job already started, 400 out of range) are left to the caller. */
  async setPriority(jobId: string, priority: number): Promise<void> {
    await firstValueFrom(this.http.put(`/api/audio-jobs/${jobId}/priority`, { priority }));
    await this.load();
  }

  /** Cancels a waiting or running job (S09), then refreshes the overview. */
  async cancel(jobId: string): Promise<void> {
    await firstValueFrom(this.http.post(`/api/audio-jobs/${jobId}/cancel`, null));
    await this.load();
  }
}
