import { Component, OnInit, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { Folder, Tag } from '../../models/audio-job.model';
import { OrganizeService } from '../../services/organize.service';
import { ToastService } from '../../services/toast.service';
import { PageTitleService } from '../../i18n/page-title.service';

/** Manage the user's tags and folders (S20): create, rename or move, delete. */
@Component({
  selector: 'app-organize',
  standalone: true,
  imports: [FormsModule, TranslocoPipe],
  template: `
    <div class="max-w-3xl mx-auto space-y-8">
      <h1 class="text-3xl font-bold">{{ 'organize.title' | transloco }}</h1>

      @if (loadFailed()) {
        <div class="alert alert-error" role="alert">{{ 'organize.loadFailed' | transloco }}</div>
      }

      <!-- Tags -->
      <section [attr.aria-labelledby]="'organize-tags-title'">
        <h2 id="organize-tags-title" class="text-xl font-semibold mb-3">{{ 'organize.tags.title' | transloco }}</h2>

        <form class="flex flex-wrap items-end gap-3 mb-4" (ngSubmit)="addTag()" novalidate>
          <div class="flex flex-col gap-1">
            <label for="new-tag-name" class="text-sm font-medium">{{ 'organize.tags.name' | transloco }}</label>
            <input id="new-tag-name" name="tagName" class="input input-sm" maxlength="50" autocomplete="off" [(ngModel)]="newTagName" />
          </div>
          <div class="flex flex-col gap-1">
            <label for="new-tag-color" class="text-sm font-medium">{{ 'organize.tags.color' | transloco }}</label>
            <input id="new-tag-color" name="tagColor" type="color" class="input input-sm w-16 p-1" [(ngModel)]="newTagColor" />
          </div>
          <button type="submit" class="btn btn-primary btn-sm" [disabled]="busy()">{{ 'organize.tags.add' | transloco }}</button>
        </form>
        @if (tagError(); as key) {
          <p class="text-error text-sm mb-3" role="alert">{{ key | transloco }}</p>
        }

        @if (organize.tags().length === 0) {
          <p class="text-sm text-base-content/60">{{ 'organize.tags.empty' | transloco }}</p>
        } @else {
          <ul class="divide-y divide-base-300 rounded-box bg-base-200">
            @for (tag of organize.tags(); track tag.id) {
              <li class="flex items-center justify-between gap-3 p-3">
                <span class="badge badge-outline" [style.border-color]="tag.color">{{ tag.name }}</span>
                @if (confirmTag() === tag.id) {
                  <span class="flex items-center gap-2">
                    <span class="text-sm">{{ 'organize.confirmTag' | transloco }}</span>
                    <button type="button" class="btn btn-error btn-xs" data-action="confirm-delete-tag" [disabled]="busy()" (click)="deleteTag(tag)">{{ 'organize.delete' | transloco }}</button>
                    <button type="button" class="btn btn-xs" (click)="confirmTag.set(null)">{{ 'organize.cancel' | transloco }}</button>
                  </span>
                } @else {
                  <button
                    type="button"
                    class="btn btn-ghost btn-xs"
                    data-action="delete-tag"
                    [attr.aria-label]="'organize.tags.deleteFor' | transloco: { name: tag.name }"
                    (click)="confirmTag.set(tag.id)"
                  >{{ 'organize.delete' | transloco }}</button>
                }
              </li>
            }
          </ul>
        }
      </section>

      <!-- Folders -->
      <section [attr.aria-labelledby]="'organize-folders-title'">
        <h2 id="organize-folders-title" class="text-xl font-semibold mb-3">{{ 'organize.folders.title' | transloco }}</h2>

        <form class="flex flex-wrap items-end gap-3 mb-4" (ngSubmit)="addFolder()" novalidate>
          <div class="flex flex-col gap-1">
            <label for="new-folder-name" class="text-sm font-medium">{{ 'organize.folders.name' | transloco }}</label>
            <input id="new-folder-name" name="folderName" class="input input-sm" maxlength="100" autocomplete="off" [(ngModel)]="newFolderName" />
          </div>
          <div class="flex flex-col gap-1">
            <label for="new-folder-parent" class="text-sm font-medium">{{ 'organize.folders.parent' | transloco }}</label>
            <select id="new-folder-parent" name="folderParent" class="select select-sm" [(ngModel)]="newFolderParent">
              <option value="">{{ 'organize.folders.topLevel' | transloco }}</option>
              @for (option of organize.folderOptions(); track option.id) {
                <option [value]="option.id">{{ indent(option.depth) }}{{ option.name }}</option>
              }
            </select>
          </div>
          <button type="submit" class="btn btn-primary btn-sm" [disabled]="busy()">{{ 'organize.folders.add' | transloco }}</button>
        </form>
        @if (folderError(); as key) {
          <p class="text-error text-sm mb-3" role="alert">{{ key | transloco }}</p>
        }

        @if (organize.folderOptions().length === 0) {
          <p class="text-sm text-base-content/60">{{ 'organize.folders.empty' | transloco }}</p>
        } @else {
          <ul class="divide-y divide-base-300 rounded-box bg-base-200">
            @for (option of organize.folderOptions(); track option.id) {
              <li class="p-3">
                @if (editing()?.id === option.id) {
                  <form class="flex flex-wrap items-end gap-3" (ngSubmit)="saveFolder()" novalidate>
                    <div class="flex flex-col gap-1">
                      <label [for]="'edit-name-' + option.id" class="text-xs font-medium">{{ 'organize.folders.name' | transloco }}</label>
                      <input [id]="'edit-name-' + option.id" name="editName" class="input input-sm" maxlength="100" [(ngModel)]="editName" />
                    </div>
                    <div class="flex flex-col gap-1">
                      <label [for]="'edit-parent-' + option.id" class="text-xs font-medium">{{ 'organize.folders.parent' | transloco }}</label>
                      <select [id]="'edit-parent-' + option.id" name="editParent" class="select select-sm" [(ngModel)]="editParent">
                        <option value="">{{ 'organize.folders.topLevel' | transloco }}</option>
                        @for (target of organize.folderOptions(); track target.id) {
                          @if (target.id !== option.id) {
                            <option [value]="target.id">{{ indent(target.depth) }}{{ target.name }}</option>
                          }
                        }
                      </select>
                    </div>
                    <button type="submit" class="btn btn-primary btn-sm" [disabled]="busy()">{{ 'organize.save' | transloco }}</button>
                    <button type="button" class="btn btn-sm" (click)="editing.set(null)">{{ 'organize.cancel' | transloco }}</button>
                  </form>
                } @else {
                  <div class="flex items-center justify-between gap-3">
                    <span class="truncate">{{ indent(option.depth) }}{{ option.name }}</span>
                    @if (confirmFolder() === option.id) {
                      <span class="flex items-center gap-2">
                        <span class="text-sm">{{ 'organize.confirmFolder' | transloco }}</span>
                        <button type="button" class="btn btn-error btn-xs" data-action="confirm-delete-folder" [disabled]="busy()" (click)="deleteFolder(option.id)">{{ 'organize.delete' | transloco }}</button>
                        <button type="button" class="btn btn-xs" (click)="confirmFolder.set(null)">{{ 'organize.cancel' | transloco }}</button>
                      </span>
                    } @else {
                      <span class="flex items-center gap-1">
                        <button type="button" class="btn btn-ghost btn-xs" data-action="edit-folder" [attr.aria-label]="'organize.folders.editFor' | transloco: { name: option.name }" (click)="startEdit(option.id)">{{ 'organize.edit' | transloco }}</button>
                        <button type="button" class="btn btn-ghost btn-xs" data-action="delete-folder" [attr.aria-label]="'organize.folders.deleteFor' | transloco: { name: option.name }" (click)="confirmFolder.set(option.id)">{{ 'organize.delete' | transloco }}</button>
                      </span>
                    }
                  </div>
                }
              </li>
            }
          </ul>
        }
      </section>
    </div>
  `,
})
export class OrganizeComponent implements OnInit {
  readonly organize = inject(OrganizeService);
  private toast = inject(ToastService);
  private transloco = inject(TranslocoService);
  private pageTitle = inject(PageTitleService);

  readonly busy = signal(false);
  readonly loadFailed = signal(false);
  readonly tagError = signal<string | null>(null);
  readonly folderError = signal<string | null>(null);
  readonly confirmTag = signal<string | null>(null);
  readonly confirmFolder = signal<string | null>(null);
  readonly editing = signal<{ id: string } | null>(null);

  newTagName = '';
  newTagColor = '#6b7280';
  newFolderName = '';
  newFolderParent = '';
  editName = '';
  editParent = '';

  async ngOnInit(): Promise<void> {
    this.pageTitle.set('organize.pageTitle');
    try {
      await this.organize.load();
    } catch {
      this.loadFailed.set(true);
    }
  }

  indent(depth: number): string {
    return '\u00a0\u00a0'.repeat(depth);
  }

  async addTag(): Promise<void> {
    const name = this.newTagName.trim();
    if (!name || this.busy()) return;
    this.busy.set(true);
    this.tagError.set(null);
    try {
      await this.organize.createTag(name, this.newTagColor);
      this.newTagName = '';
    } catch (error) {
      this.tagError.set(this.errorKey(error, 'organize.tags.error'));
    } finally {
      this.busy.set(false);
    }
  }

  async deleteTag(tag: Tag): Promise<void> {
    this.confirmTag.set(null);
    await this.guard(() => this.organize.deleteTag(tag.id));
  }

  async addFolder(): Promise<void> {
    const name = this.newFolderName.trim();
    if (!name || this.busy()) return;
    this.busy.set(true);
    this.folderError.set(null);
    try {
      await this.organize.createFolder(name, this.newFolderParent || null);
      this.newFolderName = '';
    } catch (error) {
      this.folderError.set(this.errorKey(error, 'organize.folders.error'));
    } finally {
      this.busy.set(false);
    }
  }

  startEdit(id: string): void {
    const folder = this.organize.folders().find((f: Folder) => f.id === id);
    if (!folder) return;
    this.editName = folder.name;
    this.editParent = folder.parentId ?? '';
    this.folderError.set(null);
    this.editing.set({ id });
  }

  async saveFolder(): Promise<void> {
    const id = this.editing()?.id;
    const name = this.editName.trim();
    if (!id || !name || this.busy()) return;
    this.busy.set(true);
    this.folderError.set(null);
    try {
      await this.organize.updateFolder(id, name, this.editParent || null);
      this.editing.set(null);
    } catch (error) {
      this.folderError.set(this.errorKey(error, 'organize.folders.error'));
    } finally {
      this.busy.set(false);
    }
  }

  async deleteFolder(id: string): Promise<void> {
    this.confirmFolder.set(null);
    await this.guard(() => this.organize.deleteFolder(id));
  }

  private async guard(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
    } catch {
      this.toast.error(this.transloco.translate('organize.failed'));
    } finally {
      this.busy.set(false);
    }
  }

  /** 409 = name taken, 400 = invalid input; anything else is generic. Never the server's own text. */
  private errorKey(error: unknown, prefix: string): string {
    const status = error instanceof HttpErrorResponse ? error.status : 0;
    if (status === 409) return `${prefix}.duplicate`;
    if (status === 400) return `${prefix}.invalid`;
    return 'organize.failed';
  }
}
