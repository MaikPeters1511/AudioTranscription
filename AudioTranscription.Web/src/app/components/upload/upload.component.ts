import { Component, computed, inject, signal, OnInit } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { AudioJobService } from '../../services/audio-job.service';
import { ToastService } from '../../services/toast.service';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { PageTitleService } from '../../i18n/page-title.service';
import { languageName } from '../../i18n/language-names';
import { AUTO_LANGUAGE, TranscriptionOptions } from '../../models/audio-job.model';

@Component({
  selector: 'app-upload',
  standalone: true,
  imports: [CommonModule, TranslocoPipe],
  template: `
    <div class="max-w-2xl mx-auto">
      <h1 class="text-3xl font-bold mb-6">{{ 'upload.title' | transloco }}</h1>

      <!-- Transcription settings; without options from the server, its defaults apply -->
      @if (options(); as opts) {
        <div class="grid grid-cols-1 sm:grid-cols-2 gap-4 mb-6">
          <div class="flex flex-col gap-1">
            <label for="upload-model" class="text-sm font-medium">{{ 'upload.settings.model' | transloco }}</label>
            <select
              id="upload-model"
              class="select w-full"
              aria-describedby="upload-model-hint"
              [disabled]="uploading()"
              [value]="selectedModel()"
              (change)="selectedModel.set($any($event.target).value)"
            >
              @for (model of opts.models; track model) {
                <option [value]="model" [selected]="model === selectedModel()">{{ model }}</option>
              }
            </select>
            <p id="upload-model-hint" class="text-xs text-base-content/60">{{ 'upload.settings.modelHint' | transloco }}</p>
          </div>
          <div class="flex flex-col gap-1">
            <label for="upload-language" class="text-sm font-medium">{{ 'upload.settings.language' | transloco }}</label>
            <select
              id="upload-language"
              class="select w-full"
              [disabled]="uploading()"
              [value]="selectedLanguage()"
              (change)="selectedLanguage.set($any($event.target).value)"
            >
              <option [value]="autoLanguage" [selected]="selectedLanguage() === autoLanguage">
                {{ 'upload.settings.auto' | transloco }}
              </option>
              @for (language of languageOptions(); track language.code) {
                <option [value]="language.code" [selected]="language.code === selectedLanguage()">{{ language.name }}</option>
              }
            </select>
          </div>
        </div>
      }

      <!-- Drop Zone -->
      <div
        class="border-2 border-dashed rounded-2xl p-12 text-center transition-all duration-300"
        [class.border-primary]="isDragging()"
        [class.bg-primary/5]="isDragging()"
        [class.border-base-300]="!isDragging()"
        [class.hover:border-primary]="!uploading()"
        [class.cursor-pointer]="!uploading()"
        [class.cursor-default]="uploading()"
        (dragover)="onDragOver($event)"
        (dragleave)="onDragLeave($event)"
        (drop)="onDrop($event)"
        (click)="!uploading() && fileInput.click()"
      >
        @if (!uploading()) {
          <div class="flex flex-col items-center gap-4">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-16 w-16 text-base-content/30" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12" />
            </svg>
            <div>
              <p class="text-lg font-medium">{{ 'upload.dropTitle' | transloco }}</p>
              <p class="text-sm text-base-content/60 mt-1">{{ 'upload.dropHint' | transloco }}</p>
            </div>
            <p class="text-xs text-base-content/40">{{ 'upload.formats' | transloco }}</p>
          </div>
        } @else {
          <div class="flex flex-col items-center gap-4">
            <span class="loading loading-spinner loading-lg text-primary"></span>
            <div class="w-full max-w-xs">
              <p class="text-lg font-medium truncate text-center">{{ selectedFileName() }}</p>
              <progress
                class="progress progress-primary w-full mt-2"
                [value]="uploadProgress()"
                max="100"
                [attr.aria-label]="'upload.progress' | transloco"
              ></progress>
              <p class="text-sm text-base-content/60 mt-1 text-center">{{ uploadProgress() }}%</p>
            </div>
            <button class="btn btn-sm btn-ghost" (click)="cancelUpload($event)">
              {{ 'upload.cancel' | transloco }}
            </button>
          </div>
        }
      </div>

      <input
        #fileInput
        type="file"
        class="hidden"
        [attr.aria-label]="'upload.dropZone' | transloco"
        accept=".mp3,.wav,.m4a,.ogg,audio/mpeg,audio/wav,audio/mp4,audio/ogg"
        (change)="onFileSelected($event)"
      />

      <!-- Error Message -->
      @if (errorMessage()) {
        <div class="alert alert-error mt-4">
          <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4c-.77-.833-1.964-.833-2.732 0L3.268 16.5c-.77.833.192 2.5 1.732 2.5z" />
          </svg>
          <span>{{ errorMessage() }}</span>
        </div>
      }

      <!-- Success Message -->
      @if (successMessage()) {
        <div class="alert alert-success mt-4">
          <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          <span>{{ successMessage() }}</span>
        </div>
      }
    </div>
  `,
})
export class UploadComponent implements OnInit {
  private audioJobService = inject(AudioJobService);
  private router = inject(Router);
  private toastService = inject(ToastService);
  private pageTitle = inject(PageTitleService);
  private transloco = inject(TranslocoService);
  private uploadSubscription?: Subscription;

  isDragging = signal(false);
  uploading = signal(false);
  uploadProgress = signal(0);
  errorMessage = signal('');
  successMessage = signal('');
  selectedFileName = signal('');

  readonly autoLanguage = AUTO_LANGUAGE;
  options = signal<TranscriptionOptions | null>(null);
  selectedModel = signal('');
  selectedLanguage = signal(AUTO_LANGUAGE);
  private activeLang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });
  /** Selectable languages in the configured order, named in the UI language. */
  languageOptions = computed(() => {
    const uiLanguage = this.activeLang();
    return (this.options()?.languages ?? []).map((code) => ({ code, name: languageName(code, uiLanguage) }));
  });

  private readonly maxSize = 10_485_760; // 10 MB
  private readonly allowedTypes = [
    'audio/mpeg', 'audio/wav', 'audio/x-wav',
    'audio/mp4', 'audio/x-m4a', 'audio/ogg',
  ];

  ngOnInit(): void {
    this.pageTitle.set('upload.pageTitle');
    this.audioJobService.loadTranscriptionOptions().subscribe({
      next: (options) => {
        this.options.set(options);
        this.selectedModel.set(options.defaultModel);
      },
      // Uploads still work without the selects: the server uses its default model and detection
      error: () => this.options.set(null),
    });
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    if (this.uploading()) return;
    this.isDragging.set(true);
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragging.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragging.set(false);
    if (this.uploading()) return;

    const files = event.dataTransfer?.files;
    if (files?.length) {
      this.processFile(files[0]);
    }
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files?.length) {
      this.processFile(input.files[0]);
      input.value = '';
    }
  }

  cancelUpload(event: Event): void {
    event.stopPropagation();
    this.uploadSubscription?.unsubscribe();
    this.uploading.set(false);
    this.uploadProgress.set(0);
    this.toastService.show(this.transloco.translate('upload.cancelled'), 'info');
  }

  private processFile(file: File): void {
    this.errorMessage.set('');
    this.successMessage.set('');

    // Client-side validation
    if (file.size > this.maxSize) {
      this.errorMessage.set(
        this.transloco.translate('upload.tooLarge', { size: this.audioJobService.formatFileSize(file.size) })
      );
      return;
    }

    if (!this.allowedTypes.includes(file.type) && !this.isAllowedExtension(file.name)) {
      this.errorMessage.set(this.transloco.translate('upload.invalidType'));
      return;
    }

    this.selectedFileName.set(file.name);
    this.uploading.set(true);
    this.uploadProgress.set(0);

    this.uploadSubscription = this.audioJobService.uploadFile(file, this.transcriptionSettings()).subscribe({
      next: (event) => {
        this.uploadProgress.set(event.progress);
        if (event.jobId) {
          this.uploading.set(false);
          this.toastService.success(this.transloco.translate('upload.success'));
          this.router.navigate(['/jobs', event.jobId]);
        }
      },
      error: (err) => {
        this.uploading.set(false);
        const detail = err.error?.detail || err.error?.title || this.transloco.translate('upload.failed');
        this.errorMessage.set(detail);
        this.toastService.error(detail);
      },
    });
  }

  private transcriptionSettings() {
    return this.options() ? { model: this.selectedModel(), language: this.selectedLanguage() } : {};
  }

  private isAllowedExtension(name: string): boolean {
    const ext = name.toLowerCase().split('.').pop();
    return ['mp3', 'wav', 'm4a', 'ogg'].includes(ext || '');
  }
}
