import { Component, inject, OnInit, signal, computed, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { AudioJobService } from '../../services/audio-job.service';
import { AudioJobStatus } from '../../models/audio-job.model';
import { ToastService } from '../../services/toast.service';

@Component({
  selector: 'app-job-detail',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <div class="max-w-4xl mx-auto">
      <a routerLink="/jobs" class="btn btn-ghost btn-sm mb-4 gap-1">
        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7" />
        </svg>
        Zurück zur Liste
      </a>

      @if (jobService.selectedJobLoading()) {
        <div class="space-y-4">
          <div class="skeleton h-8 w-64"></div>
          <div class="skeleton h-40 w-full rounded-xl"></div>
          <div class="skeleton h-64 w-full rounded-xl"></div>
        </div>
      } @else if (jobService.selectedJob(); as job) {
        <!-- Header -->
        <div class="flex items-start justify-between mb-6">
          <div>
            <h1 class="text-3xl font-bold">{{ job.fileName }}</h1>
            <p class="text-sm text-base-content/60 mt-1">
              {{ jobService.formatFileSize(job.fileSizeBytes) }} •
              {{ job.contentType }} •
              {{ job.createdAtUtc | date:'dd.MM.yyyy HH:mm:ss' }}
            </p>
          </div>
          <span class="badge badge-lg" [ngClass]="jobService.getStatusBadgeClass(job.status)">
            @if (job.status === AudioJobStatus.Processing) {
              <span class="loading loading-spinner loading-xs mr-1"></span>
            }
            {{ jobService.getStatusLabel(job.status) }}
          </span>
        </div>

        <!-- Metadata Cards -->
        <div class="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-6">
          <div class="card bg-base-200 shadow-sm">
            <div class="card-body p-4">
              <p class="text-xs uppercase tracking-wider text-base-content/50">Sprache</p>
              <p class="text-xl font-semibold">{{ job.language || 'Nicht erkannt' }}</p>
            </div>
          </div>
          <div class="card bg-base-200 shadow-sm">
            <div class="card-body p-4">
              <p class="text-xs uppercase tracking-wider text-base-content/50">Dauer</p>
              <p class="text-xl font-semibold">{{ jobService.formatDuration(job.durationSeconds) }}</p>
            </div>
          </div>
          <div class="card bg-base-200 shadow-sm">
            <div class="card-body p-4">
              <p class="text-xs uppercase tracking-wider text-base-content/50">Abgeschlossen</p>
              <p class="text-xl font-semibold">
                {{ job.completedAtUtc ? (job.completedAtUtc | date:'HH:mm:ss') : '-' }}
              </p>
            </div>
          </div>
        </div>

        <!-- Processing State -->
        @if (job.status === AudioJobStatus.Pending || job.status === AudioJobStatus.Processing) {
          <div class="card bg-base-200 shadow-sm">
            <div class="card-body items-center text-center py-12">
              <span class="loading loading-dots loading-lg text-primary"></span>
              <p class="text-lg mt-4">
                {{ job.status === AudioJobStatus.Pending ? 'Warte auf Verarbeitung...' : 'Transkription läuft...' }}
              </p>
              <p class="text-sm text-base-content/50">
                Das Ergebnis erscheint hier automatisch via Live-Update.
              </p>
            </div>
          </div>
        }

        <!-- Error State -->
        @if (job.status === AudioJobStatus.Failed) {
          <div class="alert alert-error">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4c-.77-.833-1.964-.833-2.732 0L3.268 16.5c-.77.833.192 2.5 1.732 2.5z" />
            </svg>
            <div>
              <p class="font-medium">Transkription fehlgeschlagen</p>
              <p class="text-sm">{{ job.errorMessage || 'Unbekannter Fehler' }}</p>
            </div>
          </div>
        }

        <!-- Transcript -->
        @if (job.status === AudioJobStatus.Completed && transcript(); as transcriptText) {
          <div class="card bg-base-200 shadow-sm">
            <div class="card-body">
              <div class="flex items-center justify-between mb-3 flex-wrap gap-2">
                <div class="flex items-center gap-3">
                  <h2 class="card-title text-lg">Transkript</h2>
                  <span class="text-xs text-base-content/50">
                    {{ wordCount() }} Wörter · {{ transcriptText.length }} Zeichen
                  </span>
                </div>
                <div class="flex gap-2">
                  <button
                    class="btn btn-sm btn-outline gap-1"
                    (click)="downloadTranscript(transcriptText, job.fileName)"
                  >
                    <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                    </svg>
                    Download .txt
                  </button>
                  <button
                    class="btn btn-sm gap-1"
                    [class.btn-ghost]="!copied()"
                    [class.btn-success]="copied()"
                    (click)="copyTranscript(transcriptText)"
                  >
                    @if (copied()) {
                      <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" />
                      </svg>
                      Kopiert!
                    } @else {
                      <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 16H6a2 2 0 01-2-2V6a2 2 0 012-2h8a2 2 0 012 2v2m-6 12h8a2 2 0 002-2v-8a2 2 0 00-2-2h-8a2 2 0 00-2 2v8a2 2 0 002 2z" />
                      </svg>
                      Kopieren
                    }
                  </button>
                </div>
              </div>
              <div class="bg-base-100 rounded-lg p-4 whitespace-pre-wrap leading-relaxed text-sm max-h-[500px] overflow-y-auto">
                {{ transcriptText }}
              </div>
            </div>
          </div>
        }
      } @else {
        <div class="alert alert-warning">
          <span>Job nicht gefunden.</span>
        </div>
      }
    </div>
  `,
})
export class JobDetailComponent implements OnInit {
  jobService = inject(AudioJobService);
  private route = inject(ActivatedRoute);
  private toastService = inject(ToastService);
  private titleService = inject(Title);
  AudioJobStatus = AudioJobStatus;
  copied = signal(false);

  /** Post-processed version if available, otherwise the raw Whisper output (toggle: S04-T4). */
  transcript = computed(() => {
    const job = this.jobService.selectedJob();
    return job?.processedTranscript ?? job?.rawTranscript ?? '';
  });

  wordCount = computed(() => {
    const text = this.transcript();
    return text ? text.trim().split(/\s+/).filter(Boolean).length : 0;
  });

  constructor() {
    // Keep the browser tab title in sync with the currently viewed file.
    effect(() => {
      const job = this.jobService.selectedJob();
      this.titleService.setTitle(job ? `${job.fileName} · Transkription` : 'Transkription');
    });
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.jobService.loadJob(id);
    }
  }

  copyTranscript(text: string): void {
    navigator.clipboard.writeText(text).then(() => {
      this.copied.set(true);
      this.toastService.success('Transkript in die Zwischenablage kopiert');
      setTimeout(() => this.copied.set(false), 2000);
    });
  }

  downloadTranscript(text: string, originalFileName: string): void {
    const blob = new Blob([text], { type: 'text/plain' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${originalFileName}.txt`;
    a.click();
    window.URL.revokeObjectURL(url);
    this.toastService.success('Download gestartet');
  }
}


