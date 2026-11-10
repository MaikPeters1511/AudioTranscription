import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PaginatedResult, SearchHit, SearchMode, SearchModes } from '../models/audio-job.model';

@Injectable({ providedIn: 'root' })
export class SearchService {
  private http = inject(HttpClient);

  /** Optionally narrowed to one of the user's tags and/or folders (S20). */
  search(
    query: string,
    page = 1,
    pageSize = 20,
    filters: { tag?: string; folder?: string } = {},
    mode: SearchMode = 'literal',
  ): Observable<PaginatedResult<SearchHit>> {
    const params: Record<string, string> = { q: query, page: page.toString(), pageSize: pageSize.toString() };
    // Semantic and hybrid search (S23) have no tag/folder filter; the literal search keeps its filters
    if (mode === 'literal') {
      if (filters.tag) params['tag'] = filters.tag;
      if (filters.folder) params['folder'] = filters.folder;
      return this.http.get<PaginatedResult<SearchHit>>('/api/search', { params });
    }
    params['mode'] = mode;
    return this.http.get<PaginatedResult<SearchHit>>('/api/search/semantic', { params });
  }

  /** Which modes the server can serve; semantic search is off without an embedding model and a vector database. */
  loadModes(): Observable<SearchModes> {
    return this.http.get<SearchModes>('/api/search/modes');
  }
}
