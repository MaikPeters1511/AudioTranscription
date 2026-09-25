import { Component, computed, inject, signal } from '@angular/core';
import { toObservable, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { debounceTime, distinctUntilChanged, of, switchMap } from 'rxjs';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { SearchService } from '../../services/search.service';
import { PaginatedResult, SearchHit } from '../../models/audio-job.model';
import { PageTitleService } from '../../i18n/page-title.service';

/**
 * Full-text search across all transcripts (S13): a debounced search box and a results list that
 * links back to the job detail, jumping straight to the matched segment's time when there is one.
 */
@Component({
  selector: 'app-search',
  standalone: true,
  imports: [RouterLink, TranslocoPipe],
  template: `
    <search class="max-w-2xl mx-auto block" [attr.aria-label]="'search.landmark' | transloco">
      <h1 class="text-3xl font-bold mb-6">{{ 'search.title' | transloco }}</h1>

      <div class="flex flex-col gap-1 mb-6">
        <label for="search-input" class="text-sm font-medium">{{ 'search.label' | transloco }}</label>
        <input
          id="search-input"
          type="search"
          class="input w-full"
          aria-describedby="search-status"
          [value]="query()"
          (input)="query.set($any($event.target).value)"
        />
      </div>

      <p id="search-status" class="sr-only" aria-live="polite">{{ statusText() }}</p>

      @if (loading()) {
        <div class="skeleton h-24 w-full" role="status">
          <span class="sr-only">{{ 'search.loading' | transloco }}</span>
        </div>
      } @else if (query().trim().length === 0) {
        <p class="text-sm text-base-content/60">{{ 'search.hint' | transloco }}</p>
      } @else if (results(); as r) {
        @if (r.items.length === 0) {
          <p class="text-sm text-base-content/60">{{ 'search.empty' | transloco: { query: query() } }}</p>
        } @else {
          <ul class="space-y-3">
            @for (hit of r.items; track hit.jobId + '|' + hit.snippet + '|' + (hit.segmentStartMs ?? '')) {
              <li class="card bg-base-200 shadow-sm">
                <a
                  class="card-body p-4 block hover:bg-base-300 rounded-lg"
                  [routerLink]="['/jobs', hit.jobId]"
                  [queryParams]="hit.segmentStartMs != null ? { t: hit.segmentStartMs } : {}"
                >
                  <p class="font-medium">{{ hit.fileName }}</p>
                  <p class="text-sm text-base-content/80">
                    {{ before(hit) }}@if (hit.highlightLength > 0) {<mark>{{ match(hit) }}</mark>}{{ after(hit) }}
                  </p>
                </a>
              </li>
            }
          </ul>
          <p class="text-xs text-base-content/60 mt-3">{{ 'search.resultCount' | transloco: { count: r.totalCount } }}</p>
        }
      }
    </search>
  `,
})
export class SearchComponent {
  private searchService = inject(SearchService);
  private pageTitle = inject(PageTitleService);
  private transloco = inject(TranslocoService);

  readonly query = signal('');
  readonly loading = signal(false);
  readonly results = signal<PaginatedResult<SearchHit> | null>(null);

  readonly statusText = computed(() => {
    if (this.loading()) {
      return '';
    }
    const r = this.results();
    if (r === null) {
      return '';
    }
    return r.items.length === 0
      ? this.transloco.translate('search.empty', { query: this.query() })
      : this.transloco.translate('search.resultCount', { count: r.totalCount });
  });

  constructor() {
    this.pageTitle.set('search.pageTitle');

    toObservable(this.query)
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        switchMap((query) => {
          const trimmed = query.trim();
          if (!trimmed) {
            this.results.set(null);
            this.loading.set(false);
            return of(null);
          }
          this.loading.set(true);
          return this.searchService.search(trimmed);
        }),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        this.loading.set(false);
        if (result) {
          this.results.set(result);
        }
      });
  }

  before(hit: SearchHit): string {
    return hit.snippet.slice(0, hit.highlightStart);
  }

  match(hit: SearchHit): string {
    return hit.snippet.slice(hit.highlightStart, hit.highlightStart + hit.highlightLength);
  }

  after(hit: SearchHit): string {
    return hit.snippet.slice(hit.highlightStart + hit.highlightLength);
  }
}
