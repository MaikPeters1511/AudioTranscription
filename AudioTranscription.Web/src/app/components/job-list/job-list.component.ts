import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { animate, query, stagger, style, transition, trigger } from '@angular/animations';
import { AudioJobService } from '../../services/audio-job.service';
import { AudioJobStatus } from '../../models/audio-job.model';
import { JobActionsComponent } from '../job-actions/job-actions.component';
import { TranslocoPipe } from '@jsverse/transloco';
import { PageTitleService } from '../../i18n/page-title.service';

@Component({
  selector: 'app-job-list',
  standalone: true,
  imports: [CommonModule, RouterLink, TranslocoPipe, JobActionsComponent],
  animations: [
    trigger('listAnimation', [
      transition('* <=> *', [
        query(':enter', [
          style({ opacity: 0, transform: 'translateY(10px)' }),
          stagger('50ms', animate('300ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })))
        ], { optional: true })
      ])
    ])
  ],
  template: `
    <div class="max-w-5xl mx-auto">
      <div class="flex items-center justify-between mb-6 gap-3">
        <div>
          <h1 class="text-3xl font-bold">{{ 'jobList.title' | transloco }}</h1>
          @if (!jobService.loading() && jobService.jobs().length > 0) {
            <p class="text-sm text-base-content/50 mt-1">
              {{ 'jobList.count' | transloco: { shown: jobService.jobs().length, total: jobService.totalCount() } }}
            </p>
          }
        </div>
        <div class="flex items-center gap-2">
          <button
            class="btn btn-ghost btn-circle btn-sm"
            [disabled]="jobService.loading()"
            (click)="refresh()"
            [attr.aria-label]="'jobList.refresh' | transloco"
            [title]="'jobList.refresh' | transloco"
          >
            <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" [class.animate-spin]="jobService.loading()" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
            </svg>
          </button>
          <a routerLink="/upload" class="btn btn-primary">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
            </svg>
            {{ 'jobList.newFile' | transloco }}
          </a>
        </div>
      </div>

      @if (jobService.loading()) {
        <!-- Skeleton Loading -->
        <div class="space-y-3">
          @for (i of [1,2,3,4,5]; track i) {
            <div class="skeleton h-14 w-full rounded-lg"></div>
          }
        </div>
      } @else if (jobService.jobs().length === 0) {
        <!-- Empty State -->
        <div class="text-center py-16">
          <svg xmlns="http://www.w3.org/2000/svg" class="h-20 w-20 mx-auto text-base-content/20" fill="none" viewBox="0 0 24 24" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1" d="M19 11a7 7 0 01-7 7m0 0a7 7 0 01-7-7m7 7v4m0 0H8m4 0h4m-4-8a3 3 0 01-3-3V5a3 3 0 116 0v6a3 3 0 01-3 3z" />
          </svg>
          <p class="mt-4 text-lg text-base-content/50">{{ 'jobList.empty' | transloco }}</p>
          <a routerLink="/upload" class="btn btn-primary mt-4">{{ 'jobList.emptyCta' | transloco }}</a>
        </div>
      } @else {
        <!-- Job Table (desktop) -->
        <div class="hidden sm:block overflow-x-auto rounded-xl border border-base-300">
          <table class="table table-zebra">
            <thead>
              <tr class="bg-base-200">
                <th>{{ 'jobList.columns.fileName' | transloco }}</th>
                <th>{{ 'jobList.columns.size' | transloco }}</th>
                <th>{{ 'jobList.columns.status' | transloco }}</th>
                <th>{{ 'jobList.columns.language' | transloco }}</th>
                <th>{{ 'jobList.columns.duration' | transloco }}</th>
                <th>{{ 'jobList.columns.created' | transloco }}</th>
                <th><span class="sr-only">{{ 'jobList.columns.actions' | transloco }}</span></th>
                <th></th>
              </tr>
            </thead>
            <tbody [@listAnimation]="jobService.jobs().length">
              @for (job of jobService.jobs(); track job.id) {
                <tr class="hover:bg-base-200/50 cursor-pointer" [routerLink]="['/jobs', job.id]">
                  <td class="font-medium max-w-[200px] truncate">{{ job.fileName }}</td>
                  <td class="text-sm text-base-content/70">{{ jobService.formatFileSize(job.fileSizeBytes) }}</td>
                  <td>
                    <span class="badge badge-sm" [ngClass]="jobService.getStatusBadgeClass(job.status)">
                      @if (job.status === AudioJobStatus.Processing) {
                        <span class="loading loading-spinner loading-xs mr-1"></span>
                      }
                      {{ jobService.getStatusLabelKey(job.status) | transloco }}
                    </span>
                  </td>
                  <td class="text-sm">{{ job.language || '-' }}</td>
                  <td class="text-sm">{{ jobService.formatDuration(job.durationSeconds) }}</td>
                  <td class="text-sm text-base-content/70">{{ job.createdAtUtc | date: ('format.dateTime' | transloco) }}</td>
                  <!-- Actions must not open the job (the row itself is a link) -->
                  <td (click)="$event.stopPropagation()" (keydown)="$event.stopPropagation()">
                    <app-job-actions [job]="job" [compact]="true" />
                  </td>
                  <td>
                    <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4 text-base-content/40" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7" />
                    </svg>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <!-- Job Cards (mobile) -->
        <div class="sm:hidden space-y-3" [@listAnimation]="jobService.jobs().length">
          @for (job of jobService.jobs(); track job.id) {
            <a
              [routerLink]="['/jobs', job.id]"
              class="card bg-base-200 shadow-sm active:scale-[0.99] transition-transform"
            >
              <div class="card-body p-4 gap-2">
                <div class="flex items-start justify-between gap-2">
                  <p class="font-medium truncate">{{ job.fileName }}</p>
                  <span class="badge badge-sm shrink-0" [ngClass]="jobService.getStatusBadgeClass(job.status)">
                    @if (job.status === AudioJobStatus.Processing) {
                      <span class="loading loading-spinner loading-xs mr-1"></span>
                    }
                    {{ jobService.getStatusLabelKey(job.status) | transloco }}
                  </span>
                </div>
                <div class="flex items-center gap-3 text-xs text-base-content/60">
                  <span>{{ jobService.formatFileSize(job.fileSizeBytes) }}</span>
                  <span>•</span>
                  <span>{{ jobService.formatDuration(job.durationSeconds) }}</span>
                  <span>•</span>
                  <span>{{ job.createdAtUtc | date: ('format.dateTime' | transloco) }}</span>
                </div>
              </div>
            </a>
          }
        </div>

        <!-- Pagination -->
        @if (jobService.totalPages() > 1) {
          <div class="flex justify-center mt-6">
            <div class="join">
              <button
                class="join-item btn btn-sm"
                [disabled]="jobService.currentPage() <= 1"
                (click)="jobService.loadJobs(jobService.currentPage() - 1)"
                [attr.aria-label]="'jobList.pagination.previous' | transloco"
              >«</button>
              @for (page of pages(); track page) {
                <button
                  class="join-item btn btn-sm"
                  [class.btn-active]="page === jobService.currentPage()"
                  (click)="jobService.loadJobs(page)"
                  [attr.aria-label]="'jobList.pagination.page' | transloco: { page }"
                  [attr.aria-current]="page === jobService.currentPage() ? 'page' : null"
                >{{ page }}</button>
              }
              <button
                class="join-item btn btn-sm"
                [disabled]="jobService.currentPage() >= jobService.totalPages()"
                (click)="jobService.loadJobs(jobService.currentPage() + 1)"
                [attr.aria-label]="'jobList.pagination.next' | transloco"
              >»</button>
            </div>
          </div>
        }
      }
    </div>
  `,
})
export class JobListComponent implements OnInit {
  jobService = inject(AudioJobService);
  private pageTitle = inject(PageTitleService);
  AudioJobStatus = AudioJobStatus;

  pages = () => {
    const total = this.jobService.totalPages();
    return Array.from({ length: total }, (_, i) => i + 1);
  };

  ngOnInit(): void {
    this.pageTitle.set('jobList.pageTitle');
    this.jobService.loadJobs();
  }

  refresh(): void {
    this.jobService.loadJobs(this.jobService.currentPage());
  }
}
