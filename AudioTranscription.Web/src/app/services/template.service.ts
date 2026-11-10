import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { PostprocessingTemplate, SaveTemplateRequest } from '../models/audio-job.model';

/** Post-processing templates (S26): everyone lists them, admins manage the custom ones. */
@Injectable({ providedIn: 'root' })
export class TemplateService {
  private http = inject(HttpClient);

  /** Built-in templates first. */
  readonly templates = signal<PostprocessingTemplate[]>([]);
  readonly loadFailed = signal(false);

  load(): void {
    this.http.get<PostprocessingTemplate[]>('/api/templates').subscribe({
      next: (templates) => {
        this.templates.set(templates);
        this.loadFailed.set(false);
      },
      error: () => {
        this.templates.set([]);
        this.loadFailed.set(true);
      },
    });
  }

  create(request: SaveTemplateRequest): Observable<PostprocessingTemplate> {
    return this.http.post<PostprocessingTemplate>('/api/templates', request).pipe(tap(() => this.load()));
  }

  update(id: string, request: SaveTemplateRequest): Observable<PostprocessingTemplate> {
    return this.http.put<PostprocessingTemplate>(`/api/templates/${id}`, request).pipe(tap(() => this.load()));
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/templates/${id}`).pipe(tap(() => this.load()));
  }
}
