import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { Glossary } from '../../models/audio-job.model';
import { GlossaryService } from '../../services/glossary.service';
import { ToastService } from '../../services/toast.service';
import { PageTitleService } from '../../i18n/page-title.service';

/** Manage the user's glossaries (S22): terms to be recognized correctly, one per line. */
@Component({
  selector: 'app-glossary',
  standalone: true,
  imports: [FormsModule, TranslocoPipe],
  template: `
    <div class="max-w-3xl mx-auto space-y-8">
      <h1 class="text-3xl font-bold">{{ 'glossary.title' | transloco }}</h1>
      <p class="text-sm text-base-content/70">{{ 'glossary.intro' | transloco }}</p>

      @if (loadFailed()) {
        <div class="alert alert-error" role="alert">{{ 'glossary.loadFailed' | transloco }}</div>
      }

      <form data-testid="glossary-form" class="space-y-3" (ngSubmit)="save()" novalidate>
        <h2 class="text-xl font-semibold">
          {{ (editingId() ? 'glossary.editTitle' : 'glossary.createTitle') | transloco }}
        </h2>
        <div class="flex flex-col gap-1">
          <label for="glossary-name" class="text-sm font-medium">{{ 'glossary.name' | transloco }}</label>
          <input
            id="glossary-name"
            name="glossaryName"
            data-testid="glossary-name"
            class="input w-full"
            maxlength="100"
            autocomplete="off"
            [(ngModel)]="name"
          />
        </div>
        <div class="flex flex-col gap-1">
          <label for="glossary-terms" class="text-sm font-medium">{{ 'glossary.terms' | transloco }}</label>
          <textarea
            id="glossary-terms"
            name="glossaryTerms"
            data-testid="glossary-terms"
            class="textarea w-full h-40"
            aria-describedby="glossary-terms-hint glossary-tokens"
            [(ngModel)]="termsText"
          ></textarea>
          <p id="glossary-terms-hint" class="text-xs text-base-content/60">{{ 'glossary.termsHint' | transloco }}</p>
          <p
            id="glossary-tokens"
            data-testid="glossary-tokens"
            class="text-xs"
            [class.text-error]="tooLong()"
          >{{ 'glossary.tokens' | transloco: { used: tokens(), max: maxTokens } }}</p>
          @if (tooLong()) {
            <p class="text-error text-sm" role="alert" data-testid="glossary-too-long">{{ 'glossary.tooLong' | transloco }}</p>
          }
        </div>
        @if (errorKey(); as key) {
          <p class="text-error text-sm" role="alert" data-testid="glossary-error">{{ key | transloco }}</p>
        }
        <div class="flex gap-2">
          <button type="submit" class="btn btn-primary btn-sm" data-testid="glossary-save" [disabled]="busy() || tooLong() || !canSave()">
            {{ 'glossary.save' | transloco }}
          </button>
          @if (editingId()) {
            <button type="button" class="btn btn-sm" (click)="cancelEdit()">{{ 'glossary.cancel' | transloco }}</button>
          }
        </div>
      </form>

      <section aria-labelledby="glossary-list-title">
        <h2 id="glossary-list-title" class="text-xl font-semibold mb-3">{{ 'glossary.listTitle' | transloco }}</h2>
        @if (glossaries.glossaries().length === 0) {
          <p class="text-sm text-base-content/60">{{ 'glossary.empty' | transloco }}</p>
        } @else {
          <ul class="divide-y divide-base-300 rounded-box bg-base-200">
            @for (glossary of glossaries.glossaries(); track glossary.id) {
              <li class="p-3 space-y-2">
                <div class="flex items-center justify-between gap-3">
                  <div class="min-w-0">
                    <span class="font-medium">{{ glossary.name }}</span>
                    <span class="text-xs text-base-content/60 ml-2">{{ 'glossary.termCount' | transloco: { count: glossary.terms.length } }}</span>
                  </div>
                  @if (confirmId() === glossary.id) {
                    <span class="flex items-center gap-2">
                      <span class="text-sm">{{ 'glossary.confirmDelete' | transloco }}</span>
                      <button type="button" class="btn btn-error btn-xs" data-action="confirm-delete-glossary" [disabled]="busy()" (click)="remove(glossary)">{{ 'glossary.delete' | transloco }}</button>
                      <button type="button" class="btn btn-xs" (click)="confirmId.set(null)">{{ 'glossary.cancel' | transloco }}</button>
                    </span>
                  } @else {
                    <span class="flex items-center gap-1">
                      <button type="button" class="btn btn-ghost btn-xs" data-action="edit-glossary" [attr.aria-label]="'glossary.editFor' | transloco: { name: glossary.name }" (click)="startEdit(glossary)">{{ 'glossary.edit' | transloco }}</button>
                      <button type="button" class="btn btn-ghost btn-xs" data-action="delete-glossary" [attr.aria-label]="'glossary.deleteFor' | transloco: { name: glossary.name }" (click)="confirmId.set(glossary.id)">{{ 'glossary.delete' | transloco }}</button>
                    </span>
                  }
                </div>
                <ul class="flex flex-wrap gap-1" [attr.aria-label]="'glossary.terms' | transloco">
                  @for (term of glossary.terms; track term) {
                    <li class="badge badge-outline">{{ term }}</li>
                  }
                </ul>
              </li>
            }
          </ul>
        }
      </section>
    </div>
  `,
})
export class GlossaryComponent implements OnInit {
  readonly glossaries = inject(GlossaryService);
  private toast = inject(ToastService);
  private transloco = inject(TranslocoService);
  private pageTitle = inject(PageTitleService);

  readonly maxTokens = 224;
  readonly busy = signal(false);
  readonly loadFailed = signal(false);
  readonly errorKey = signal<string | null>(null);
  readonly editingId = signal<string | null>(null);
  readonly confirmId = signal<string | null>(null);

  name = '';
  private readonly termsSignal = signal('');

  get termsText(): string {
    return this.termsSignal();
  }
  set termsText(value: string) {
    this.termsSignal.set(value);
  }

  private readonly terms = computed(() => this.parseTerms(this.termsSignal()));
  readonly tokens = computed(() => this.glossaries.estimateTokens(this.terms()));
  readonly tooLong = computed(() => this.tokens() > this.maxTokens);

  canSave(): boolean {
    return this.name.trim().length > 0;
  }

  async ngOnInit(): Promise<void> {
    this.pageTitle.set('glossary.pageTitle');
    try {
      await this.glossaries.load();
    } catch {
      this.loadFailed.set(true);
    }
  }

  startEdit(glossary: Glossary): void {
    this.editingId.set(glossary.id);
    this.name = glossary.name;
    this.termsSignal.set(glossary.terms.join('\n'));
    this.errorKey.set(null);
  }

  cancelEdit(): void {
    this.editingId.set(null);
    this.name = '';
    this.termsSignal.set('');
    this.errorKey.set(null);
  }

  async save(): Promise<void> {
    const name = this.name.trim();
    if (!name || this.tooLong() || this.busy()) return;
    this.busy.set(true);
    this.errorKey.set(null);
    try {
      const id = this.editingId();
      if (id) {
        await this.glossaries.update(id, name, this.terms());
      } else {
        await this.glossaries.create(name, this.terms());
      }
      this.cancelEdit();
    } catch (error) {
      this.errorKey.set(this.keyFor(error));
    } finally {
      this.busy.set(false);
    }
  }

  async remove(glossary: Glossary): Promise<void> {
    this.confirmId.set(null);
    this.busy.set(true);
    try {
      await this.glossaries.delete(glossary.id);
      if (this.editingId() === glossary.id) this.cancelEdit();
    } catch {
      this.toast.error(this.transloco.translate('glossary.failed'));
    } finally {
      this.busy.set(false);
    }
  }

  /** One term per line; blank lines dropped, whitespace trimmed. */
  private parseTerms(text: string): string[] {
    return text.split(/\r?\n/).map((line) => line.trim()).filter((line) => line.length > 0);
  }

  /** 409 = name taken, 400 = invalid input; anything else is generic. Never the server's own text. */
  private keyFor(error: unknown): string {
    const status = error instanceof HttpErrorResponse ? error.status : 0;
    if (status === 409) return 'glossary.duplicate';
    if (status === 400) return 'glossary.invalid';
    return 'glossary.failed';
  }
}
