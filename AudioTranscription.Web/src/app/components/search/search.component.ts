import { Component, computed, inject, signal } from '@angular/core';
import { toObservable, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, debounceTime, distinctUntilChanged, of, switchMap } from 'rxjs';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { SearchService } from '../../services/search.service';
import { PaginatedResult, SearchHit, SearchMode } from '../../models/audio-job.model';
import { PageTitleService } from '../../i18n/page-title.service';
import { OrganizeService } from '../../services/organize.service';

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

      <!-- Words, meaning or both (S23); only offered when the server has semantic search -->
      @if (semanticAvailable()) {
        <fieldset class="mb-6" data-testid="search-modes">
          <legend class="text-xs font-medium mb-1">{{ 'search.mode.legend' | transloco }}</legend>
          <div class="join" role="radiogroup">
            @for (m of modes; track m) {
              <input
                class="join-item btn btn-sm"
                type="radio"
                name="search-mode"
                [attr.data-testid]="'search-mode-' + m"
                [value]="m"
                [checked]="mode() === m"
                [attr.aria-label]="'search.mode.' + m | transloco"
                (change)="mode.set(m)"
              />
            }
          </div>
          <p class="text-xs text-base-content/60 mt-1">{{ 'search.mode.hint.' + mode() | transloco }}</p>
        </fieldset>
      }

      <!-- Optionally narrow the search to one of the user's tags or folders (S20) -->
      @if (mode() === 'literal') {
      <div class="flex flex-wrap gap-3 mb-6">
        <div class="flex flex-col gap-1">
          <label for="search-tag" class="text-xs font-medium">{{ 'search.filter.tag' | transloco }}</label>
          <select id="search-tag" class="select select-sm" data-testid="search-tag" (change)="tag.set($any($event.target).value)">
            <option value="" [selected]="!tag()">{{ 'search.filter.all' | transloco }}</option>
            @for (t of organize.tags(); track t.id) {
              <option [value]="t.id" [selected]="t.id === tag()">{{ t.name }}</option>
            }
          </select>
        </div>
        <div class="flex flex-col gap-1">
          <label for="search-folder" class="text-xs font-medium">{{ 'search.filter.folder' | transloco }}</label>
          <select id="search-folder" class="select select-sm" data-testid="search-folder" (change)="folder.set($any($event.target).value)">
            <option value="" [selected]="!folder()">{{ 'search.filter.all' | transloco }}</option>
            @for (f of organize.folderOptions(); track f.id) {
              <option [value]="f.id" [selected]="f.id === folder()">{{ indent(f.depth) }}{{ f.name }}</option>
            }
          </select>
        </div>
      </div>
      }

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
  readonly organize = inject(OrganizeService);
  readonly tag = signal('');
  readonly folder = signal('');
  readonly loading = signal(false);
  readonly modes: readonly SearchMode[] = ['literal', 'semantic', 'hybrid'];
  readonly mode = signal<SearchMode>('literal');
  /** Whether the server offers semantic and hybrid search (S23). */
  readonly semanticAvailable = signal(false);
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

    // The user's tags and folders for the filters; the search works without them if loading fails
    this.organize.load().catch(() => undefined);

    // Semantic search is optional; without the answer only the literal search is offered
    this.searchService.loadModes().subscribe({
      next: (modes) => this.semanticAvailable.set(modes.semantic),
      error: () => this.semanticAvailable.set(false),
    });

    // The text is debounced; choosing a tag or folder searches again at once
    toObservable(computed(() => ({ query: this.query(), tag: this.tag(), folder: this.folder(), mode: this.mode() })))
      .pipe(
        debounceTime(300),
        distinctUntilChanged((a, b) => a.query === b.query && a.tag === b.tag && a.folder === b.folder && a.mode === b.mode),
        switchMap(({ query, tag, folder, mode }) => {
          const trimmed = query.trim();
          if (!trimmed) {
            this.results.set(null);
            this.loading.set(false);
            return of(null);
          }
          this.loading.set(true);
          return this.searchService
            .search(trimmed, 1, 20, { tag: tag || undefined, folder: folder || undefined }, mode)
            .pipe(
              // A failing semantic search must not leave the page stuck: show the empty state
              catchError(() => of({ items: [], totalCount: 0, page: 1, pageSize: 20 } as PaginatedResult<SearchHit>)),
            );
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

  indent(depth: number): string {
    return '\u00a0\u00a0'.repeat(depth);
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
