import { Component, OnInit, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AdminJobService } from '../../services/admin-job.service';
import { AdminUser, AdminUserService } from '../../services/admin-user.service';
import { ToastService } from '../../services/toast.service';
import { PageTitleService } from '../../i18n/page-title.service';
import { AudioJobListItem } from '../../models/audio-job.model';

/**
 * Jobs from before ownership existed (S19) belong to nobody and nobody can see them. Admins give each of them to a user
 * here. The API decides what is allowed (a job that already has an owner answers 404); this page shows clean messages.
 */
@Component({
  selector: 'app-unassigned-jobs',
  standalone: true,
  imports: [DatePipe, TranslocoPipe],
  template: `
    <section class="max-w-4xl mx-auto" aria-labelledby="unassigned-title">
      <h1 id="unassigned-title" class="text-3xl font-bold mb-2">{{ 'admin.unassigned.title' | transloco }}</h1>
      <p class="text-sm text-base-content/70 mb-6">{{ 'admin.unassigned.intro' | transloco }}</p>

      @if (loading()) {
        <div class="skeleton h-32 w-full" role="status">
          <span class="sr-only">{{ 'admin.unassigned.loading' | transloco }}</span>
        </div>
      } @else if (loadFailed()) {
        <div class="alert alert-error" role="alert">
          <span>{{ 'admin.unassigned.loadFailed' | transloco }}</span>
          <button type="button" class="btn btn-sm" data-testid="unassigned-retry" (click)="load()">{{ 'admin.unassigned.retry' | transloco }}</button>
        </div>
      } @else if (jobs().length === 0) {
        <p class="text-base-content/70" data-testid="unassigned-empty">{{ 'admin.unassigned.empty' | transloco }}</p>
      } @else {
        <div class="overflow-x-auto">
          <table class="table bg-base-200 rounded-lg">
            <caption class="sr-only">{{ 'admin.unassigned.caption' | transloco }}</caption>
            <thead>
              <tr>
                <th scope="col">{{ 'admin.unassigned.file' | transloco }}</th>
                <th scope="col">{{ 'admin.unassigned.created' | transloco }}</th>
                <th scope="col">{{ 'admin.unassigned.owner' | transloco }}</th>
                <th scope="col"><span class="sr-only">{{ 'admin.unassigned.actions' | transloco }}</span></th>
              </tr>
            </thead>
            <tbody>
              @for (job of jobs(); track job.id) {
                <tr data-testid="unassigned-row">
                  <th scope="row" class="font-medium">{{ job.fileName }}</th>
                  <td>{{ job.createdAtUtc | date: 'medium' }}</td>
                  <td>
                    <select
                      class="select select-sm"
                      [attr.aria-label]="'admin.unassigned.chooseUser' | transloco: { file: job.fileName }"
                      (change)="choose(job.id, $any($event.target).value)"
                    >
                      <option value="">{{ 'admin.unassigned.choosePlaceholder' | transloco }}</option>
                      @for (user of users(); track user.id) {
                        <option [value]="user.id">{{ user.email }}</option>
                      }
                    </select>
                  </td>
                  <td class="text-right">
                    <button
                      type="button"
                      class="btn btn-sm btn-primary"
                      data-testid="unassigned-assign"
                      [disabled]="!chosenUser(job.id) || assigning() === job.id"
                      [attr.aria-label]="'admin.unassigned.assignLabel' | transloco: { file: job.fileName }"
                      (click)="assign(job)"
                    >
                      {{ 'admin.unassigned.assign' | transloco }}
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </section>
  `,
})
export class UnassignedJobsComponent implements OnInit {
  private jobService = inject(AdminJobService);
  private userService = inject(AdminUserService);
  private toast = inject(ToastService);
  private transloco = inject(TranslocoService);
  private pageTitle = inject(PageTitleService);

  readonly jobs = signal<AudioJobListItem[]>([]);
  /** Active users, the candidates for an owner. */
  readonly users = signal<AdminUser[]>([]);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly assigning = signal<string | null>(null);
  /** The user picked for each job, by job id. */
  private readonly choices = signal<Record<string, string>>({});

  ngOnInit(): void {
    this.pageTitle.set('admin.unassigned.pageTitle');
    void this.load();
  }

  chosenUser(jobId: string): string {
    return this.choices()[jobId] ?? '';
  }

  choose(jobId: string, userId: string): void {
    this.choices.update((current) => ({ ...current, [jobId]: userId }));
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailed.set(false);
    try {
      const [jobs, users] = await Promise.all([this.jobService.listUnassigned(), this.allUsers()]);
      this.jobs.set(jobs);
      this.users.set(users.filter((u) => u.isActive));
    } catch {
      this.loadFailed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  async assign(job: AudioJobListItem): Promise<void> {
    const userId = this.chosenUser(job.id);
    if (!userId) {
      return;
    }
    this.assigning.set(job.id);
    try {
      await this.jobService.assignOwner(job.id, userId);
      this.jobs.update((list) => list.filter((j) => j.id !== job.id));
      this.toast.success(this.transloco.translate('admin.unassigned.assigned', { file: job.fileName }));
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        // Someone else was faster: show what is still open instead of the stale list
        this.toast.error(this.transloco.translate('admin.unassigned.gone'));
        await this.load();
      } else {
        this.toast.error(this.transloco.translate('admin.unassigned.failed'));
      }
    } finally {
      this.assigning.set(null);
    }
  }

  /** All pages of users: the API returns at most 100 at once. */
  private async allUsers(): Promise<AdminUser[]> {
    const all: AdminUser[] = [];
    for (let page = 1; ; page++) {
      const result = await this.userService.list(page, 100);
      all.push(...result.items);
      if (all.length >= result.total || result.items.length === 0) {
        return all;
      }
    }
  }
}
