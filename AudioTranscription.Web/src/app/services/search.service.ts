import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PaginatedResult, SearchHit } from '../models/audio-job.model';

@Injectable({ providedIn: 'root' })
export class SearchService {
  private http = inject(HttpClient);

  search(query: string, page = 1, pageSize = 20): Observable<PaginatedResult<SearchHit>> {
    return this.http.get<PaginatedResult<SearchHit>>('/api/search', {
      params: { q: query, page: page.toString(), pageSize: pageSize.toString() },
    });
  }
}
