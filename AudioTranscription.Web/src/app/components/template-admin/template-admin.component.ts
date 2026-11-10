import { Component, OnInit, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { PostprocessingTemplate } from '../../models/audio-job.model';
import { TemplateService } from '../../services/template.service';
import { ToastService } from '../../services/toast.service';
import { PageTitleService } from '../../i18n/page-title.service';

/** Admins manage the custom post-processing templates (S26); the built-in ones are listed but read only. */
@Component({
  selector: 'app-template-admin',
  standalone: true,
  imports: [FormsModule, TranslocoPipe],
  template: `
    <div class="max-w-3xl mx-auto space-y-8">
      <h1 class="text-3xl font-bold">{{ 'templates.title' | transloco }}</h1>
      <p class="text-sm text-base-content/70">{{ 'templates.intro' | transloco }}</p>

      @if (service.loadFailed()) {
        <div class="alert alert-error" role="alert">{{ 'templates.loadFailed' | transloco }}</div>
      }

      <form data-testid="template-form" class="space-y-3" (ngSubmit)="save()" novalidate>
        <h2 class="text-xl font-semibold">{{ (editingId() ? 'templates.editTitle' : 'templates.createTitle') | transloco }}</h2>

        <div class="flex flex-col gap-1">
          <label for="template-name" class="text-sm font-medium">{{ 'templates.name' | transloco }}</label>
          <input id="template-name" name="templateName" data-testid="template-name" class="input w-full" maxlength="100" autocomplete="off" [(ngModel)]="name" />
          @if (fieldError('name'); as message) {
            <p class="text-error text-sm" role="alert">{{ message }}</p>
          }
        </div>
        <div class="flex flex-col gap-1">
          <label for="template-description" class="text-sm font-medium">{{ 'templates.description' | transloco }}</label>
          <input id="template-description" name="templateDescription" class="input w-full" maxlength="300" autocomplete="off" [(ngModel)]="description" />
        </div>
        <div class="flex flex-col gap-1">
          <label for="template-prompt" class="text-sm font-medium">{{ 'templates.prompt' | transloco }}</label>
          <textarea id="template-prompt" name="templatePrompt" data-testid="template-prompt" class="textarea w-full h-32" aria-describedby="template-prompt-hint" [(ngModel)]="prompt"></textarea>
          <p id="template-prompt-hint" class="text-xs text-base-content/60">{{ 'templates.promptHint' | transloco }}</p>
          @if (fieldError('prompt'); as message) {
            <p class="text-error text-sm" role="alert">{{ message }}</p>
          }
        </div>
        <div class="flex flex-col gap-1">
          <label for="template-schema" class="text-sm font-medium">{{ 'templates.schema' | transloco }}</label>
          <textarea id="template-schema" name="templateSchema" data-testid="template-schema" class="textarea w-full h-40 font-mono text-sm" aria-describedby="template-schema-hint" [(ngModel)]="outputSchema"></textarea>
          <p id="template-schema-hint" class="text-xs text-base-content/60">{{ 'templates.schemaHint' | transloco }}</p>
          @if (fieldError('outputSchema'); as message) {
            <p class="text-error text-sm" role="alert">{{ message }}</p>
          }
        </div>

        <div class="flex gap-2">
          <button type="submit" class="btn btn-primary" data-testid="template-save" [disabled]="saving()">{{ 'templates.save' | transloco }}</button>
          @if (editingId()) {
            <button type="button" class="btn" (click)="reset()">{{ 'templates.cancel' | transloco }}</button>
          }
        </div>
      </form>

      <section>
        @if (service.templates().length === 0 && !service.loadFailed()) {
          <p class="text-sm text-base-content/70">{{ 'templates.empty' | transloco }}</p>
        }
        <ul class="space-y-2">
          @for (template of service.templates(); track template.id) {
            <li class="rounded-lg border border-base-300 p-3" data-testid="template-row">
              <div class="flex items-center justify-between gap-2">
                <div class="min-w-0">
                  <span class="font-medium">{{ template.name }}</span>
                  @if (template.isBuiltIn) {
                    <span class="badge badge-ghost ml-2">{{ 'templates.builtIn' | transloco }}</span>
                  }
                  @if (template.description) {
                    <p class="text-sm text-base-content/70">{{ template.description }}</p>
                  }
                </div>
                @if (!template.isBuiltIn) {
                  <div class="flex gap-1 shrink-0">
                    <button type="button" class="btn btn-ghost btn-sm" data-testid="template-edit"
                      [attr.aria-label]="'templates.edit' | transloco: { name: template.name }" (click)="edit(template)">✎</button>
                    @if (confirmDeleteId() === template.id) {
                      <button type="button" class="btn btn-error btn-sm" data-testid="template-delete-confirm"
                        [attr.aria-label]="'templates.delete' | transloco: { name: template.name }" (click)="remove(template)">{{ 'templates.confirmDelete' | transloco }}</button>
                      <button type="button" class="btn btn-ghost btn-sm" (click)="confirmDeleteId.set(null)">{{ 'templates.cancel' | transloco }}</button>
                    } @else {
                      <button type="button" class="btn btn-ghost btn-sm" data-testid="template-delete"
                        [attr.aria-label]="'templates.delete' | transloco: { name: template.name }" (click)="confirmDeleteId.set(template.id)">🗑</button>
                    }
                  </div>
                }
              </div>
            </li>
          }
        </ul>
      </section>
    </div>
  `,
})
export class TemplateAdminComponent implements OnInit {
  service = inject(TemplateService);
  private toast = inject(ToastService);
  private transloco = inject(TranslocoService);
  private pageTitle = inject(PageTitleService);

  name = '';
  description = '';
  prompt = '';
  outputSchema = '';
  readonly editingId = signal<string | null>(null);
  readonly saving = signal(false);
  readonly confirmDeleteId = signal<string | null>(null);
  private readonly errors = signal<Record<string, string[]>>({});

  ngOnInit(): void {
    this.pageTitle.set('templates.pageTitle');
    this.service.load();
  }

  fieldError(field: string): string | undefined {
    return this.errors()[field]?.[0];
  }

  edit(template: PostprocessingTemplate): void {
    this.editingId.set(template.id);
    this.name = template.name;
    this.description = template.description;
    this.prompt = template.prompt;
    this.outputSchema = template.outputSchema;
    this.errors.set({});
  }

  reset(): void {
    this.editingId.set(null);
    this.name = this.description = this.prompt = this.outputSchema = '';
    this.errors.set({});
  }

  save(): void {
    const request = { name: this.name, description: this.description, prompt: this.prompt, outputSchema: this.outputSchema };
    const id = this.editingId();
    this.saving.set(true);
    this.errors.set({});
    (id ? this.service.update(id, request) : this.service.create(request)).subscribe({
      next: () => {
        this.saving.set(false);
        this.reset();
        this.toast.success(this.transloco.translate('templates.saved'));
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        const fieldErrors = err.status === 400 ? (err.error?.errors as Record<string, string[]> | undefined) : undefined;
        if (fieldErrors) {
          // The server's validation messages are written for users (length, unknown placeholder, invalid schema)
          this.errors.set(Object.fromEntries(Object.entries(fieldErrors).map(([key, value]) => [key.charAt(0).toLowerCase() + key.slice(1), value])));
        } else {
          this.toast.error(this.transloco.translate('templates.saveFailed'));
        }
      },
    });
  }

  remove(template: PostprocessingTemplate): void {
    this.confirmDeleteId.set(null);
    this.service.delete(template.id).subscribe({
      next: () => this.toast.success(this.transloco.translate('templates.deleted')),
      error: () => this.toast.error(this.transloco.translate('templates.deleteFailed')),
    });
  }
}
