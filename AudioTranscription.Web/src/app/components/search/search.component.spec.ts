import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SearchComponent } from './search.component';
import { translocoTesting } from '../../i18n/transloco-testing';

describe('SearchComponent', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      imports: [SearchComponent, translocoTesting('en')],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  function render(tags: unknown[] = [], folders: unknown[] = []) {
    const fixture = TestBed.createComponent(SearchComponent);
    fixture.detectChanges();
    // The filters' tags and folders (S20)
    http.expectOne('/api/tags').flush(tags);
    http.expectOne('/api/folders').flush(folders);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  function type(fixture: ReturnType<typeof render>['fixture'], el: HTMLElement, value: string) {
    const input: HTMLInputElement = el.querySelector('#search-input')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('has a <search> landmark and a labelled input', () => {
    const { el } = render();

    expect(el.querySelector('search')).not.toBeNull();
    expect(el.querySelector('label[for=search-input]')).not.toBeNull();
    expect(el.querySelector('input#search-input')?.getAttribute('type')).toBe('search');
  });

  it('shows a hint and makes no request before anything is typed', () => {
    const { el } = render();

    expect(el.textContent).toContain('Type a word to search');
    http.expectNone((r) => r.url === '/api/search');
  });

  it('debounces requests: only fires once typing settles', () => {
    const { fixture, el } = render();

    type(fixture, el, 'Z');
    vi.advanceTimersByTime(100);
    type(fixture, el, 'Za');
    vi.advanceTimersByTime(100);
    type(fixture, el, 'Zauberer');
    http.expectNone((r) => r.url === '/api/search');

    vi.advanceTimersByTime(300);

    http.expectOne((r) => r.url === '/api/search' && r.params.get('q') === 'Zauberer').flush({
      items: [], totalCount: 0, page: 1, pageSize: 20,
    });
  });

  it('renders a result with the match highlighted via <mark>, using the offsets not HTML', () => {
    const { fixture, el } = render();
    type(fixture, el, 'Zauberer');
    vi.advanceTimersByTime(300);

    http.expectOne((r) => r.url === '/api/search').flush({
      items: [{ jobId: 'job-1', fileName: 'meeting.mp3', snippet: 'Der Zauberer wanderte.', highlightStart: 4, highlightLength: 8 }],
      totalCount: 1, page: 1, pageSize: 20,
    });
    fixture.detectChanges();

    const mark = el.querySelector('mark')!;
    expect(mark.textContent).toBe('Zauberer');
    expect(el.querySelector('a')?.getAttribute('href')).toContain('/jobs/job-1');
    expect(el.textContent).toContain('meeting.mp3');
  });

  it('links to the segment time when the hit has one', () => {
    const { fixture, el } = render();
    type(fixture, el, 'Kobold');
    vi.advanceTimersByTime(300);

    http.expectOne((r) => r.url === '/api/search').flush({
      items: [{ jobId: 'job-2', fileName: 'interview.mp3', snippet: 'Ein Kobold.', highlightStart: 4, highlightLength: 6, segmentStartMs: 1000 }],
      totalCount: 1, page: 1, pageSize: 20,
    });
    fixture.detectChanges();

    expect(el.querySelector('a')?.getAttribute('href')).toBe('/jobs/job-2?t=1000');
  });

  it('does not render a <mark> when there is no literal highlight (stemmed-only match)', () => {
    const { fixture, el } = render();
    type(fixture, el, 'spielen');
    vi.advanceTimersByTime(300);

    http.expectOne((r) => r.url === '/api/search').flush({
      items: [{ jobId: 'job-3', fileName: 'x.mp3', snippet: 'Die Kinder spielten.', highlightStart: 0, highlightLength: 0 }],
      totalCount: 1, page: 1, pageSize: 20,
    });
    fixture.detectChanges();

    expect(el.querySelector('mark')).toBeNull();
    expect(el.textContent).toContain('Die Kinder spielten.');
  });

  it('shows an empty state when there are no results', () => {
    const { fixture, el } = render();
    type(fixture, el, 'Dinosaurier');
    vi.advanceTimersByTime(300);

    http.expectOne((r) => r.url === '/api/search').flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
    fixture.detectChanges();

    expect(el.textContent).toContain('No results for "Dinosaurier"');
  });

  it('clears results and makes no request when the input is cleared', () => {
    const { fixture, el } = render();
    type(fixture, el, 'Zauberer');
    vi.advanceTimersByTime(300);
    http.expectOne((r) => r.url === '/api/search').flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
    fixture.detectChanges();

    type(fixture, el, '');
    vi.advanceTimersByTime(300);

    http.expectNone((r) => r.url === '/api/search');
    expect(el.textContent).toContain('Type a word to search');
  });

  it('offers the user\'s tags and folders and narrows the search to the chosen one at once', async () => {
    const { fixture, el } = render(
      [{ id: 'tag-1', name: 'Budget' }],
      [{ id: 'f-1', name: 'Projekte' }, { id: 'f-2', name: 'Alpha', parentId: 'f-1' }],
    );
    await vi.advanceTimersByTimeAsync(0); // the tags and folders arrive
    fixture.detectChanges();
    type(fixture, el, 'Zauberer');
    vi.advanceTimersByTime(300);
    http.expectOne((r) => r.url === '/api/search').flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });

    const folders = el.querySelectorAll('#search-folder option');
    expect(Array.from(folders).map((o) => o.textContent?.trim())).toEqual(['All', 'Projekte', 'Alpha']);
    const tag = el.querySelector('#search-tag') as HTMLSelectElement;
    tag.value = 'tag-1';
    tag.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    vi.advanceTimersByTime(300);

    const request = http.expectOne((r) => r.url === '/api/search');
    expect(request.request.params.get('tag')).toBe('tag-1');
    expect(request.request.params.has('folder')).toBe(false);
    request.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  });

  it('labels the filters', () => {
    const { el } = render();

    expect(el.querySelector('label[for=search-tag]')).not.toBeNull();
    expect(el.querySelector('label[for=search-folder]')).not.toBeNull();
  });
});
