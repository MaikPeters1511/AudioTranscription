import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Glossary } from '../models/audio-job.model';

const CHARACTERS_PER_TOKEN = 3;

/**
 * The user's glossaries (S22). Names and terms the server hands to Whisper as initial prompt; the API only ever
 * returns and accepts the signed-in user's own.
 */
@Injectable({ providedIn: 'root' })
export class GlossaryService {
  private http = inject(HttpClient);

  readonly glossaries = signal<Glossary[]>([]);

  async load(): Promise<void> {
    this.glossaries.set(await firstValueFrom(this.http.get<Glossary[]>('/api/glossaries')));
  }

  async create(name: string, terms: string[]): Promise<void> {
    const created = await firstValueFrom(this.http.post<Glossary>('/api/glossaries', { name, terms }));
    this.glossaries.update((current) => this.sorted([...current, created]));
  }

  async update(id: string, name: string, terms: string[]): Promise<void> {
    const updated = await firstValueFrom(this.http.put<Glossary>(`/api/glossaries/${id}`, { name, terms }));
    this.glossaries.update((current) => this.sorted(current.map((g) => (g.id === id ? updated : g))));
  }

  async delete(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/glossaries/${id}`));
    this.glossaries.update((current) => current.filter((g) => g.id !== id));
  }

  /** Same estimate as the server (three characters per token of the comma-joined terms); the server decides. */
  estimateTokens(terms: string[]): number {
    return Math.ceil(terms.join(', ').length / CHARACTERS_PER_TOKEN);
  }

  private sorted(glossaries: Glossary[]): Glossary[] {
    return [...glossaries].sort((a, b) => a.name.localeCompare(b.name));
  }
}
