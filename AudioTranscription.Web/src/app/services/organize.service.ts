import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Folder, Tag } from '../models/audio-job.model';
import { AudioJobService } from './audio-job.service';

/** A folder as an option in a list: indented by its depth in the tree. */
export interface FolderOption {
  id: string;
  name: string;
  depth: number;
}

/**
 * The user's tags, folders and favorites (S20). Everything is personal; the API only ever returns and accepts
 * the signed-in user's own. Changes made here are applied to the job list and the open job at once.
 */
@Injectable({ providedIn: 'root' })
export class OrganizeService {
  private http = inject(HttpClient);
  private jobService = inject(AudioJobService);

  readonly tags = signal<Tag[]>([]);
  readonly folders = signal<Folder[]>([]);

  /** The folder tree flattened depth-first (siblings by name), for selects and the management page. */
  readonly folderOptions = computed<FolderOption[]>(() => {
    const all = this.folders();
    const known = new Set(all.map((f) => f.id));
    const byParent = new Map<string | null, Folder[]>();
    for (const folder of all) {
      // A parent we do not know (should not happen) is treated like the top level
      const key = folder.parentId && known.has(folder.parentId) ? folder.parentId : null;
      byParent.set(key, [...(byParent.get(key) ?? []), folder]);
    }
    const result: FolderOption[] = [];
    const walk = (parent: string | null, depth: number) => {
      const children = (byParent.get(parent) ?? []).sort((a, b) => a.name.localeCompare(b.name));
      for (const child of children) {
        result.push({ id: child.id, name: child.name, depth });
        walk(child.id, depth + 1);
      }
    };
    walk(null, 0);
    return result;
  });

  async load(): Promise<void> {
    const [tags, folders] = await Promise.all([
      firstValueFrom(this.http.get<Tag[]>('/api/tags')),
      firstValueFrom(this.http.get<Folder[]>('/api/folders')),
    ]);
    this.tags.set(tags);
    this.folders.set(folders);
  }

  // --- tags ------------------------------------------------------------------------

  async createTag(name: string, color?: string | null): Promise<void> {
    const tag = await firstValueFrom(this.http.post<Tag>('/api/tags', { name, color: color || null }));
    this.tags.update((current) => [...current, tag].sort((a, b) => a.name.localeCompare(b.name)));
  }

  async deleteTag(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/tags/${id}`));
    this.tags.update((current) => current.filter((t) => t.id !== id));
    // The tag is gone from every job
    this.jobService.jobs.update((jobs) => jobs.map((j) => ({ ...j, tags: (j.tags ?? []).filter((t) => t.id !== id) })));
    if (this.jobService.filters().tag === id) {
      this.jobService.setFilters({ ...this.jobService.filters(), tag: undefined });
    }
  }

  /** Replaces the user's own tags on a job. */
  async setJobTags(jobId: string, tagIds: string[]): Promise<void> {
    await firstValueFrom(this.http.put(`/api/audio-jobs/${jobId}/tags`, { tagIds }));
    const known = new Map(this.tags().map((t) => [t.id, t]));
    this.jobService.patchJob(jobId, { tags: tagIds.map((id) => known.get(id)).filter((t): t is Tag => !!t) });
  }

  // --- favorites -------------------------------------------------------------------

  async setFavorite(jobId: string, favorite: boolean): Promise<void> {
    const url = `/api/audio-jobs/${jobId}/favorite`;
    await firstValueFrom(favorite ? this.http.put(url, null) : this.http.delete(url));
    this.jobService.patchJob(jobId, { isFavorite: favorite });
    // On a favorites-only list an un-favorited job has to go
    if (!favorite && this.jobService.filters().favorite) {
      this.jobService.loadJobs(this.jobService.currentPage());
    }
  }

  // --- folders ---------------------------------------------------------------------

  async createFolder(name: string, parentId?: string | null): Promise<void> {
    const folder = await firstValueFrom(this.http.post<Folder>('/api/folders', { name, parentId: parentId || null }));
    this.folders.update((current) => [...current, folder]);
  }

  async updateFolder(id: string, name: string, parentId?: string | null): Promise<void> {
    await firstValueFrom(this.http.put(`/api/folders/${id}`, { name, parentId: parentId || null }));
    this.folders.update((current) => current.map((f) => (f.id === id ? { ...f, name, parentId: parentId || null } : f)));
  }

  /** Deleting reshapes the tree (children move up, a name may get a suffix), so it is reloaded. */
  async deleteFolder(id: string): Promise<void> {
    await firstValueFrom(this.http.delete(`/api/folders/${id}`));
    this.folders.set(await firstValueFrom(this.http.get<Folder[]>('/api/folders')));
    if (this.jobService.filters().folder === id) {
      this.jobService.setFilters({ ...this.jobService.filters(), folder: undefined });
    }
  }

  /** Files a job (owner only) in a folder, or at the top level with null. */
  async setJobFolder(jobId: string, folderId: string | null): Promise<void> {
    await firstValueFrom(this.http.put(`/api/audio-jobs/${jobId}/folder`, { folderId }));
    this.jobService.patchJob(jobId, { folderId });
  }
}
