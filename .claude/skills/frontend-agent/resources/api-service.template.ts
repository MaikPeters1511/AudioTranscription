import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { [EntityName] } from '../domain/[entity-name].model';

@Injectable({
  providedIn: 'root'
})
export class [Name]ApiService {
  private http = inject(HttpClient);
  private readonly baseUrl = '/api/[resource]';

  getAll(): Observable<[EntityName][]> {
    return this.http.get<[EntityName][]>(this.baseUrl);
  }

  getById(id: string): Observable<[EntityName]> {
    return this.http.get<[EntityName]>(`${this.baseUrl}/${id}`);
  }
}
