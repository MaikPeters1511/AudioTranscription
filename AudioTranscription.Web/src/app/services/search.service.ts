import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PaginatedResult, SearchHit } from '../models/audio-job.model';

@Injectable({ providedIn: 'root' })
export class SearchService {
  private http = inject(HttpClient);

  /** Optionally narrowed to one of the user's tags and/or folders (S20). */
  search(
    query: string,
    page = 1,
    pageSize = 20,
    filters: { tag?: string; folder?: string } = {},
  ): Observable<PaginatedResult<SearchHit>> {
    const params: Record<string, string> = { q: query, page: page.toString(), pageSize: pageSize.toString() };
    if (filters.tag) params['tag'] = filters.tag;
    if (filters.folder) params['folder'] = filters.folder;
    return this.http.get<PaginatedResult<SearchHit>>('/api/search', { params });
  }
}
