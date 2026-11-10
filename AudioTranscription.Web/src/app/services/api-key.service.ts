import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ApiKey, ApiKeyScope, CreatedApiKey } from '../models/automation.model';

/** The user's API keys (S28). The key itself is only part of the response of `create`; it is never stored here. */
@Injectable({ providedIn: 'root' })
export class ApiKeyService {
  private http = inject(HttpClient);

  readonly keys = signal<ApiKey[]>([]);

  async load(): Promise<void> {
    this.keys.set(await firstValueFrom(this.http.get<ApiKey[]>('/api/api-keys')));
  }

  /** @returns the new key including its secret; show it once and drop it. */
  async create(name: string, scopes: ApiKeyScope[], expiresAt: string | null): Promise<CreatedApiKey> {
    const created = await firstValueFrom(this.http.post<CreatedApiKey>('/api/api-keys', { name, scopes, expiresAt }));
    const { key: _secret, ...listed } = created;
    this.keys.update((current) => [listed, ...current]);
    return created;
  }

  async delete(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/api-keys/${id}`));
    this.keys.update((current) => current.filter((k) => k.id !== id));
  }
}
