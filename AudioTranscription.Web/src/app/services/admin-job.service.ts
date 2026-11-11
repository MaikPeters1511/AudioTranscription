import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AudioJobListItem } from '../models/audio-job.model';

/** Jobs from before ownership existed (S19): admins list those without an owner and give them to a user. */
@Injectable({ providedIn: 'root' })
export class AdminJobService {
  private http = inject(HttpClient);
  private readonly baseUrl = '/api/admin/jobs';

  listUnassigned(): Promise<AudioJobListItem[]> {
    return firstValueFrom(this.http.get<AudioJobListItem[]>(`${this.baseUrl}/unassigned`));
  }

  assignOwner(jobId: string, userId: string): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.baseUrl}/${encodeURIComponent(jobId)}/owner`, { userId }));
  }
}
