import { Component, computed, inject, input } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AudioJob } from '../../models/audio-job.model';
import { OrganizeService } from '../../services/organize.service';
import { ToastService } from '../../services/toast.service';

let nextId = 0;

/**
 * Favorite mark, tags and (for the owner) folder of one job (S20). Favorite and tags are personal and work
 * on shared jobs too; filing in a folder is the owner's.
 */
@Component({
  selector: 'app-job-organize',
  standalone: true,
  imports: [TranslocoPipe],
  template: `
    <div class="flex flex-wrap items-center gap-3 mb-6" role="group" [attr.aria-label]="'organize.job.label' | transloco">
      <button
        type="button"
        class="btn btn-sm"
        data-testid="favorite"
        [class.btn-warning]="job().isFavorite"
        [attr.aria-pressed]="!!job().isFavorite"
        (click)="toggleFavorite()"
      >
        <span aria-hidden="true">{{ job().isFavorite ? '★' : '☆' }}</span>
        {{ 'organize.job.favorite' | transloco }}
      </button>

      <details class="dropdown">
        <summary class="btn btn-sm" data-testid="tags-summary">
          {{ 'organize.job.tags' | transloco: { count: (job().tags ?? []).length } }}
        </summary>
        <div class="dropdown-content z-10 bg-base-200 rounded-box shadow p-3 w-64" data-testid="tags-menu">
          @if (organize.tags().length === 0) {
            <p class="text-sm text-base-content/60">{{ 'organize.job.noTags' | transloco }}</p>
          } @else {
            <ul class="space-y-1">
              @for (tag of organize.tags(); track tag.id) {
                <li>
                  <label class="flex items-center gap-2 cursor-pointer">
                    <input
                      type="checkbox"
                      class="checkbox checkbox-sm"
                      [checked]="isTagged(tag.id)"
                      (change)="toggleTag(tag.id, $any($event.target).checked)"
                    />
                    <span class="truncate">{{ tag.name }}</span>
                  </label>
                </li>
              }
            </ul>
          }
        </div>
      </details>

      @if (job().isOwner !== false) {
        <div class="flex items-center gap-2">
          <label class="text-sm" [for]="selectId">{{ 'organize.job.folder' | transloco }}</label>
          <select
            class="select select-sm"
            data-testid="folder-select"
            [id]="selectId"
            (change)="setFolder($any($event.target).value)"
          >
            <option value="" [selected]="!job().folderId">{{ 'organize.job.noFolder' | transloco }}</option>
            @for (folder of organize.folderOptions(); track folder.id) {
              <option [value]="folder.id" [selected]="folder.id === job().folderId">{{ indent(folder.depth) }}{{ folder.name }}</option>
            }
          </select>
        </div>
      }
    </div>
  `,
})
export class JobOrganizeComponent {
  readonly organize = inject(OrganizeService);
  private toast = inject(ToastService);
  private transloco = inject(TranslocoService);

  job = input.required<AudioJob>();
  readonly selectId = `job-organize-folder-${nextId++}`;

  private readonly tagIds = computed(() => (this.job().tags ?? []).map((t) => t.id));

  isTagged(tagId: string): boolean {
    return this.tagIds().includes(tagId);
  }

  indent(depth: number): string {
    return '\u00a0\u00a0'.repeat(depth);
  }

  async toggleFavorite(): Promise<void> {
    await this.run(() => this.organize.setFavorite(this.job().id, !this.job().isFavorite));
  }

  async toggleTag(tagId: string, tagged: boolean): Promise<void> {
    const current = this.tagIds();
    const next = tagged ? [...current, tagId] : current.filter((id) => id !== tagId);
    await this.run(() => this.organize.setJobTags(this.job().id, next));
  }

  async setFolder(folderId: string): Promise<void> {
    await this.run(() => this.organize.setJobFolder(this.job().id, folderId || null));
  }

  /** Clean message instead of the server's, and the controls resync because the input signal did not change. */
  private async run(action: () => Promise<void>): Promise<void> {
    try {
      await action();
    } catch {
      this.toast.error(this.transloco.translate('organize.failed'));
    }
  }
}
