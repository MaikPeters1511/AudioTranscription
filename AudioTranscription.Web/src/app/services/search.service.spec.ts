import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SearchService } from './search.service';

describe('SearchService', () => {
  let service: SearchService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(SearchService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends the query, page and pageSize as query parameters', () => {
    service.search('Zauberer', 2, 10).subscribe();

    const req = http.expectOne((r) => r.url === '/api/search');
    expect(req.request.params.get('q')).toBe('Zauberer');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('10');
    req.flush({ items: [], totalCount: 0, page: 2, pageSize: 10 });
  });

  it('defaults to page 1 and a page size of 20', () => {
    service.search('test').subscribe();

    const req = http.expectOne((r) => r.url === '/api/search');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('pageSize')).toBe('20');
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  });

  it('resolves with the paginated hits', () => {
    let result: unknown;
    service.search('Kobold').subscribe((r) => (result = r));

    const hits = { items: [{ jobId: 'a', fileName: 'x.mp3', snippet: 'Ein Kobold', highlightStart: 4, highlightLength: 6 }], totalCount: 1, page: 1, pageSize: 20 };
    http.expectOne((r) => r.url === '/api/search').flush(hits);

    expect(result).toEqual(hits);
  });
});
