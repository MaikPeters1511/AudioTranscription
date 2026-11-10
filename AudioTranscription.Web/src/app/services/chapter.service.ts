import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { Chapter, ChapterList } from '../models/audio-job.model';

/** Automatic chapters of the job that is open in the player (S25). */
@Injectable({ providedIn: 'root' })
export class ChapterService {
  private http = inject(HttpClient);

  /** The state of the loaded job: "None" until chapters were asked for. */
  readonly state = signal<ChapterList>({ status: 'None', chapters: [] });
  /** The job the state belongs to. */
  private loadedJobId: string | null = null;

  load(jobId: string): void {
    this.loadedJobId = jobId;
    this.state.set({ status: 'None', chapters: [] });
    this.http.get<ChapterList>(this.url(jobId)).subscribe({
      next: (list) => this.applyIfCurrent(jobId, list),
      // Chapters are an optional extra; the player stays usable without them
      error: () => this.applyIfCurrent(jobId, { status: 'None', chapters: [] }),
    });
  }

  /** Starts (or restarts, which replaces all chapters) the generation; the result arrives via {@link refresh}. */
  generate(jobId: string): Observable<ChapterList> {
    return this.http.post<ChapterList>(this.url(jobId), null).pipe(tap((list) => this.applyIfCurrent(jobId, list)));
  }

  /** Called when "ChaptersCompleted" arrives for the open job. */
  refresh(jobId: string): void {
    if (this.loadedJobId === jobId) {
      this.http.get<ChapterList>(this.url(jobId)).subscribe((list) => this.applyIfCurrent(jobId, list));
    }
  }

  update(jobId: string, chapter: Chapter, title: string, summary: string): Observable<Chapter> {
    return this.http.put<Chapter>(`${this.url(jobId)}/${chapter.id}`, { title, summary }).pipe(
      tap((saved) =>
        this.state.update((s) => ({ ...s, chapters: s.chapters.map((c) => (c.id === saved.id ? saved : c)) })),
      ),
    );
  }

  /** The neighbour takes over the range, so the list is reloaded after the delete. */
  delete(jobId: string, chapter: Chapter): Observable<void> {
    return this.http.delete<void>(`${this.url(jobId)}/${chapter.id}`).pipe(tap(() => this.refresh(jobId)));
  }

  private applyIfCurrent(jobId: string, list: ChapterList): void {
    if (this.loadedJobId === jobId) {
      this.state.set(list);
    }
  }

  private url(jobId: string): string {
    return `/api/audio-jobs/${jobId}/chapters`;
  }
}
